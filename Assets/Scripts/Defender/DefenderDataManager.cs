using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Spawns and maintains defender data pickups at a fixed world height:
/// - Keeps 'targetActiveCount' pickups alive (default 5).
/// - When one is collected/stolen, immediately spawns a replacement.
/// - Spawn positions are randomized on the XZ plane only, at 'spawnHeightY'.
/// - All pickups are at least 'minSeparation' meters apart (planar distance).
/// - Positions are constrained by a BoxCollider 'spawnArea' (assumes no tilt).
/// </summary>
public class DefenderDataManager : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Pickup prefab that represents the 'data' object.")]
    [SerializeField] private GameObject dataPickupPrefab;

    [Tooltip("How many pickups must be active at all times.")]
    [Min(1)] [SerializeField] private int targetActiveCount = 5;

    [Header("Spawn Area (BoxCollider)")]
    [Tooltip("Defines the allowed spawn region. Assumes its 'up' is world-up (no tilt).")]
    [SerializeField] private BoxCollider spawnArea;

    [Header("Spawn Rules")]
    [Tooltip("Fixed world Y height for all spawned data (meters).")]
    [SerializeField] private float spawnHeightY = 1.5f;

    [Tooltip("Minimum planar distance between any two pickups (meters).")]
    [Min(0f)] [SerializeField] private float minSeparation = 2f;

    [Tooltip("Max attempts for finding a valid position per spawn to avoid infinite loops.")]
    [Min(1)] [SerializeField] private int maxAttemptsPerSpawn = 64;

    [Header("Optional Overlap Check")]
    [Tooltip("If > 0, rejects positions that overlap colliders on 'overlapBlockers' within this radius.")]
    [SerializeField] private float overlapCheckRadius = 0f;
    [SerializeField] private LayerMask overlapBlockers = 0;

    [Header("Events")]
    public UnityEvent onAllPickupsMaintained; // invoked after initial fill and after each successful respawn

    /// <summary>Active pickups tracked by the manager.</summary>
    public IReadOnlyList<DataPickup> ActivePickups => _activePickups;

    private readonly List<DataPickup> _activePickups = new List<DataPickup>();

    private void Awake()
    {
        if (!spawnArea)
            spawnArea = GetComponent<BoxCollider>();
    }

    private void Start()
    {
        if (!dataPickupPrefab)
        {
            Debug.LogError("[DefenderDataManager] Missing dataPickupPrefab.");
            enabled = false; return;
        }
        if (!spawnArea)
        {
            Debug.LogError("[DefenderDataManager] Missing spawnArea (BoxCollider).");
            enabled = false; return;
        }

        TopUpPickups();
        onAllPickupsMaintained?.Invoke();
    }

    /// <summary>
    /// Called by DataPickup when it has been stolen/collected.
    /// Removes it from tracking and spawns one replacement.
    /// </summary>
    public void NotifyPickupStolen(DataPickup pickup)
    {
        if (pickup != null)
            _activePickups.Remove(pickup);

        SpawnOne();
        onAllPickupsMaintained?.Invoke();
    }

    private void TopUpPickups()
    {
        int need = Mathf.Max(0, targetActiveCount - _activePickups.Count);
        for (int i = 0; i < need; i++)
            SpawnOne();
    }

    private void SpawnOne()
    {
        if (TryFindValidSpawnPosition(out Vector3 pos))
        {
            var go = Instantiate(dataPickupPrefab, pos, Quaternion.identity);
            go.transform.SetParent(transform);
            go.SetActive(true);
            var pickup = go.GetComponent<DataPickup>();
            if (!pickup) pickup = go.AddComponent<DataPickup>();
            pickup.Initialize(this);
            _activePickups.Add(pickup);
        }
        else
        {
            Debug.LogWarning("[DefenderDataManager] Could not find a valid spawn position. " +
                             "Consider enlarging spawnArea or reducing minSeparation.");
        }
    }

    /// <summary>
    /// Computes a random point on the XZ plane inside the BoxCollider footprint and at fixed Y height.
    /// Assumes spawnArea's up is world-up (no tilt). If your area can tilt, switch to a custom planar projection.
    /// </summary>
    private bool TryFindValidSpawnPosition(out Vector3 worldPos)
    {
        worldPos = default;

        var tr = spawnArea.transform;
        // World center of the collider
        Vector3 centerWorld = tr.TransformPoint(spawnArea.center);

        // Effective half extents in world (respecting scale)
        Vector3 lossy = tr.lossyScale;
        float halfX = Mathf.Abs(spawnArea.size.x * 0.5f * lossy.x);
        float halfZ = Mathf.Abs(spawnArea.size.z * 0.5f * lossy.z);

        // Local X/Z directions in world space (assumes no tilt – i.e., right/forward lie on XZ plane)
        Vector3 right = tr.right;   right.y = 0f; right.Normalize();
        Vector3 fwd   = tr.forward; fwd.y   = 0f; fwd.Normalize();

        for (int attempt = 0; attempt < maxAttemptsPerSpawn; attempt++)
        {
            // Random offset within rectangle on XZ plane
            float ox = Random.Range(-halfX, halfX);
            float oz = Random.Range(-halfZ, halfZ);

            Vector3 candidate = centerWorld + right * ox + fwd * oz;
            candidate.y = spawnHeightY;

            // Enforce planar separation from existing pickups
            bool tooClose = false;
            for (int i = 0; i < _activePickups.Count; i++)
            {
                var p = _activePickups[i];
                if (!p) continue;

                Vector2 a = new Vector2(candidate.x, candidate.z);
                Vector2 b = new Vector2(p.transform.position.x, p.transform.position.z);
                if ((a - b).sqrMagnitude < (minSeparation * minSeparation))
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Optional overlap rejection (sphere at fixed height)
            if (overlapCheckRadius > 0f && overlapBlockers.value != 0)
            {
                if (Physics.CheckSphere(candidate, overlapCheckRadius, overlapBlockers, QueryTriggerInteraction.Ignore))
                    continue;
            }

            worldPos = candidate;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!spawnArea) return;

        // Draw the spawn rectangle on the XZ plane at spawnHeightY
        var tr = spawnArea.transform;
        Vector3 centerWorld = tr.TransformPoint(spawnArea.center);
        centerWorld.y = spawnHeightY;

        Vector3 lossy = tr.lossyScale;
        float halfX = Mathf.Abs(spawnArea.size.x * 0.5f * lossy.x);
        float halfZ = Mathf.Abs(spawnArea.size.z * 0.5f * lossy.z);

        Vector3 right = tr.right;   right.y = 0f; right.Normalize();
        Vector3 fwd   = tr.forward; fwd.y   = 0f; fwd.Normalize();

        Vector3 p1 = centerWorld + right * halfX + fwd * halfZ;
        Vector3 p2 = centerWorld - right * halfX + fwd * halfZ;
        Vector3 p3 = centerWorld - right * halfX - fwd * halfZ;
        Vector3 p4 = centerWorld + right * halfX - fwd * halfZ;

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);

        // Visualize min separation around current pickups
        if (_activePickups != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            foreach (var p in _activePickups)
            {
                if (!p) continue;
                Vector3 c = p.transform.position; c.y = spawnHeightY;
                Gizmos.DrawSphere(c, minSeparation * 0.5f);
            }
        }
    }
#endif
}