using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Builds a Unity terrain from the heightmap baked by python/generate_terrain.py.</summary>
public static class QuantumTerrainImporter
{
    const string TerrainName = "Quantum Terrain";
    const string TerrainDataPath = "Assets/QuantumTerrainData.asset";
    const float TerrainWidth = 1000f;
    const float TerrainHeight = 200f;

    [MenuItem("Quantum Terrain/Import Baked Heightmap")]
    public static void Import()
    {
        string bakedDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "baked"));
        string rawPath = Path.Combine(bakedDirectory, "terrain.r16");
        if (!File.Exists(rawPath))
        {
            Debug.LogError($"No baked heightmap at {rawPath}. Run python/generate_terrain.py first.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(rawPath);
        int side = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2));

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }

        // Unity needs a 2^n + 1 grid. Repeating the first row and column works
        // because the QFT output wraps around, so the map tiles seamlessly.
        data.heightmapResolution = side + 1;
        data.size = new Vector3(TerrainWidth, TerrainHeight, TerrainWidth);

        var heights = new float[side + 1, side + 1];
        for (int y = 0; y <= side; y++)
        {
            for (int x = 0; x <= side; x++)
            {
                int offset = 2 * ((y % side) * side + x % side);
                heights[y, x] = System.BitConverter.ToUInt16(bytes, offset) / 65535f;
            }
        }
        data.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        Terrain terrain = Object.FindObjectOfType<Terrain>();
        if (terrain == null)
        {
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = TerrainName;
            terrain = go.GetComponent<Terrain>();
        }
        terrain.terrainData = data;
        terrain.GetComponent<TerrainCollider>().terrainData = data;
        Debug.Log($"Imported {side}x{side} quantum heightmap from {rawPath}");
    }

    /// <summary>Batch-mode entry point: imports, then saves the open scene.</summary>
    public static void ImportAndSaveScene()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        Import();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/QuantumTerrain.unity");
    }
}
