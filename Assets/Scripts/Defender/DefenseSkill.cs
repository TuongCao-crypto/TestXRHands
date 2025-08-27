using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Oculus.Interaction; // ActiveStateSelector
using Oculus.Interaction.Input; // IHmd, HandRef

public class DefenseSkill : MonoBehaviour
{
    private const int PAPER_LEFT_INDEX = 0; // paper pose: attack beam (left)
    private const int PAPER_RIGHT_INDEX = 1; // paper pose: attack beam (right)
    private const int STOP_LEFT_INDEX = 2; // stop pose: freeze (left)
    private const int STOP_RIGHT_INDEX = 3; // stop pose: freeze (right)

    [Header("XR")] [SerializeField, Interface(typeof(IHmd))]
    private UnityEngine.Object _hmd;

    private IHmd Hmd { get; set; }

    [Header("Poses (size=4: 0=PaperL,1=PaperR,2=StopL,3=StopR)")] [SerializeField]
    private ActiveStateSelector[] _poses; // 0: paperL, 1: paperR, 2: stopL, 3: stopR

    [SerializeField] private Material[] _onSelectIcons; // optional icon material for the visual prefab
    [SerializeField] private GameObject _poseActiveVisualPrefab;

    private GameObject[] _poseActiveVisuals;

    [Header("Raycast / Hit Settings")] [SerializeField]
    private LayerMask droneLayerMask = ~0; // set Drone layer here

    [SerializeField] private float raySphereRadius = 0.06f; // SphereCast radius to make hits easier
    [SerializeField] private float maxRayDistance = 10f; // if miss, beam extends straight to this distance
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Paper (Attack)")] [SerializeField]
    private float paperDamage = 1f;

    const float startOffsetMeters = 0.30f; // beam starts 30 cm from the hand/palm along the firing direction

    [Header("Stop (Freeze)")] [SerializeField]
    private float stopDamage = 1f;
    [SerializeField] private float freezeSeconds = 2f;
    [SerializeField] private GameObject stopProjectilePrefab;   // VFX that flies forward (no collider needed)
    [SerializeField] private int stopProjectilePoolSize = 6;    // pool size for stop projectiles
    [SerializeField] private float stopProjectileSpeed = 20f;   // meters per second
    [SerializeField] private bool stopProjectileFaceDirection = true;
    private Queue<GameObject> _stopProjectilePool;

    [Header("Beam FX (Pooled)")] [SerializeField]
    private GameObject beamPrefab; // prefab containing a LineRenderer (optional)

    [SerializeField] private int beamPoolSize = 8;
    [SerializeField] private float beamWidth = 0.02f;
    [SerializeField] private float beamLifetime = 0.12f;

    [Header("Stability")] [SerializeField]
    private float retriggerCooldown = 0.25f; // debounce to avoid pose flicker spam

    [SerializeField] private bool debugLogs = false;

    // --- runtime ---
    private readonly List<Action> _subsOnSelected = new();
    private readonly List<Action> _subsOnUnselected = new();
    private float[] _lastCastTimes; // per-pose cooldown registry
    private Queue<LineRenderer> _beamPool;

    [Header("Editor Test")] [SerializeField]
    private bool editorTestMode = true; // enable keyboard/mouse testing in Editor

    [SerializeField] private KeyCode paperKey = KeyCode.Mouse0; // LMB = Paper (Attack)
    [SerializeField] private KeyCode stopKey = KeyCode.Mouse1; // RMB = Stop (Freeze)
    [SerializeField] private Transform editorAimSource; // if null uses Camera.main
    [SerializeField] private bool useCameraWhenNoAimSource = true;

    protected virtual void Awake()
    {
        Hmd = _hmd as IHmd;
    }

    protected virtual void Start()
    {
        // --- Validate required fields ---
        this.AssertField(Hmd, nameof(Hmd));
        this.AssertField(_poseActiveVisualPrefab, nameof(_poseActiveVisualPrefab));
        this.AssertField(_poses, nameof(_poses));
        if (_poses.Length < 4)
        {
            Debug.LogError("[DefenseSkill] _poses must have 4 elements: 0=PaperL,1=PaperR,2=StopL,3=StopR");
            enabled = false;
            return;
        }

        // --- Create visual helpers for each pose ---
        _poseActiveVisuals = new GameObject[_poses.Length];
        for (int i = 0; i < _poses.Length; i++)
        {
            _poseActiveVisuals[i] = Instantiate(_poseActiveVisualPrefab);
            var tmp = _poseActiveVisuals[i].GetComponentInChildren<TextMeshPro>();
            if (tmp) tmp.text = _poses[i].name;

            var psr = _poseActiveVisuals[i].GetComponentInChildren<ParticleSystemRenderer>();
            if (psr && i < _onSelectIcons.Length && _onSelectIcons[i] != null)
                psr.material = _onSelectIcons[i];

            _poseActiveVisuals[i].SetActive(false);
        }

        // --- Subscribe to pose selection events ---
        _lastCastTimes = new float[_poses.Length];
        for (int i = 0; i < _poses.Length; i++)
        {
            int poseIndex = i;
            Action onSel = () =>
            {
                ShowVisuals(poseIndex);
                TryTriggerSkill(poseIndex);
            };
            Action onUnsel = () => HideVisuals(poseIndex);

            _subsOnSelected.Add(onSel);
            _subsOnUnselected.Add(onUnsel);

            _poses[i].WhenSelected += onSel;
            _poses[i].WhenUnselected += onUnsel;
        }

        // --- Initialize beam pool ---
        InitBeamPool();
        InitStopProjectilePool();
    }

    private void OnDestroy()
    {
        // Clean up subscriptions to avoid leaks or callbacks after destroy
        if (_poses != null)
        {
            for (int i = 0; i < _poses.Length; i++)
            {
                if (i < _subsOnSelected.Count && _subsOnSelected[i] != null)
                    _poses[i].WhenSelected -= _subsOnSelected[i];
                if (i < _subsOnUnselected.Count && _subsOnUnselected[i] != null)
                    _poses[i].WhenUnselected -= _subsOnUnselected[i];
            }
        }
    }

    // ---------------- UI / Visuals ----------------

    private void ShowVisuals(int poseNumber)
    {
        if (!Hmd.TryGetRootPose(out Pose hmdPose)) return;

        // spawn near HMD, then snap near the corresponding wrist if available
        Vector3 spawnSpot = hmdPose.position + hmdPose.forward * 0.3f;
        _poseActiveVisuals[poseNumber].transform.position = spawnSpot;
        _poseActiveVisuals[poseNumber].transform.LookAt(
            2 * _poseActiveVisuals[poseNumber].transform.position - hmdPose.position);

        var hand = GetHandForPose(poseNumber);
        if (hand != null && hand.GetRootPose(out Pose wristPose))
        {
            _poseActiveVisuals[poseNumber].transform.position =
                wristPose.position + wristPose.forward * .15f + Vector3.up * .02f;
        }

        _poseActiveVisuals[poseNumber].SetActive(true);
    }

    private void HideVisuals(int poseNumber)
    {
        if (_poseActiveVisuals != null && poseNumber < _poseActiveVisuals.Length)
            _poseActiveVisuals[poseNumber].SetActive(false);
    }

    // ---------------- Helpers ----------------

    private HandRef GetHandForPose(int poseIndex)
    {
        return _poses[poseIndex].GetComponent<HandRef>();
    }

    // ---------------- Skill Triggering ----------------

    private void TryTriggerSkill(int poseIndex)
    {
        if(GameManager.Instance.GameStage != EGameStage.Live) return;
        
        // Debounce to prevent rapid re-trigger from pose flicker
        if (Time.unscaledTime - _lastCastTimes[poseIndex] < retriggerCooldown)
            return;
        _lastCastTimes[poseIndex] = Time.unscaledTime;

        switch (poseIndex)
        {
            case PAPER_LEFT_INDEX:
            case PAPER_RIGHT_INDEX:
                FirePaperBeamFromPose(poseIndex);
                break;

            case STOP_LEFT_INDEX:
            case STOP_RIGHT_INDEX:
                CastStopFromPose(poseIndex);
                break;

            default:
                if (debugLogs) Debug.Log($"[DefenseSkill] Unknown pose index {poseIndex}");
                break;
        }
    }

    // ---- Paper: fire from the pose's hand (fallback to HMD if hand missing) ----
    private void FirePaperBeamFromPose(int poseIndex)
    {
        var hand = GetHandForPose(poseIndex);
        var uniqueHits = new HashSet<DroneHealth>();
        if (hand != null && hand.GetRootPose(out Pose wristPose))
        {
            ShootPaper(wristPose.position, wristPose.forward, uniqueHits);
            return;
        }

        // Fallback to HMD
        if (Hmd.TryGetRootPose(out Pose hmdPose))
            ShootPaper(hmdPose.position, hmdPose.forward, uniqueHits);
    }

    private void ShootPaper(Vector3 origin, Vector3 dir, HashSet<DroneHealth> unique)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        dir = dir.normalized;

        // Start 30 cm in front of the hand/wrist
        Vector3 start = origin + dir * startOffsetMeters;

        if (SphereRay(start, dir, out RaycastHit hit))
        {
            DrawBeam(start, hit.point);

            var health = hit.collider.GetComponentInParent<DroneHealth>();
            if (health != null && unique.Add(health))
            {
                health.ApplyDamage(paperDamage);
                if (debugLogs) Debug.Log($"[Paper] Hit {health.name} dmg={paperDamage}");
            }
        }
        else
        {
            Vector3 end = start + dir * maxRayDistance;
            DrawBeam(start, end);
        }
    }

    // ---- Stop: freeze from the palm normal (fallback to HMD if hand missing) ----
    private void CastStopFromPose(int poseIndex)
    {
        var hand = GetHandForPose(poseIndex);
        var uniqueHits = new HashSet<DroneHealth>();

        if (hand != null && TryGetPalmAim(hand, out var origin, out var dir))
        {
            ShootStop(origin, dir, uniqueHits);
            return;
        }

        // Fallback to HMD
        if (Hmd.TryGetRootPose(out Pose hmdPose))
        {
            ShootStop(hmdPose.position, hmdPose.forward, uniqueHits);
        }
    }

    private bool TryGetPalmAim(HandRef hand, out Vector3 origin, out Vector3 dir)
    {
        origin = default;
        dir = default;

        const float EPS = 1e-6f;
        Vector3 baseDir;

        // 1) Prefer the Palm joint
        if (hand.GetJointPose(HandJointId.HandPalm, out Pose palmPose))
        {
            // Common Oculus rigs: outward palm normal ≈ -palmPose.up
            baseDir = -palmPose.up; // flip if your rig is opposite

            // Flatten to horizontal so the beam is always parallel to the ground
            dir = Vector3.ProjectOnPlane(baseDir, Vector3.up);

            if (dir.sqrMagnitude < EPS)
            {
                // Fallback: use wrist forward (flattened)
                if (hand.GetRootPose(out Pose wristFromPalm))
                    dir = Vector3.ProjectOnPlane(wristFromPalm.forward, Vector3.up);
            }

            if (dir.sqrMagnitude < EPS)
            {
                dir = Vector3.ProjectOnPlane(Vector3.forward, Vector3.up);
            }

            dir.y = 0f; // ensure perfectly horizontal
            if (dir.sqrMagnitude < EPS) return false;
            dir.Normalize();

            origin = palmPose.position + dir * startOffsetMeters;
            return true;
        }

        // 2) Fallback: estimate palm plane from wrist + middle fingertip
        Pose wristEst, middleTip;
        bool hasWrist = hand.GetJointPose(HandJointId.HandWristRoot, out wristEst);
        bool hasMid = hand.GetJointPose(HandJointId.HandMiddle3, out middleTip);

        if (hasWrist && hasMid)
        {
            Vector3 palmCenter = Vector3.Lerp(wristEst.position, middleTip.position, 0.6f);
            Vector3 axis = (middleTip.position - wristEst.position).normalized;
            Vector3 approxRight = wristEst.right;
            baseDir = Vector3.Cross(axis, approxRight).normalized;

            dir = Vector3.ProjectOnPlane(baseDir, Vector3.up);
            if (dir.sqrMagnitude < EPS)
                dir = Vector3.ProjectOnPlane(wristEst.forward, Vector3.up);

            if (dir.sqrMagnitude < EPS)
                dir = Vector3.ProjectOnPlane(Vector3.forward, Vector3.up);

            dir.y = 0f;
            if (dir.sqrMagnitude < EPS) return false;
            dir.Normalize();

            origin = palmCenter + dir * startOffsetMeters;
            return true;
        }

        // 3) Last fallback: wrist forward (flattened)
        if (hand.GetRootPose(out Pose wristFinal))
        {
            baseDir = wristFinal.forward;
            dir = Vector3.ProjectOnPlane(baseDir, Vector3.up);
            dir.y = 0f;

            if (dir.sqrMagnitude < EPS) return false;
            dir.Normalize();

            origin = wristFinal.position + dir * (0.06f + startOffsetMeters);
            return true;
        }

        return false;

    }

    private void ShootStop(Vector3 origin, Vector3 dir, HashSet<DroneHealth> unique)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        if (SphereRay(origin, dir, out RaycastHit hit))
        {
            // Visual: spawn projectile from palm to hit point
            PlayStopProjectile(origin, hit.point, hit.collider);

        }
        else
        {
            // Miss: fly straight to max range along the same (already-flattened) direction
            Vector3 end = origin + dir * maxRayDistance;
            PlayStopProjectile(origin, end, null);
        }
    }
    private void PlayStopProjectile(Vector3 start, Vector3 end, Collider hit)
    {
        if (_stopProjectilePool == null || _stopProjectilePool.Count == 0)
            _stopProjectilePool?.Enqueue(CreateStopProjectile());

        var go = _stopProjectilePool.Dequeue();
        go.transform.position = start;
        if (stopProjectileFaceDirection)
            go.transform.rotation = Quaternion.LookRotation((end - start).sqrMagnitude > 1e-6f ? (end - start).normalized : Vector3.forward);

        go.SetActive(true);
        StartCoroutine(MoveStopProjectile(go, start, end, hit));
    }

    private IEnumerator MoveStopProjectile(GameObject go, Vector3 start, Vector3 end, Collider hit)
    {
        float dist = Vector3.Distance(start, end);
        float speed = Mathf.Max(0.01f, stopProjectileSpeed);
        float dur = dist / speed;

        float t = 0f;
        while (t < dur && go != null && go.activeSelf)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            go.transform.position = Vector3.Lerp(start, end, u);
            yield return null;
        }

        if (go != null)
        {
            go.SetActive(false);
            _stopProjectilePool.Enqueue(go);
        }

        if (hit != null)
        {
            var health = hit.GetComponentInParent<DroneHealth>();
            if (health != null)
            {
                //health.FreezeAndDamage(freezeSeconds, stopDamage);
                health.ApplyDamage(stopDamage);
                if (debugLogs) Debug.Log($"[Stop] Hit {health.name} freeze={freezeSeconds}s dmg={stopDamage}");
            }
        }
    }
    private void InitStopProjectilePool()
    {
        _stopProjectilePool = new Queue<GameObject>(Mathf.Max(1, stopProjectilePoolSize));
        for (int i = 0; i < stopProjectilePoolSize; i++)
            _stopProjectilePool.Enqueue(CreateStopProjectile());
    }

    private GameObject CreateStopProjectile()
    {
        GameObject go;
        if (stopProjectilePrefab != null)
            go = Instantiate(stopProjectilePrefab);
        else
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere); // simple fallback visual

        go.SetActive(false);

        // If we used a primitive fallback, remove collider & shadows
        var col = go.GetComponent<Collider>();
        if (col) Destroy(col);

        var r = go.GetComponent<Renderer>();
        if (r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        return go;
    }

    // ---------------- Ray Helpers ----------------

    private bool SphereRay(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        dir.Normalize();

        // Prefer SphereCast to reduce misses on device due to small colliders
        if (Physics.SphereCast(origin, raySphereRadius, dir, out hit, maxRayDistance, droneLayerMask,
                triggerInteraction))
            return true;

        // Fallback to a thin Raycast if SphereCast misses
        return Physics.Raycast(origin, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction);
    }

    // ---------------- Beam Pooling ----------------

    private void InitBeamPool()
    {
        _beamPool = new Queue<LineRenderer>(beamPoolSize);
        for (int i = 0; i < beamPoolSize; i++)
            _beamPool.Enqueue(CreateBeam());
    }

    private LineRenderer CreateBeam()
    {
        GameObject go = beamPrefab ? Instantiate(beamPrefab) : new GameObject("Beam");
        go.SetActive(false);

        LineRenderer lr = go.GetComponent<LineRenderer>();
        if (!lr) lr = go.AddComponent<LineRenderer>();

        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = beamWidth;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    private void DrawBeam(Vector3 start, Vector3 end)
    {
        LineRenderer lr = (_beamPool.Count > 0) ? _beamPool.Dequeue() : CreateBeam();
        lr.gameObject.SetActive(true);
        lr.enabled = true;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        StartCoroutine(ReleaseBeamAfter(lr, beamLifetime));
    }

    private IEnumerator ReleaseBeamAfter(LineRenderer lr, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (lr)
        {
            lr.enabled = false;
            lr.gameObject.SetActive(false);
            _beamPool.Enqueue(lr);
        }
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (!editorTestMode) return;

        Transform src = editorAimSource;
        if (src == null && useCameraWhenNoAimSource && Camera.main != null)
            src = Camera.main.transform;
        if (src == null) return;

        if (Input.GetKeyDown(paperKey))
        {
            var unique = new HashSet<DroneHealth>();
            ShootPaper(src.position, src.forward, unique);
        }

        if (Input.GetKeyDown(stopKey))
        {
            var unique = new HashSet<DroneHealth>();
            ShootStop(src.position, src.forward, unique);
        }
    }
#endif
}