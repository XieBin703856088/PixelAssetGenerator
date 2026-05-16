using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Base class providing common procedural noise and math utilities for graph nodes.
/// </summary>
public abstract class GraphNodeBase : IGraphNode, IGpuAcceleratedNode
{
    public abstract string TypeName { get; }
    public abstract string Category { get; }
    public abstract IReadOnlyList<GraphNodePort> InputPorts { get; }
    public abstract IReadOnlyList<GraphNodePort> OutputPorts { get; }
    public abstract IReadOnlyList<NodeParameterDefinition> Parameters { get; }
    public abstract PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context);

    /// <summary>
    /// Optional GPU-accelerated processing path. Default implementation returns null
    /// so evaluator will fall back to CPU Process(). Override in nodes to provide
    /// GPU implementations.
    /// </summary>
    public virtual PixelBuffer? ProcessGpu(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        return null;
    }

    // --- Parameter helpers ---

    public static float GetFloat(IReadOnlyDictionary<string, object> p, string name, float fallback = 0f)
    {
        return p.TryGetValue(name, out var v) ? Convert.ToSingle(v) : fallback;
    }

    public static int GetInt(IReadOnlyDictionary<string, object> p, string name, int fallback = 0)
    {
        return p.TryGetValue(name, out var v) ? Convert.ToInt32(v) : fallback;
    }

    public static bool GetBool(IReadOnlyDictionary<string, object> p, string name, bool fallback = false)
    {
        return p.TryGetValue(name, out var v) ? Convert.ToBoolean(v) : fallback;
    }

    public static string GetChoice(IReadOnlyDictionary<string, object> p, string name, string fallback = "")
    {
        return p.TryGetValue(name, out var v) ? v?.ToString() ?? fallback : fallback;
    }

    public static string GetString(IReadOnlyDictionary<string, object> p, string name, string fallback = "")
    {
        return p.TryGetValue(name, out var v) ? v?.ToString() ?? fallback : fallback;
    }

    public static System.Windows.Media.Color GetColor(IReadOnlyDictionary<string, object> p, string name, System.Windows.Media.Color fallback)
    {
        return p.TryGetValue(name, out var v) && v is System.Windows.Media.Color c ? c : fallback;
    }

    // --- Tileable noise primitives ---

    /// <summary>
    /// Tileable fractal value noise with periodic boundary.
    /// Returns value in [0,1].
    /// </summary>
    public static float TileableFractalNoise(float x, float y, int period, int octaves, float persistence, float lacunarity, int seed)
    {
        var amplitude = 1f;
        var frequency = 1f;
        var value = 0f;
        var max = 0f;

        for (var o = 0; o < octaves; o++)
        {
            var octavePeriod = Math.Max(1, (int)MathF.Round(period * frequency));
            value += TileableValueNoise(x * frequency, y * frequency, octavePeriod, seed + o * 1013) * amplitude;
            max += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return max == 0f ? 0f : value / max;
    }

    /// <summary>
    /// Tileable gradient noise (Perlin-like) with periodic boundary.
    /// Returns value in [0,1].
    /// </summary>
    public static float TileableGradientNoise(float x, float y, int period, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var dx = x - x0;
        var dy = y - y0;

        var px0 = Mod(x0, period);
        var py0 = Mod(y0, period);
        var px1 = Mod(x1, period);
        var py1 = Mod(y1, period);

        var g00 = Gradient(px0, py0, seed);
        var g10 = Gradient(px1, py0, seed);
        var g01 = Gradient(px0, py1, seed);
        var g11 = Gradient(px1, py1, seed);

        var n00 = g00.x * dx + g00.y * dy;
        var n10 = g10.x * (dx - 1f) + g10.y * dy;
        var n01 = g01.x * dx + g01.y * (dy - 1f);
        var n11 = g11.x * (dx - 1f) + g11.y * (dy - 1f);

        var sx = SmoothStep(dx);
        var sy = SmoothStep(dy);

        var ix0 = Lerp(n00, n10, sx);
        var ix1 = Lerp(n01, n11, sx);
        return Lerp(ix0, ix1, sy) * 0.5f + 0.5f;
    }

    /// <summary>
    /// Tileable value noise.
    /// </summary>
    public static float TileableValueNoise(float x, float y, int period, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var sx = SmoothStep(x - x0);
        var sy = SmoothStep(y - y0);

        var px0 = Mod(x0, period);
        var py0 = Mod(y0, period);
        var px1 = Mod(x1, period);
        var py1 = Mod(y1, period);

        var n00 = HashToUnit(px0, py0, seed);
        var n10 = HashToUnit(px1, py0, seed);
        var n01 = HashToUnit(px0, py1, seed);
        var n11 = HashToUnit(px1, py1, seed);

        var ix0 = Lerp(n00, n10, sx);
        var ix1 = Lerp(n01, n11, sx);
        return Lerp(ix0, ix1, sy);
    }

    /// <summary>
    /// Tileable Voronoi cell noise. Returns normalized nearest/second distances and cell coords.
    /// </summary>
    public static void TileableVoronoi(float x, float y, int cellSize, int periodInCells, int seed,
        out float nearest, out float secondNearest, out int cellX, out int cellY)
    {
        var cellX0 = (int)MathF.Floor(x / cellSize);
        var cellY0 = (int)MathF.Floor(y / cellSize);
        var periodSize = Math.Max(1, periodInCells * cellSize);

        nearest = float.MaxValue;
        secondNearest = float.MaxValue;
        cellX = 0;
        cellY = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var cx = cellX0 + dx;
                var cy = cellY0 + dy;
                var wrappedX = Mod(cx, periodInCells);
                var wrappedY = Mod(cy, periodInCells);

                var jitterX = HashToUnit(wrappedX, wrappedY, seed) - 0.5f;
                var jitterY = HashToUnit(wrappedX, wrappedY, seed + 19) - 0.5f;

                var pointX = (wrappedX + 0.5f + jitterX) * cellSize;
                var pointY = (wrappedY + 0.5f + jitterY) * cellSize;

                var deltaX = pointX - x;
                var deltaY = pointY - y;
                deltaX -= MathF.Round(deltaX / periodSize) * periodSize;
                deltaY -= MathF.Round(deltaY / periodSize) * periodSize;

                var distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (distance < nearest)
                {
                    secondNearest = nearest;
                    nearest = distance;
                    cellX = wrappedX;
                    cellY = wrappedY;
                }
                else if (distance < secondNearest)
                {
                    secondNearest = distance;
                }
            }
        }

        var normalizer = MathF.Max(1f, cellSize);
        nearest = Math.Clamp(nearest / normalizer, 0f, 1f);
        secondNearest = Math.Clamp(secondNearest / normalizer, 0f, 1f);
    }

    // --- Math helpers ---

    public static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (MathF.Abs(edge1 - edge0) < 0.0001f)
            return x < edge0 ? 0f : 1f;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static int Mod(int value, int modulus)
    {
        if (modulus <= 0) return 0;
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    public static float HashToUnit(int x, int y, int seed)
    {
        unchecked
        {
            var hash = x * 374761393 + y * 668265263 + seed * 982451653;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private static (float x, float y) Gradient(int x, int y, int seed)
    {
        var angle = HashToUnit(x, y, seed) * MathF.Tau;
        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>
    /// Perturbs UV coordinates using fractal noise for organic distortion.
    /// Generators can call this to make procedural textures look more natural.
    /// </summary>
    public static (float u, float v) PerturbUv(float u, float v, float strength, int period, int seed)
    {
        var dx = (TileableFractalNoise(u + 10f, v, period, 2, 0.5f, 2f, seed) - 0.5f) * 2f * strength;
        var dy = (TileableFractalNoise(u, v + 10f, period, 2, 0.5f, 2f, seed + 137) - 0.5f) * 2f * strength;
        return (u + dx, v + dy);
    }
}
