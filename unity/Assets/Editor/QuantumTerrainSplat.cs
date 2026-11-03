using System;

/// <summary>Works out how much of each ground layer belongs at every terrain point.</summary>
public static class QuantumTerrainSplat
{
    public const int Grass = 0;
    public const int Rock = 1;
    public const int Snow = 2;
    public const int Sand = 3;
    public const int LayerCount = 4;

    const float RockSlopeStart = 35f;
    const float RockSlopeEnd = 48f;
    const float SnowHeightStart = 0.6f;
    const float SnowHeightEnd = 0.72f;
    const float SandHeightStart = 0.12f;
    const float SandHeightEnd = 0.06f;

    /// <summary>
    /// Returns [y, x, layer] weights that sum to 1 at each point. Heights are 0 to 1 on a
    /// grid one larger than the result, and slopes wrap because the map tiles.
    /// </summary>
    public static float[,,] Compute(float[,] heights, float cellSize, float heightScale)
    {
        int side = heights.GetLength(0) - 1;
        var weights = new float[side, side, LayerCount];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                float dx = heights[y, (x + 1) % side] - heights[y, (x + side - 1) % side];
                float dy = heights[(y + 1) % side, x] - heights[(y + side - 1) % side, x];
                float rise = heightScale * (float)Math.Sqrt(dx * dx + dy * dy) / (2f * cellSize);
                float slope = (float)(Math.Atan(rise) * 180.0 / Math.PI);

                // Rock wins on steep ground, so snow and sand only claim what is left.
                float rock = Ramp(RockSlopeStart, RockSlopeEnd, slope);
                float snow = (1f - rock) * Ramp(SnowHeightStart, SnowHeightEnd, heights[y, x]);
                float sand = (1f - rock - snow) * Ramp(SandHeightStart, SandHeightEnd, heights[y, x]);

                weights[y, x, Rock] = rock;
                weights[y, x, Snow] = snow;
                weights[y, x, Sand] = sand;
                weights[y, x, Grass] = 1f - rock - snow - sand;
            }
        }
        return weights;
    }

    /// <summary>Smoothly goes from 0 at <paramref name="from"/> to 1 at <paramref name="to"/>.</summary>
    static float Ramp(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
