using UnityEngine;

/// <summary>
/// Simple pickup that notifies the manager when stolen.
/// You can trigger collection by calling Collect(),
/// or enable the trigger-based auto-collect if you prefer.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DataPickup : MonoBehaviour
{
    private DefenderDataManager _manager;
    private Collider _col;

    public void Initialize(DefenderDataManager manager)
    {
        _manager = manager;
        if (!_col) _col = GetComponent<Collider>();
    }

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col) _col.isTrigger = true;
    }

    /// <summary>
    /// Marks this pickup as stolen and notifies the manager.
    /// </summary>
    public void Collect()
    {
        // Notify manager, then destroy this pickup
        _manager?.NotifyPickupStolen(this);
        Destroy(gameObject);
    }
    
}