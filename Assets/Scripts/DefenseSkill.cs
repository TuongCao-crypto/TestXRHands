using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Oculus.Interaction;          // ActiveStateSelector
using Oculus.Interaction.Input;    // IHmd, HandRef

public class DefenseSkill : MonoBehaviour
{
    private const int PAPER_LEFT_INDEX = 0; // paper pose: attack beam
    private const int PAPER_RIGHT_INDEX = 1; // paper pose: attack beam
    private const int STOP_LEFT_INDEX  = 2; // stop pose : freeze
    private const int STOP_RIGHT_INDEX  = 3; // stop pose : freeze

    [Header("XR")]
    [SerializeField, Interface(typeof(IHmd))]
    private UnityEngine.Object _hmd;
    private IHmd Hmd { get; set; }

    [Header("Poses (size=2: [0]=Paper, [1]=Stop)")]
    [SerializeField] private ActiveStateSelector[] _poses;     // 0: paper, 1: stop
    [SerializeField] private Material[] _onSelectIcons;        // icon gắn vào prefab visual (optional)
    [SerializeField] private GameObject _poseActiveVisualPrefab;

    private GameObject[] _poseActiveVisuals;

    [Header("Raycast/Hit Settings")]
    [SerializeField] private LayerMask droneLayerMask = ~0;  // set layer Drone trong Inspector
    [SerializeField] private float raySphereRadius = 0.06f;  // SphereCast để dễ trúng hơn
    [SerializeField] private float maxRayDistance = 10f;     // beam bay thẳng 10m nếu không trúng
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Paper (Attack)")]
    [SerializeField] private float paperDamage = 1f;
    const float startOffsetMeters = 0.30f; 

    [Header("Stop (Freeze)")]
    [SerializeField] private float stopDamage = 1f;
    [SerializeField] private float freezeSeconds = 2f;
    [Header("Palm Aim")]
    [SerializeField] private bool invertPalmForward = false;

    [Header("Beam FX (pooled)")]
    [SerializeField] private GameObject beamPrefab;   // prefab có LineRenderer
    [SerializeField] private int beamPoolSize = 8;
    [SerializeField] private float beamWidth = 0.02f;
    [SerializeField] private float beamLifetime = 0.12f;

    [Header("Stability")]
    [SerializeField] private float retriggerCooldown = 0.25f; // chống flicker pose spam
    [SerializeField] private bool  debugLogs = false;

    // --- runtime ---
    private readonly List<Action> _subsOnSelected = new();
    private readonly List<Action> _subsOnUnselected = new();
    private float[] _lastCastTimes;   // per pose cooldown
    private Queue<LineRenderer> _beamPool;
    
    [Header("Editor Test")]
    [SerializeField] private bool editorTestMode = true;     // bật/tắt test trong Editor
    [SerializeField] private KeyCode paperKey = KeyCode.Mouse0; // chuột trái = Paper (Attack)
    [SerializeField] private KeyCode stopKey  = KeyCode.Mouse1; // chuột phải = Stop (Freeze)
    [SerializeField] private Transform editorAimSource;       // nếu để trống sẽ dùng Camera.main
    [SerializeField] private bool useCameraWhenNoAimSource = true;

    protected virtual void Awake()
    {
        Hmd = _hmd as IHmd;
    }

    protected virtual void Start()
    {
        // --- Validate ---
        this.AssertField(Hmd, nameof(Hmd));
        this.AssertField(_poseActiveVisualPrefab, nameof(_poseActiveVisualPrefab));
        this.AssertField(_poses, nameof(_poses));
        if (_poses.Length < 2)
        {
            Debug.LogError("[DefenseSkill] _poses cần 2 phần tử: [0]=Paper, [1]=Stop");
            enabled = false;
            return;
        }

        // --- Init visuals ---
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

        // --- Subscribe pose events (and remember delegates to unsubscribe later) ---
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

            _poses[i].WhenSelected   += onSel;
            _poses[i].WhenUnselected += onUnsel;
        }

        // --- Beam pool ---
        InitBeamPool();
    }

    private void OnDestroy()
    {
        // Unsubscribe để tránh leak / gọi vào object đã Destroy
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

    // ---------------- UI/Visuals ----------------

    private void ShowVisuals(int poseNumber)
    {
        if (!Hmd.TryGetRootPose(out Pose hmdPose)) return;

        Vector3 spawnSpot = hmdPose.position + hmdPose.forward * 0.3f;
        _poseActiveVisuals[poseNumber].transform.position = spawnSpot;
        _poseActiveVisuals[poseNumber].transform.LookAt(2 * _poseActiveVisuals[poseNumber].transform.position - hmdPose.position);

        var hands = _poses[poseNumber].GetComponents<HandRef>();
        Vector3 visualsPos = Vector3.zero;
        int count = 0;
        foreach (var hand in hands)
        {
            if (!hand.GetRootPose(out Pose wristPose)) continue;
            visualsPos += wristPose.position + wristPose.forward * .15f + Vector3.up * .02f;
            count++;
        }
        if (count > 0)
            _poseActiveVisuals[poseNumber].transform.position = visualsPos / count;

        _poseActiveVisuals[poseNumber].SetActive(true);
    }

    private void HideVisuals(int poseNumber)
    {
        if (_poseActiveVisuals != null && poseNumber < _poseActiveVisuals.Length)
            _poseActiveVisuals[poseNumber].SetActive(false);
    }

    // ---------------- Skill Triggering ----------------

    private void TryTriggerSkill(int poseIndex)
    {
        // chống spam do pose state flicker
        if (Time.unscaledTime - _lastCastTimes[poseIndex] < retriggerCooldown)
            return;
        _lastCastTimes[poseIndex] = Time.unscaledTime;

        switch (poseIndex)
        {
            case PAPER_LEFT_INDEX: 
            case PAPER_RIGHT_INDEX: 
                FirePaperBeamFromAllHands(poseIndex); 
                break;
            case STOP_LEFT_INDEX:
            case STOP_RIGHT_INDEX:
                CastStopFromAllHands(poseIndex); 
                break;
            default:
                if (debugLogs) Debug.Log($"[DefenseSkill] Unknown pose index {poseIndex}");
                break;
        }
    }

    // ---- Paper Pose: bắn tia từ các tay (fallback HMD nếu không có tay) ----
    private void FirePaperBeamFromAllHands(int poseIndex)
    {
        var hands = _poses[poseIndex].GetComponents<HandRef>();
        bool any = false;
        var uniqueHits = new HashSet<DroneHealth>();

        foreach (var hand in hands)
        {
            if (!hand.GetRootPose(out Pose wristPose)) continue;
            any = true;
            ShootPaper(wristPose.position, wristPose.forward, uniqueHits);
        }
        if (!any && Hmd.TryGetRootPose(out Pose hmdPose))
        {
            ShootPaper(hmdPose.position, hmdPose.forward, uniqueHits);
        }
    }
    
    private void ShootPaper(Vector3 origin, Vector3 dir, HashSet<DroneHealth> unique)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        dir = dir.normalized;

        Vector3 start = origin + dir * startOffsetMeters;

        if (SphereRay(start, dir, out RaycastHit hit))
        {
            DrawBeam(start, hit.point);

            var health = hit.collider.GetComponentInParent<DroneHealth>();

            if (health && unique.Add(health))
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

    // ---- Stop Pose: Freeze + Damage ----
    private void CastStopFromAllHands(int poseIndex)
    {
        var hands = _poses[poseIndex].GetComponents<HandRef>();
        bool any = false;
        var uniqueHits = new HashSet<DroneHealth>();

        foreach (var hand in hands)
        {
            if (TryGetPalmAim(hand, out var origin, out var dir))
            {
                any = true;
                ShootStop(origin, dir, uniqueHits);
            }
        }

        // Fallback HMD nếu không thấy tay
        if (!any && Hmd.TryGetRootPose(out Pose hmdPose))
        {
            ShootStop(hmdPose.position, hmdPose.forward, uniqueHits);
        }
    }
    
    private bool TryGetPalmAim(HandRef hand, out Vector3 origin, out Vector3 dir)
    {
        origin = default;
        dir = default;

        // Ưu tiên lấy đúng khớp Palm nếu version SDK hỗ trợ
        // HandRef trong Oculus Interaction hỗ trợ GetJointPose(HandJointId, out Pose)
        Pose palmPose;
        try
        {
            if (hand.GetJointPose(HandJointId.HandPalm, out palmPose))
            {
                dir = palmPose.forward;                 // pháp tuyến lòng bàn tay
                if (invertPalmForward) dir = -dir;      // đảo hướng nếu cần
                dir.Normalize();

                origin = palmPose.position + dir * startOffsetMeters; // bắt đầu cách lòng bàn tay 3cm
                return true;
            }
        }
        catch { /* một số version không có Palm -> fallback */ }

        // Fallback: suy đoán từ cổ tay
        if (hand.GetRootPose(out Pose wristPose))
        {
            // Heuristic: tịnh tiến ra giữa lòng bàn tay ~ 6cm từ cổ tay
            // Dùng forward của cổ tay làm pháp tuyến lòng bàn tay (nếu sai, bật invert hoặc đổi sang wristPose.up)
            dir = wristPose.forward;
            if (invertPalmForward) dir = -dir;
            dir.Normalize();

            origin = wristPose.position + dir * (0.06f + startOffsetMeters);
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
            DrawBeam(origin, hit.point);

            var health = hit.collider.attachedRigidbody
                ? hit.collider.attachedRigidbody.GetComponentInParent<DroneHealth>()
                : hit.collider.GetComponentInParent<DroneHealth>();

            if (health && unique.Add(health))
            {
                health.FreezeAndDamage(freezeSeconds, stopDamage);
                if (debugLogs) Debug.Log($"[Stop] Hit {health.name} freeze={freezeSeconds}s dmg={stopDamage}");
            }
        }
        else
        {
            DrawBeam(origin, origin + dir * maxRayDistance);
        }
    }

    // ---------------- Ray Helpers ----------------

    private bool SphereRay(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        dir.Normalize();
        // ưu tiên SphereCast, giảm miss trên thiết bị
        if (Physics.SphereCast(origin, raySphereRadius, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction))
            return true;

        // Fallback Raycast mảnh
        //return Physics.Raycast(origin, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction);

        return false;
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
            // bắn Paper một phát (tia 10m, gây damage nếu trúng)
            var unique = new System.Collections.Generic.HashSet<DroneHealth>();
            ShootPaper(src.position, src.forward, unique); // dùng đúng hàm nội bộ của bạn
        }
        if (Input.GetKeyDown(stopKey))
        {
            // bắn Stop (đóng băng 2s + damage)
            var unique = new System.Collections.Generic.HashSet<DroneHealth>();
            ShootStop(src.position, src.forward, unique);
        }
    }
#endif
}
