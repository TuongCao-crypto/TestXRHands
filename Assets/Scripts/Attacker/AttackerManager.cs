using System.Collections.Generic;
using UnityEngine;

public class AttackerManager : MonoBehaviour
{
    private List<DroneHealth> dronesHealth;
    private readonly Dictionary<AutoFlightInputController, Transform> _assignedTargets = new();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        dronesHealth = new List<DroneHealth>(GetComponentsInChildren<DroneHealth>(true));
        InitializeDroneHealth();
    }

    public void InitializeDroneHealth()
    {
        for (int i = 0; i < dronesHealth.Count; i++)
        {
            dronesHealth[i].Init(i, this);
        }
    }

    public void SetDroneInitTargets(IReadOnlyList<DataPickup> targets)
    {
        for (int i = 0; i < dronesHealth.Count; i++)
        {
            SetDroneTarget(dronesHealth[i].GetComponent<AutoFlightInputController>(), targets);
        }
        // int n = dronesHealth.Count;
        // int m = targets.Count;
        //
        // // Build all (drone, target) pairs with squared distance
        // var pairs = new List<(int di, int ti, float d2)>(n * m);
        // for (int di = 0; di < n; di++)
        // {
        //     var d = dronesHealth[di];
        //     if (d == null) continue;
        //     Vector3 dp = d.transform.position;
        //
        //     for (int ti = 0; ti < m; ti++)
        //     {
        //         var t = targets[ti];
        //         if (t == null) continue;
        //         float d2 = (t.transform.position - dp).sqrMagnitude;
        //         pairs.Add((di, ti, d2));
        //     }
        // }
        //
        // // Sort by distance ascending (global greedy assignment)
        // pairs.Sort((a, b) => a.d2.CompareTo(b.d2));
        //
        // var usedDrone = new bool[n];
        // var usedTarget = new bool[m];
        // int assigned = 0;
        //
        // foreach (var p in pairs)
        // {
        //     if (assigned >= n) break; // all drones assigned
        //     if (usedDrone[p.di] || usedTarget[p.ti]) // already taken
        //         continue;
        //
        //     var droneHealth = dronesHealth[p.di];
        //     var targetPickup = targets[p.ti];
        //     if (droneHealth == null || targetPickup == null) continue;
        //
        //     var ctrl = droneHealth.GetComponent<AutoFlightInputController>();
        //     if (ctrl != null)
        //     {
        //         ctrl.target = targetPickup.transform;
        //         usedDrone[p.di] = true;
        //         usedTarget[p.ti] = true;
        //         assigned++;
        //     }
        // }
    }

    /// <summary>
    /// Assigns the nearest AVAILABLE target (not currently assigned to another drone)
    /// to the given drone. If none available, clears the target.
    /// </summary>
    public void SetDroneTarget(AutoFlightInputController drone, IReadOnlyList<DataPickup> targets)
    {
        if (drone == null)
        {
            Debug.LogWarning("[AttackerManager] SetDroneTarget: drone is null.");
            return;
        }

        if (targets == null || targets.Count == 0)
        {
            drone.SetTarget(null);
            _assignedTargets.Remove(drone);
            return;
        }

        Vector3 dp = drone.transform.position;
        Transform best = null;
        float bestSqr = float.PositiveInfinity;

        // Pick nearest target that is not assigned to another drone
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.gameObject.activeInHierarchy) continue;

            Transform tt = t.transform;
            if (IsTargetAssignedToAnotherDrone(tt, drone)) continue;

            float sqr = (tt.position - dp).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = tt;
            }
        }

        drone.SetTarget(best);
        if (best != null) _assignedTargets[drone] = best;
        else _assignedTargets.Remove(drone);
    }

    /// <summary>
    /// Returns true if 'target' is currently assigned to ANY other drone (not 'self').
    /// Checks controller readable 'target' (property/field) if available; otherwise uses _assignedTargets fallback.
    /// </summary>
    private bool IsTargetAssignedToAnotherDrone(Transform target, AutoFlightInputController self)
    {
        if (target == null) return false;

        for (int i = 0; i < dronesHealth.Count; i++)
        {
            var dh = dronesHealth[i];
            if (dh == null) continue;

            var ctrl = dh.GetComponent<AutoFlightInputController>();
            if (ctrl == null || ctrl == self) continue;

            // Try to read current target from controller; fallback to our map
            if (TryGetControllerTarget(ctrl, out var current))
            {
                if (current == target) return true;
            }
            else
            {
                if (_assignedTargets.TryGetValue(ctrl, out var remembered) && remembered == target)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to read controller's current target via property/field named 'target'.
    /// Returns false if not accessible.
    /// </summary>
    private bool TryGetControllerTarget(AutoFlightInputController ctrl, out Transform target)
    {
        if (ctrl == null)
        {
            target = null;
            return false;
        }

        target = ctrl.target;
        return target != null;
        
    }

    public void OnDroneBroken(int id)
    {
        for (int i = 0; i < dronesHealth.Count; i++)
        {
            if (dronesHealth[i].ID == id)
            {
                dronesHealth.Remove(dronesHealth[i]);
                break;
            }
        }

        if (dronesHealth.Count <= 0)
        {
            //Endgame
            ScoreManager.Instance.EndMatchNow();
        }
    }
}