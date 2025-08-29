using System;
using System.Collections;
using System.Collections.Generic;
using DroneController;
using UnityEngine;
using TMPro;
#if UNITY_XR_MANAGEMENT || ENABLE_VR
using UnityEngine.XR;
#endif

/// <summary>
/// Controller-driven defense skills for Meta Quest:
/// - Press A (right controller) -> Paper (attack beam).
/// - Press B (right controller) -> Stop (freeze projectile).
/// Origin & direction come from the right controller pose.
/// </summary>
public class DefenseSkillController : MonoBehaviour
{
    [Header("XR (optional)")]
    [Tooltip("Optional: HMD interface for fallbacks (not strictly required).")]
    [SerializeField] private UnityEngine.Object _hmdObj; // IHmd (from Oculus.Interaction), optional
    private object _hmd; // kept as object to avoid hard dependency

    [Header("Controller Poses")]
    [Tooltip("Right controller transform (e.g., RightHandAnchor). Strongly recommended to assign.")]
    [SerializeField] private Transform rightController;
    [Tooltip("Optional: Left controller transform if you want to mirror inputs later.")]
    [SerializeField] private Transform leftController;
    [Tooltip("If true, flatten forward direction to horizontal (y=0).")]
    [SerializeField] private bool flattenForward = false;
    [Tooltip("Start the ray/effect this far in front of the controller.")]
    [SerializeField] private float startOffsetMeters = 0.30f;

    [Header("Raycast / Hit Settings")]
    [SerializeField] private LayerMask droneLayerMask = ~0;
    [SerializeField] private float raySphereRadius = 0.06f;
    [SerializeField] private float maxRayDistance = 10f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Paper (Attack)")]
    [SerializeField] private float paperDamage = 1f;

    [Header("Stop (Freeze)")]
    [SerializeField] private float stopDamage = 1f;
    [SerializeField] private float freezeSeconds = 2f;

    [Header("Stop VFX (Projectile)")]
    [SerializeField] private GameObject stopProjectilePrefab;   // projectile visual (no collider required)
    [SerializeField] private int stopProjectilePoolSize = 6;
    [SerializeField] private float stopProjectileSpeed = 20f;
    [SerializeField] private bool stopProjectileFaceDirection = true;

    [Header("Paper Beam FX (pooled)")]
    [SerializeField] private GameObject beamPrefab; // prefab with LineRenderer (optional)
    [SerializeField] private int beamPoolSize = 8;
    [SerializeField] private float beamWidth = 0.02f;
    [SerializeField] private float beamLifetime = 0.12f;

    [Header("Gameplay")]
    [Tooltip("Debounce between activations in seconds.")]
    [SerializeField] private float retriggerCooldown = 0.15f;
    [SerializeField] private bool debugLogs = false;

    // --- runtime ---
    private Queue<LineRenderer> _beamPool;
    private Queue<GameObject> _stopProjectilePool;
    private float _lastPaperTime = -999f;
    private float _lastStopTime = -999f;

    private bool _isButtonAPressed = false;
    private bool _isButtonBPressed = false;
    private void OnEnable()
    {
        InputManager.onGripPress += OnGripPress;
    }

    private void OnDisable()
    {
        InputManager.onGripPress -= OnGripPress;
    }

    private void OnGripPress(bool isPressed, bool isLeft)
    {
        if (isPressed)
        {
            _isButtonAPressed = isLeft;
            _isButtonBPressed = !isLeft;
        }
    }

    private void Awake()
    {
        // Keep IHmd as object to avoid requiring Oculus.Interaction namespaces here
        _hmd = _hmdObj;
    }

    private void Start()
    {
        InitBeamPool();
        InitStopProjectilePool();
    }

    private void Update()
    {
        if (_isButtonAPressed && Time.time - _lastPaperTime >= retriggerCooldown)
        {
            _lastPaperTime = Time.time;
            if (TryGetLeftControllerPose(out var origin, out var dir))
            {
                var unique = new HashSet<DroneHealth>();
                ShootPaper(origin, dir, unique);
            }
        }

        if (_isButtonBPressed && Time.time - _lastStopTime >= retriggerCooldown)
        {
            _lastStopTime = Time.time;
            if (TryGetRightControllerPose(out var origin, out var dir))
            {
                var unique = new HashSet<DroneHealth>();
                ShootStop(origin, dir, unique);
            }
        }

        _isButtonAPressed = false;
        _isButtonBPressed = false;
    }

    // ========= Controller Input & Pose =========

    /// <summary>Try to get right controller origin & forward (optionally flattened).</summary>
    private bool TryGetRightControllerPose(out Vector3 origin, out Vector3 dir)
    {
        origin = default; dir = default;

        // Preferred: assigned transform
        if (rightController != null)
        {
            origin = rightController.position;
            dir = rightController.forward;
            if (flattenForward) dir = Flat(dir);
            origin += dir * startOffsetMeters;
            return true;
        }
        return false;
    }
    
    private bool TryGetLeftControllerPose(out Vector3 origin, out Vector3 dir)
    {
        origin = default; dir = default;

        // Preferred: assigned transform
        if (leftController != null)
        {
            origin = leftController.position;
            dir = leftController.forward;
            if (flattenForward) dir = Flat(dir);
            origin += dir * startOffsetMeters;
            return true;
        }
        return false;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ========= Skills =========

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

    // ========= Projectile (Stop) =========

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
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere); // fallback visual

        go.SetActive(false);

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
                health.ApplyDamage(stopDamage);
                if (debugLogs) Debug.Log($"[Stop] Hit {health.name} freeze={freezeSeconds}s dmg={stopDamage}");
            }
        }
    }

    // ========= Beam (Paper) =========

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

    // ========= Ray Helpers =========

    private bool SphereRay(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        dir.Normalize();

        if (Physics.SphereCast(origin, raySphereRadius, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction))
            return true;

        return Physics.Raycast(origin, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction);
    }
}