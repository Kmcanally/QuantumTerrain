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

        Apply(Decode(File.ReadAllBytes(rawPath)));
        AssetDatabase.SaveAssets();
        Debug.Log($"Imported quantum heightmap from {rawPath}");
    }

    /// <summary>Reads a square 16-bit little-endian heightmap into Unity's 2^n + 1 grid.</summary>
    public static float[,] Decode(byte[] bytes)
    {
        int side = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2));

        // Repeating the first row and column works because the QFT output wraps
        // around, so the map tiles seamlessly.
        var heights = new float[side + 1, side + 1];
        for (int y = 0; y <= side; y++)
        {
            for (int x = 0; x <= side; x++)
            {
                int offset = 2 * ((y % side) * side + x % side);
                heights[y, x] = System.BitConverter.ToUInt16(bytes, offset) / 65535f;
            }
        }
        return heights;
    }

    /// <summary>
    /// Writes heights to the scene's terrain. Preview updates skip Unity's LOD
    /// rebuild so they can run every frame, so finish with a full update.
    /// </summary>
    public static void Apply(float[,] heights, bool preview = false)
    {
        int side = heights.GetLength(0) - 1;

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }

        if (data.heightmapResolution != side + 1)
        {
            data.heightmapResolution = side + 1;
        }
        var size = new Vector3(TerrainWidth, TerrainHeight, TerrainWidth);
        if (data.size != size)
        {
            data.size = size;
        }

        if (preview)
        {
            data.SetHeightsDelayLOD(0, 0, heights);
        }
        else
        {
            data.SetHeights(0, 0, heights);
        }

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
        if (terrain.terrainData != data)
        {
            terrain.terrainData = data;
        }
        var collider = terrain.GetComponent<TerrainCollider>();
        if (collider.terrainData != data)
        {
            collider.terrainData = data;
        }
    }
}
