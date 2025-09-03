
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RemoveMeshCollidersEditor))]
public class RemoveMeshCollidersButton : Editor{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Remove Mesh Colliders"))
        {
            (target as RemoveMeshCollidersEditor)?.RemoveMeshColliders();
        }
    }
}
#endif