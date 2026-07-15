using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.PixelArt;

/// <summary>
/// Shared style contract for 16/32/64 px pixel-art assets.  The kernel intentionally
/// works on discrete pixels: adaptive palette reduction removes smooth gradients and
/// wrapped component cleanup turns isolated noise into readable pixel clusters.
/// </summary>
public readonly record struct PixelArtStyleProfile(
    int PaletteSize,
    int MinimumClusterSize,
    float Contrast,
    float Saturation,
    float DitherStrength,
    bool PreserveAlpha = true,
    bool Enabled = true,
    bool HsvWeighted = false)
{
    public static PixelArtStyleProfile ForLegacyNode(string category, string typeName, int tileSize)
    {
        if (string.Equals(category, "Material", StringComparison.OrdinalIgnoreCase))
            return new(8, tileSize <= 32 ? 2 : 3, 1.14f, 1.08f, 0f);

        if (string.Equals(category, "Generate", StringComparison.OrdinalIgnoreCase))
            return new(8, tileSize <= 32 ? 2 : 3, 1.12f, 1.06f, 0f);

        if (string.Equals(category, "Noise", StringComparison.OrdinalIgnoreCase))
            return new(6, 2, 1.16f, 1.02f, 0f);

        if (string.Equals(category, "Pattern", StringComparison.OrdinalIgnoreCase)
            && !TechnicalPatternNodes.Contains(typeName))
            return new(8, 1, 1.1f, 1.05f, 0f);

        if (string.Equals(category, "ImageProcess", StringComparison.OrdinalIgnoreCase)
            && VisualImageProcessors.Contains(typeName))
        {
            return new(tileSize <= 32 ? 10 : 14, 1, 1.06f, 1.03f, 0f);
        }

        return new(0, 0, 1f, 1f, 0f, Enabled: false);
    }

    private static readonly HashSet<string> VisualImageProcessors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bevel", "Blur", "ChromaticAberration", "ColorAdjust", "Colorize",
        "ColorQuantize", "Curves", "DropShadow", "Glow", "GradientMap",
        "Grayscale", "HslAdjust", "Lighting", "MotionBlur", "NoiseInjection",
        "Outline", "PaletteMap", "Pixelate", "PixelPerfectOutline", "Posterize",
        "RadialBlur", "Scanlines", "Sharpen", "Stylize", "Vignette"
    };

    private static readonly HashSet<string> TechnicalPatternNodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FrameSequence", "ParameterAnimation", "NineSlice", "SpriteSheet",
        "SpriteSlice", "Symmetry", "TileCombine", "TileMirror"
    };
}

public static class PixelArtKernel
{
    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    /// <summary>
    /// Converts a continuous-looking buffer into a palette-limited, cluster-cleaned
    /// pixel-art buffer. Neighbour operations wrap at the edges so tileable inputs stay
    /// tileable after cleanup.
    /// </summary>
    public static PixelBuffer Stylize(PixelBuffer source, PixelArtStyleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!profile.Enabled || profile.PaletteSize < 2)
            return source.Clone();

        var width = source.Width;
        var height = source.Height;
        var pixelCount = width * height;
        var adjusted = new Color4[pixelCount];
        var histogram = new Dictionary<int, ColorAccumulator>();
        var sourceData = source.AsReadOnlySpan();

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var a = Math.Clamp(sourceData[offset + 3], 0f, 1f);
            if (a <= 0.001f)
            {
                adjusted[i] = new Color4(0f, 0f, 0f, 0f);
                continue;
            }

            var color = AdjustColor(
                Math.Clamp(sourceData[offset], 0f, 1f),
                Math.Clamp(sourceData[offset + 1], 0f, 1f),
                Math.Clamp(sourceData[offset + 2], 0f, 1f),
                a,
                profile.Contrast,
                profile.Saturation);
            adjusted[i] = color;

            var key = QuantizedKey(color);
            histogram.TryGetValue(key, out var accumulator);
            histogram[key] = accumulator.Add(color);
        }

        if (histogram.Count == 0)
            return source.Clone();

        var palette = BuildPalette(histogram, Math.Min(profile.PaletteSize, histogram.Count), profile.HsvWeighted);
        var indices = new int[pixelCount];
        Array.Fill(indices, -1);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (adjusted[index].A <= 0.001f)
                    continue;

                FindNearest(adjusted[index], palette, profile.HsvWeighted,
                    out var nearest, out var nearestDistance, out var second, out var secondDistance);

                if (profile.DitherStrength > 0f && second >= 0)
                {
                    var secondChance = nearestDistance / Math.Max(0.00001f, nearestDistance + secondDistance);
                    var threshold = (Bayer4[y & 3, x & 3] + 0.5f) / 16f;
                    if (threshold < secondChance * Math.Clamp(profile.DitherStrength, 0f, 1f))
                        nearest = second;
                }

                indices[index] = nearest;
            }
        }

        if (profile.MinimumClusterSize > 1)
            CleanSmallClusters(indices, width, height, palette, profile.MinimumClusterSize, profile.HsvWeighted);

        var result = PixelBufferPool.Borrow(width, height);
        for (var i = 0; i < pixelCount; i++)
        {
            var sourceAlpha = adjusted[i].A;
            if (indices[i] < 0 || sourceAlpha <= 0.001f)
            {
                result.SetPixel(i % width, i / width, 0f, 0f, 0f, 0f);
                continue;
            }

            var color = palette[indices[i]];
            var alpha = profile.PreserveAlpha
                ? SnapAlpha(sourceAlpha)
                : 1f;
            result.SetPixel(i % width, i / width, color.R, color.G, color.B, alpha);
        }

        return result;
    }

    private static Color4 AdjustColor(float r, float g, float b, float a, float contrast, float saturation)
    {
        var luminance = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        r = luminance + (r - luminance) * saturation;
        g = luminance + (g - luminance) * saturation;
        b = luminance + (b - luminance) * saturation;

        r = (r - 0.5f) * contrast + 0.5f;
        g = (g - 0.5f) * contrast + 0.5f;
        b = (b - 0.5f) * contrast + 0.5f;
        return new Color4(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f), a);
    }

    private static float SnapAlpha(float alpha)
    {
        if (alpha <= 0.18f) return 0f;
        if (alpha >= 0.82f) return 1f;
        return MathF.Round(alpha * 3f) / 3f;
    }

    private static int QuantizedKey(Color4 color)
    {
        var r = Math.Clamp((int)MathF.Round(color.R * 31f), 0, 31);
        var g = Math.Clamp((int)MathF.Round(color.G * 31f), 0, 31);
        var b = Math.Clamp((int)MathF.Round(color.B * 31f), 0, 31);
        return (r << 10) | (g << 5) | b;
    }

    private static List<Color4> BuildPalette(Dictionary<int, ColorAccumulator> histogram, int paletteSize,
        bool hsvWeighted)
    {
        var samples = new List<WeightedColor>(histogram.Count);
        foreach (var accumulator in histogram.Values)
            samples.Add(new WeightedColor(accumulator.Average, accumulator.Count));

        samples.Sort((left, right) => right.Weight.CompareTo(left.Weight));
        var palette = new List<Color4>(paletteSize) { samples[0].Color };

        while (palette.Count < paletteSize)
        {
            var bestScore = -1f;
            var best = samples[0].Color;
            foreach (var sample in samples)
            {
                var nearest = float.MaxValue;
                foreach (var color in palette)
                    nearest = Math.Min(nearest, ColorDistance(sample.Color, color, hsvWeighted));

                var score = nearest * MathF.Sqrt(sample.Weight);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = sample.Color;
                }
            }
            palette.Add(best);
        }

        var assignments = new int[samples.Count];
        for (var iteration = 0; iteration < 7; iteration++)
        {
            var sums = new ColorAccumulator[palette.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                FindNearest(samples[i].Color, palette, hsvWeighted, out var nearest, out _, out _, out _);
                assignments[i] = nearest;
                sums[nearest] = sums[nearest].Add(samples[i].Color, samples[i].Weight);
            }

            for (var i = 0; i < palette.Count; i++)
            {
                if (sums[i].Count > 0)
                    palette[i] = sums[i].Average;
            }
        }

        palette.Sort((left, right) => Luminance(left).CompareTo(Luminance(right)));
        return palette;
    }

    private static void FindNearest(Color4 color, IReadOnlyList<Color4> palette, bool hsvWeighted,
        out int nearestIndex, out float nearestDistance, out int secondIndex, out float secondDistance)
    {
        nearestIndex = -1;
        secondIndex = -1;
        nearestDistance = float.MaxValue;
        secondDistance = float.MaxValue;

        for (var i = 0; i < palette.Count; i++)
        {
            var distance = ColorDistance(color, palette[i], hsvWeighted);
            if (distance < nearestDistance)
            {
                secondDistance = nearestDistance;
                secondIndex = nearestIndex;
                nearestDistance = distance;
                nearestIndex = i;
            }
            else if (distance < secondDistance)
            {
                secondDistance = distance;
                secondIndex = i;
            }
        }
    }

    private static float ColorDistance(Color4 left, Color4 right, bool hsvWeighted)
    {
        if (hsvWeighted)
        {
            var leftHsv = ToHsv(left);
            var rightHsv = ToHsv(right);
            var hue = MathF.Abs(leftHsv.H - rightHsv.H);
            hue = MathF.Min(hue, 1f - hue);
            var saturation = leftHsv.S - rightHsv.S;
            var value = leftHsv.V - rightHsv.V;
            var hueWeight = 0.15f + MathF.Min(leftHsv.S, rightHsv.S) * 0.85f;
            return hue * hue * hueWeight * 2.2f + saturation * saturation * 0.72f
                   + value * value * 1.15f;
        }

        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        var leftY = Luminance(left);
        var rightY = Luminance(right);
        var dy = leftY - rightY;
        return dr * dr * 0.24f + dg * dg * 0.48f + db * db * 0.18f + dy * dy * 0.1f;
    }

    private static (float H, float S, float V) ToHsv(Color4 color)
    {
        var maximum = MathF.Max(color.R, MathF.Max(color.G, color.B));
        var minimum = MathF.Min(color.R, MathF.Min(color.G, color.B));
        var delta = maximum - minimum;
        if (delta <= 0.00001f)
            return (0f, 0f, maximum);

        var hue = maximum == color.R
            ? ((color.G - color.B) / delta) % 6f
            : maximum == color.G
                ? (color.B - color.R) / delta + 2f
                : (color.R - color.G) / delta + 4f;
        hue /= 6f;
        if (hue < 0f) hue += 1f;
        return (hue, maximum <= 0f ? 0f : delta / maximum, maximum);
    }

    private static float Luminance(Color4 color)
        => color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;

    private static void CleanSmallClusters(int[] indices, int width, int height,
        IReadOnlyList<Color4> palette, int minimumSize, bool hsvWeighted)
    {
        var visited = new bool[indices.Length];
        var queue = new Queue<int>();
        var component = new List<int>();
        var neighbours = new Dictionary<int, int>();
        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        for (var start = 0; start < indices.Length; start++)
        {
            if (visited[start] || indices[start] < 0)
                continue;

            var target = indices[start];
            queue.Clear();
            component.Clear();
            neighbours.Clear();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                var x = current % width;
                var y = current / width;

                foreach (var (dx, dy) in directions)
                {
                    var nx = (x + dx + width) % width;
                    var ny = (y + dy + height) % height;
                    var neighbour = ny * width + nx;
                    var neighbourColor = indices[neighbour];
                    if (neighbourColor == target)
                    {
                        if (!visited[neighbour])
                        {
                            visited[neighbour] = true;
                            queue.Enqueue(neighbour);
                        }
                    }
                    else if (neighbourColor >= 0)
                    {
                        neighbours.TryGetValue(neighbourColor, out var count);
                        neighbours[neighbourColor] = count + 1;
                    }
                }
            }

            if (component.Count >= minimumSize || neighbours.Count == 0)
                continue;

            var replacement = -1;
            var bestCount = -1;
            var bestDistance = float.MaxValue;
            foreach (var pair in neighbours)
            {
                var distance = ColorDistance(palette[target], palette[pair.Key], hsvWeighted);
                if (pair.Value > bestCount || pair.Value == bestCount && distance < bestDistance)
                {
                    replacement = pair.Key;
                    bestCount = pair.Value;
                    bestDistance = distance;
                }
            }

            if (replacement >= 0)
            {
                foreach (var pixel in component)
                    indices[pixel] = replacement;
            }
        }
    }

    private readonly record struct Color4(float R, float G, float B, float A);
    private readonly record struct WeightedColor(Color4 Color, int Weight);

    private readonly record struct ColorAccumulator(double R, double G, double B, double A, int Count)
    {
        public ColorAccumulator Add(Color4 color, int weight = 1)
            => new(R + color.R * weight, G + color.G * weight, B + color.B * weight,
                A + color.A * weight, Count + weight);

        public Color4 Average => Count == 0
            ? new Color4(0f, 0f, 0f, 0f)
            : new Color4((float)(R / Count), (float)(G / Count), (float)(B / Count), (float)(A / Count));
    }
}
