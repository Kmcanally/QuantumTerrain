using UnityEditor;
using UnityEngine;

/// <summary>Creates the generated ground layers, so the project needs no imported art.</summary>
public static class QuantumTerrainLayers
{
    const string LayerFolder = "Assets/QuantumTerrainLayers";
    const int TextureSize = 64;
    const float TileSize = 25f;
    const float Grain = 0.06f;
    const float Smoothness = 0.08f;

    public static TerrainLayer[] LoadOrCreate()
    {
        var layers = new TerrainLayer[QuantumTerrainSplat.LayerCount];
        layers[QuantumTerrainSplat.Grass] = LoadOrCreate("Grass", new Color(0.33f, 0.47f, 0.22f));
        layers[QuantumTerrainSplat.Rock] = LoadOrCreate("Rock", new Color(0.42f, 0.39f, 0.36f));
        layers[QuantumTerrainSplat.Snow] = LoadOrCreate("Snow", new Color(0.92f, 0.94f, 0.97f));
        layers[QuantumTerrainSplat.Sand] = LoadOrCreate("Sand", new Color(0.76f, 0.69f, 0.50f));
        return layers;
    }

    static TerrainLayer LoadOrCreate(string name, Color color)
    {
        string layerPath = $"{LayerFolder}/{name}.terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer != null)
        {
            return layer;
        }

        if (!AssetDatabase.IsValidFolder(LayerFolder))
        {
            AssetDatabase.CreateFolder("Assets", "QuantumTerrainLayers");
        }
        Texture2D texture = CreateGrainTexture(color);
        AssetDatabase.CreateAsset(texture, $"{LayerFolder}/{name}.asset");

        layer = new TerrainLayer { diffuseTexture = texture, tileSize = Vector2.one * TileSize };
        AssetDatabase.CreateAsset(layer, layerPath);
        return layer;
    }

    /// <summary>A flat colour with light speckle, so ground doesn't look like plastic up close.</summary>
    static Texture2D CreateGrainTexture(Color color)
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        var pixels = new Color[TextureSize * TextureSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            float shade = 1f + Random.Range(-Grain, Grain);
            // The terrain shader reads smoothness from alpha, so 1 would look wet.
            pixels[i] = new Color(color.r * shade, color.g * shade, color.b * shade, Smoothness);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
