using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI that auto-uses Defense skills (Paper/Stop) with a cannon model:
/// - Smoothly rotates a cannon pivot toward the pending shot direction.
/// - Random fire cadence [minFireDelay, maxFireDelay].
/// - Hit probability (hitChance); misses deviate aim within a cone.
/// - Prefers Stop when ready, else Paper.
/// </summary>
public class DefenseSkillAI : MonoBehaviour
{
    [Header("Origin & Aiming")]
    [Tooltip("Fire origin (e.g., a muzzle transform under the cannon). If null, uses this transform.")]
    [SerializeField] private Transform fireOrigin;
    [Tooltip("Rotate this pivot to aim the cannon (usually a parent of fireOrigin).")]
    [SerializeField] private Transform cannonPivot;
    [Tooltip("If true, rotate only around Y (horizontal yaw).")]
    [SerializeField] private bool yawOnly = false;
    [Tooltip("If true, flatten shot direction to horizontal (dir.y=0).")]
    [SerializeField] private bool flattenForward = false;
    [Tooltip("Ray/effect starts this far in front of fire origin.")]
    [SerializeField] private float startOffsetMeters = 0.30f;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 15f;
    [SerializeField] private float scanInterval = 0.4f;
    [SerializeField] private LayerMask droneLayerMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Raycast Settings")]
    [SerializeField] private float raySphereRadius = 0.06f;
    [SerializeField] private float maxRayDistance = 10f;

    [Header("Paper (Attack Beam)")]
    [SerializeField] private float paperDamage = 1f;
    [SerializeField] private float paperCooldown = 0.25f;

    [Header("Stop (Freeze Projectile)")]
    [SerializeField] private float stopDamage = 1f;
    [SerializeField] private float freezeSeconds = 2f;
    [SerializeField] private float stopCooldown = 1.25f;

    [Header("Stop VFX (Projectile)")]
    [SerializeField] private GameObject stopProjectilePrefab;
    [SerializeField] private int stopProjectilePoolSize = 6;
    [SerializeField] private float stopProjectileSpeed = 20f;
    [SerializeField] private bool stopProjectileFaceDirection = true;

    [Header("Paper Beam FX (pooled)")]
    [SerializeField] private GameObject beamPrefab;
    [SerializeField] private int beamPoolSize = 8;
    [SerializeField] private float beamWidth = 0.02f;
    [SerializeField] private float beamLifetime = 0.12f;

    [Header("Fire Cadence (Random)")]
    [Min(0.01f)] [SerializeField] private float minFireDelay = 0.35f;
    [Min(0.01f)] [SerializeField] private float maxFireDelay = 1.20f;

    [Header("Accuracy")]
    [Range(0f, 1f)] [SerializeField] private float hitChance = 0.7f;
    [SerializeField] private float missAngleDegrees = 12f;
    [SerializeField] private float hitAimJitterDegrees = 1.5f;

    [Header("Cannon Aiming")]
    [Tooltip("Max rotation speed of the cannon (deg/sec).")]
    [SerializeField] private float rotateSpeedDeg = 360f;
    [Tooltip("Require cannon to align within this angle before firing (deg).")]
    [SerializeField] private float aimToleranceDeg = 3f;
    [Tooltip("If true, delay the shot until cannon aligns to pending shot direction.")]
    [SerializeField] private bool alignBeforeFiring = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    // Runtime pools
    private Queue<LineRenderer> _beamPool;
    private Queue<GameObject> _stopProjectilePool;

    // Timers
    private float _lastPaperTime = -999f;
    private float _lastStopTime  = -999f;
    private float _nextScanTime  = 0f;
    private float _nextFireTime  = 0f;

    // Target & pending shot
    private DroneHealth _currentTarget;

    private enum ShotKind { None, Paper, Stop }
    private ShotKind _pendingShotKind = ShotKind.None;
    private Vector3  _pendingShotDir  = Vector3.forward;
    private bool     _pendingApplyDamage = true;

    private void Awake()
    {
        if (!fireOrigin)  fireOrigin  = transform;
        if (!cannonPivot) cannonPivot = transform;
        if (maxFireDelay < minFireDelay) maxFireDelay = minFireDelay;
    }

    private void Start()
    {
        InitBeamPool();
        InitStopProjectilePool();
        ScheduleNextFire();
    }

    private void Update()
    {
        if(GameManager.Instance.GameStage != EGameStage.Live) return;
        // Acquire/refresh nearest target
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + scanInterval;
            if (!IsValidTarget(_currentTarget))
                _currentTarget = FindNearestDrone();
        }

        // Compute a base aim direction toward target (for idle tracking)
        Vector3 baseAimDir = cannonPivot.forward;
        if (IsValidTarget(_currentTarget))
        {
            baseAimDir = _currentTarget.transform.position - fireOrigin.position;
            if (flattenForward) baseAimDir.y = 0f;
            if (baseAimDir.sqrMagnitude > 1e-6f) baseAimDir.Normalize();
        }

        // If we have a pending shot, steer to that exact direction (smooth)
        if (_pendingShotKind != ShotKind.None)
        {
            SmoothAimTowards(_pendingShotDir);
            // Only fire when aligned enough (or immediately if disabled)
            if (!alignBeforeFiring || IsAlignedTo(_pendingShotDir))
            {
                FirePendingShot();
                ScheduleNextFire();
            }
            return;
        }

        // No pending shot: track target smoothly
        SmoothAimTowards(baseAimDir);

        // If it's not time to shoot or no target, exit
        if (Time.time < _nextFireTime || !IsValidTarget(_currentTarget)) return;

        // Decide what to shoot next (Stop > Paper)
        bool stopReady  = (Time.time - _lastStopTime)  >= stopCooldown;
        bool paperReady = (Time.time - _lastPaperTime) >= paperCooldown;

        if (!stopReady && !paperReady)
        {
            // Neither ready: schedule for earliest ready moment
            float tStop  = _lastStopTime  + stopCooldown;
            float tPaper = _lastPaperTime + paperCooldown;
            _nextFireTime = Mathf.Min(tStop, tPaper) + Random.Range(0.05f, 0.15f);
            return;
        }

        // Roll hit/miss and compute the pending shot direction once
        bool shouldHit = Random.value < hitChance;
        float jitterDeg = shouldHit ? hitAimJitterDegrees : missAngleDegrees;
        Vector3 shotDir = JitterDirection(baseAimDir, jitterDeg, yawOnly);
        if (flattenForward) { shotDir.y = 0f; if (shotDir.sqrMagnitude > 1e-6f) shotDir.Normalize(); }

        // Register the pending shot; rotation will converge smoothly before firing
        _pendingShotKind   = stopReady ? ShotKind.Stop : ShotKind.Paper;
        _pendingShotDir    = shotDir;
        _pendingApplyDamage = shouldHit;
    }

    // ===== Rotation helpers =====

    private void SmoothAimTowards(Vector3 desiredDir)
    {
        if (!cannonPivot) return;
        Vector3 d = desiredDir;
        if (yawOnly) { d.y = 0f; }
        if (d.sqrMagnitude < 1e-6f) return;
        d.Normalize();

        Quaternion targetRot = Quaternion.LookRotation(d, Vector3.up);
        cannonPivot.rotation = Quaternion.RotateTowards(
            cannonPivot.rotation, targetRot, rotateSpeedDeg * Time.deltaTime);
    }

    private bool IsAlignedTo(Vector3 desiredDir)
    {
        if (!cannonPivot) return true;
        Vector3 fwd = cannonPivot.forward;
        Vector3 d   = desiredDir;
        if (yawOnly)
        {
            fwd.y = 0f; d.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f) return true;
            fwd.Normalize(); d.Normalize();
        }
        float angle = Vector3.Angle(fwd, d);
        return angle <= aimToleranceDeg;
    }

    // ===== Pending shot execution =====

    private void FirePendingShot()
    {
        if (_pendingShotKind == ShotKind.None) return;

        Vector3 origin = fireOrigin.position;
        Vector3 dir    = _pendingShotDir;
        if (dir.sqrMagnitude < 1e-6f) dir = cannonPivot.forward;
        if (flattenForward) { dir.y = 0f; if (dir.sqrMagnitude > 1e-6f) dir.Normalize(); }
        origin += dir * startOffsetMeters;

        if (_pendingShotKind == ShotKind.Stop)
            UseStop(origin, dir, _pendingApplyDamage);
        else
            UsePaper(origin, dir, _pendingApplyDamage);

        // Clear
        _pendingShotKind = ShotKind.None;
    }

    // ===== Scheduling & Targeting =====

    private void ScheduleNextFire()
    {
        _nextFireTime = Time.time + Random.Range(minFireDelay, maxFireDelay);
    }

    private bool IsValidTarget(DroneHealth h)
    {
        if (h == null) return false;
        if (!h.gameObject.activeInHierarchy) return false;
        float d2 = (h.transform.position - fireOrigin.position).sqrMagnitude;
        return d2 <= detectionRange * detectionRange;
    }

    private DroneHealth FindNearestDrone()
    {
        // If your Unity version doesn't support includeInactive, remove the argument.
        DroneHealth[] all = FindObjectsOfType<DroneHealth>(includeInactive: false);
        DroneHealth best = null;
        float bestSqr = float.PositiveInfinity;
        Vector3 p = fireOrigin.position;

        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;
            float d2 = (h.transform.position - p).sqrMagnitude;
            if (d2 <= detectionRange * detectionRange && d2 < bestSqr)
            {
                bestSqr = d2;
                best = h;
            }
        }
        return best;
    }

    // ===== Aim jitter =====

    /// <summary> Deviates 'dir' by up to 'degrees'. If yawOnly, deviation is around Y. </summary>
    private static Vector3 JitterDirection(Vector3 dir, float degrees, bool yawOnlyMode)
    {
        if (degrees <= 0.001f || dir.sqrMagnitude < 1e-6f) return dir.normalized;

        if (yawOnlyMode)
        {
            float angle = Random.Range(-degrees, degrees);
            return (Quaternion.AngleAxis(angle, Vector3.up) * dir).normalized;
        }
        else
        {
            // Conical spread: rotate around a random axis perpendicular to dir
            Vector3 any = Random.onUnitSphere;
            Vector3 axis = Vector3.Cross(dir, any);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            axis.Normalize();
            float angle = Random.Range(-degrees, degrees);
            return (Quaternion.AngleAxis(angle, axis) * dir).normalized;
        }
    }

    // ===== Use Skills =====

    private void UsePaper(Vector3 origin, Vector3 dir, bool applyDamage)
    {
        _lastPaperTime = Time.time;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        if (SphereRay(origin, dir, out RaycastHit hit))
        {
            DrawBeam(origin, hit.point);

            if (applyDamage)
            {
                var health = hit.collider.attachedRigidbody
                    ? hit.collider.attachedRigidbody.GetComponentInParent<DroneHealth>()
                    : hit.collider.GetComponentInParent<DroneHealth>();

                if (health != null)
                {
                    health.ApplyDamage(paperDamage);
                    if (debugLogs) Debug.Log($"[AI:Paper] Hit {health.name} dmg={paperDamage}");
                }
            }
        }
        else
        {
            Vector3 end = origin + dir * maxRayDistance;
            DrawBeam(origin, end);
        }
    }

    private void UseStop(Vector3 origin, Vector3 dir, bool applyDamage)
    {
        _lastStopTime = Time.time;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        if (SphereRay(origin, dir, out RaycastHit hit))
        {
            PlayStopProjectile(origin, hit.point, hit.collider);

        }
        else
        {
            Vector3 end = origin + dir * maxRayDistance;
            PlayStopProjectile(origin, end, null);
        }
    }

    // ===== Ray helpers =====

    private bool SphereRay(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        dir.Normalize();

        if (Physics.SphereCast(origin, raySphereRadius, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction))
            return true;

        return Physics.Raycast(origin, dir, out hit, maxRayDistance, droneLayerMask, triggerInteraction);
    }

    // ===== Projectile (Stop) =====

    private void InitStopProjectilePool()
    {
        _stopProjectilePool = new Queue<GameObject>(Mathf.Max(1, stopProjectilePoolSize));
        for (int i = 0; i < stopProjectilePoolSize; i++)
            _stopProjectilePool.Enqueue(CreateStopProjectile());
    }

    private GameObject CreateStopProjectile()
    {
        GameObject go = stopProjectilePrefab ? Instantiate(stopProjectilePrefab)
                                             : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.SetActive(false);
        var col = go.GetComponent<Collider>(); if (col) Destroy(col);
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

    // ===== Beam (Paper) =====

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
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !fireOrigin) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(fireOrigin.position, 0.05f);

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawWireSphere(fireOrigin.position, detectionRange);
    }
#endif
}
