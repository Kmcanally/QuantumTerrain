using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Adds the orbiting camera and readout to the open scene.</summary>
public static class QuantumTerrainPresentationSetup
{
    [MenuItem("Quantum Terrain/Set Up Presentation View")]
    static void SetUp()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            Undo.RegisterCreatedObjectUndo(go, "Create presentation camera");
            camera = go.AddComponent<Camera>();
        }
        if (camera.GetComponent<QuantumTerrainPresentation>() == null)
        {
            Undo.AddComponent<QuantumTerrainPresentation>(camera.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        Selection.activeGameObject = camera.gameObject;
        Debug.Log("Presentation view ready. Press Play, with Play Maximized on in the Game view for a clean screen.");
    }
}
