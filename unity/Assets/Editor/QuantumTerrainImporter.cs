using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Applies heightmaps from python/generate_terrain.py to a Unity terrain.</summary>
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
        string rawPath = EditorUtility.OpenFilePanel("Import baked heightmap", bakedDirectory, "r16");
        if (string.IsNullOrEmpty(rawPath))
        {
            return;
        }

        Apply(File.ReadAllBytes(rawPath));
        AssetDatabase.SaveAssets();
        Debug.Log($"Imported quantum heightmap from {rawPath}");
    }

    /// <summary>Writes a square 16-bit little-endian heightmap to the scene's terrain.</summary>
    public static void Apply(byte[] bytes)
    {
        int side = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2));

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }

        // Unity needs a 2^n + 1 grid. Repeating the first row and column works
        // because the QFT output wraps around, so the map tiles seamlessly.
        if (data.heightmapResolution != side + 1)
        {
            data.heightmapResolution = side + 1;
        }
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

        TerrainLayer[] layers = data.terrainLayers;
        if (layers.Length != QuantumTerrainSplat.LayerCount || System.Array.Exists(layers, layer => layer == null))
        {
            data.terrainLayers = QuantumTerrainLayers.LoadOrCreate();
        }
        if (data.alphamapResolution != side)
        {
            data.alphamapResolution = side;
        }
        data.SetAlphamaps(0, 0, QuantumTerrainSplat.Compute(heights, TerrainWidth / side, TerrainHeight));
        EditorUtility.SetDirty(data);

        Terrain terrain = Object.FindObjectOfType<Terrain>();
        if (terrain == null)
        {
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = TerrainName;
            terrain = go.GetComponent<Terrain>();
        }
        terrain.terrainData = data;
        terrain.GetComponent<TerrainCollider>().terrainData = data;
    }
}
