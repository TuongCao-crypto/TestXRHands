using UnityEngine;
using UnityEditor;

public class RemoveMeshCollidersEditor : MonoBehaviour
{
    [ContextMenu("Remove Mesh Colliders from Children")]
    public void RemoveMeshColliders()
    {
        Collider[] meshColliders = GetComponentsInChildren<Collider>();
        foreach (Collider collider in meshColliders)
        {
            DestroyImmediate(collider); // Use DestroyImmediate for editor execution
        }
        Debug.Log("Removed all Colliders from child objects.");
    }
}
