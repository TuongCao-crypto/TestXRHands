using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI that auto-uses Defense skills (Paper/Stop) against the nearest drone:
/// - Random fire cadence (each shot schedules the next between [minFireDelay, maxFireDelay]).
/// - Hit probability (hitChance); on miss, aim is intentionally deviated (or damage is skipped).
/// - Prefers Stop (freeze) when its cooldown is ready; otherwise Paper.
/// </summary>
public class DefenseSkillAI : MonoBehaviour
{
    [Header("Origin & Aiming")]
    [SerializeField] private Transform fireOrigin;
    [SerializeField] private bool flattenForward = false;
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
    [Tooltip("Next shot is scheduled randomly in this range after each attempt.")]
    [Min(0.01f)] [SerializeField] private float minFireDelay = 0.35f;
    [Min(0.01f)] [SerializeField] private float maxFireDelay = 1.20f;

    [Header("Accuracy")]
    [Tooltip("Chance to actually hit the target (0..1).")]
    [Range(0f, 1f)] [SerializeField] private float hitChance = 0.7f;
    [Tooltip("Cone angle (degrees) used to deviate aim when missing.")]
    [SerializeField] private float missAngleDegrees = 12f;
    [Tooltip("Small jitter even on hit (degrees) to look natural.")]
    [SerializeField] private float hitAimJitterDegrees = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    private float delayStart = 5f;

    // Runtime
    private Queue<LineRenderer> _beamPool;
    private Queue<GameObject> _stopProjectilePool;
    private float _lastPaperTime = -999f;
    private float _lastStopTime  = -999f;
    private float _nextScanTime  = 0f;
    private float _nextFireTime  = 0f;

    private DroneHealth _currentTarget;

    private void Awake()
    {
        if (!fireOrigin) fireOrigin = transform;
        if (maxFireDelay < minFireDelay) maxFireDelay = minFireDelay;
    }

    private void Start()
    {
        InitBeamPool();
        InitStopProjectilePool();
        ScheduleNextFire(); // start with a random delay
    }

    private void Update()
    {
        if (delayStart > 0)
        {
            delayStart -= Time.deltaTime;
            return;
        }

        // Acquire/refresh target at scan interval
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + scanInterval;
            if (!IsValidTarget(_currentTarget))
                _currentTarget = FindNearestDrone();
        }

        // If no target, just push next fire a bit and wait
        if (!IsValidTarget(_currentTarget))
        {
            if (Time.time >= _nextFireTime) ScheduleNextFire();
            return;
        }

        // Fire only when the randomized timer elapses
        if (Time.time < _nextFireTime) return;

        // Compute origin & direction to current target
        Vector3 origin = fireOrigin.position;
        Vector3 dir    = (_currentTarget.transform.position - origin);
        if (flattenForward) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) { ScheduleNextFire(); return; }
        dir.Normalize();
        origin += dir * startOffsetMeters;

        // Decide whether this shot should be a "hit" or a "miss"
        bool shouldHit = Random.value < hitChance;

        // Choose skill: prefer Stop if ready, else Paper; if neither ready, nudge next fire to earliest ready
        bool stopReady  = (Time.time - _lastStopTime)  >= stopCooldown;
        bool paperReady = (Time.time - _lastPaperTime) >= paperCooldown;

        if (!stopReady && !paperReady)
        {
            float tStop  = _lastStopTime  + stopCooldown;
            float tPaper = _lastPaperTime + paperCooldown;
            _nextFireTime = Mathf.Min(tStop, tPaper) + Random.Range(0.05f, 0.15f);
            return;
        }

        // Apply small jitter to look less robotic (even on hit)
        float jitterDeg = shouldHit ? hitAimJitterDegrees : missAngleDegrees;
        Vector3 finalDir = JitterDirection(dir, jitterDeg, flattenForward);

        // Perform shot (on miss we still play visuals but skip damage application)
        if (stopReady)
            UseStop(origin, finalDir, applyDamage: shouldHit);
        else
            UsePaper(origin, finalDir, applyDamage: shouldHit);

        // Schedule the next (random) fire time regardless of hit/miss
        ScheduleNextFire();
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

    /// <summary>
    /// Deviates 'dir' by up to 'degrees' within a cone. If yawOnly (flattenForward), rotates around Y for planar deviation.
    /// </summary>
    private static Vector3 JitterDirection(Vector3 dir, float degrees, bool yawOnly)
    {
        if (degrees <= 0.001f) return dir;

        if (yawOnly)
        {
            // Rotate around global up to keep horizontal shot
            float angle = Random.Range(-degrees, degrees);
            return Quaternion.AngleAxis(angle, Vector3.up) * dir;
        }
        else
        {
            // Rotate around a random perpendicular axis for a conical spread
            Vector3 axis = Vector3.Cross(dir, Random.onUnitSphere);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up; // fallback
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
