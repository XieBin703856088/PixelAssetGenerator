using System;
using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services.AiImage;

internal sealed record PixelArtPostProcessOptions(
    int OutputSize,
    int PaletteSize,
    string Style,
    string VisualStyle,
    string BackgroundMode,
    string Dithering,
    bool AddOutline,
    int Seed);

/// <summary>
/// Converts a diffusion-sized RGB image into grid-aligned game pixels through a
/// structure-aware reduction pyramid, region simplification, palette clustering,
/// pixel-cluster cleanup, mask application and a one-pixel sprite outline.
/// </summary>
internal static class PixelArtPostProcessor
{
    private sealed record PixelStyleProfile(
        int PaletteLimit,
        int SimplifyPasses,
        int ClusterPasses,
        float DitherStrength,
        bool AggressiveClusters);

    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    public static PixelBuffer Process(PixelBuffer source, PixelArtPostProcessOptions options, PixelBuffer? mask)
    {
        ArgumentNullException.ThrowIfNull(source);
        var size = Math.Clamp(options.OutputSize, 8, 256);
        var profile = GetStyleProfile(options.VisualStyle, size);
        var result = StructureAwareResize(source, size, profile);

        var removeBackground = options.BackgroundMode == "transparent"
            || (options.BackgroundMode == "auto" && options.Style is "sprite" or "character" or "icon");
        if (removeBackground)
            RemoveBorderBackground(result);

        SimplifyMicroTexture(result, profile.SimplifyPasses);
        Quantize(
            result,
            Math.Min(Math.Clamp(options.PaletteSize, 4, 32), profile.PaletteLimit),
            options.Dithering == "ordered4x4",
            profile.DitherStrength,
            options.Seed);
        ConsolidatePixelClusters(result, profile.ClusterPasses, profile.AggressiveClusters);
        if (removeBackground)
            CleanAlphaIslands(result);

        if (mask != null)
            ApplyMask(result, mask);

        if (options.AddOutline && removeBackground)
            AddOutline(result);

        return result;
    }

    private static PixelBuffer StructureAwareResize(PixelBuffer source, int targetSize, PixelStyleProfile profile)
    {
        // Keep a 4x structural grid before committing to final logical pixels.
        // Progressive halves preserve boundaries better than a 512 -> 32 average.
        var structuralSize = Math.Min(Math.Min(source.Width, source.Height), targetSize * 4);
        PixelBuffer current = source.Clone();
        try
        {
            while (current.Width > structuralSize || current.Height > structuralSize)
            {
                var nextWidth = Math.Max(structuralSize, current.Width / 2);
                var nextHeight = Math.Max(structuralSize, current.Height / 2);
                var next = AreaResize(current, nextWidth, nextHeight);
                current.Dispose();
                current = next;
            }

            SimplifyMicroTexture(current, Math.Max(1, profile.SimplifyPasses));
            return DominantRegionResize(current, targetSize, targetSize);
        }
        finally
        {
            current.Dispose();
        }
    }

    private static PixelBuffer DominantRegionResize(PixelBuffer source, int width, int height)
    {
        var result = PixelBufferPool.Borrow(width, height);
        for (var y = 0; y < height; y++)
        {
            var y0 = y * source.Height / height;
            var y1 = Math.Max(y0 + 1, (y + 1) * source.Height / height);
            for (var x = 0; x < width; x++)
            {
                var x0 = x * source.Width / width;
                var x1 = Math.Max(x0 + 1, (x + 1) * source.Width / width);
                var regions = new Dictionary<int, (int Count, double R, double G, double B, double A)>();
                var sampleCount = 0;
                for (var sy = y0; sy < Math.Min(source.Height, y1); sy++)
                {
                    for (var sx = x0; sx < Math.Min(source.Width, x1); sx++)
                    {
                        var pixel = source.GetPixel(sx, sy);
                        var key = PackCoarseColor(pixel.R, pixel.G, pixel.B, pixel.A);
                        regions.TryGetValue(key, out var entry);
                        regions[key] = (entry.Count + 1, entry.R + pixel.R, entry.G + pixel.G,
                            entry.B + pixel.B, entry.A + pixel.A);
                        sampleCount++;
                    }
                }

                if (regions.Count == 0)
                {
                    result.SetPixel(x, y, 0, 0, 0, 0);
                    continue;
                }

                var dominant = regions.OrderByDescending(pair => pair.Value.Count).First();
                var center = source.GetPixel(Math.Min(source.Width - 1, (x0 + x1) / 2),
                    Math.Min(source.Height - 1, (y0 + y1) / 2));
                var centerKey = PackCoarseColor(center.R, center.G, center.B, center.A);

                // A repeated high-contrast center feature (for example a window)
                // is structural even when the surrounding wall is more frequent.
                if (regions.TryGetValue(centerKey, out var centerRegion)
                    && centerRegion.Count >= Math.Max(2, sampleCount / 6)
                    && ColorDistance(centerRegion, dominant.Value) >= 0.028f)
                    dominant = new KeyValuePair<int, (int Count, double R, double G, double B, double A)>(centerKey, centerRegion);

                var chosen = dominant.Value;
                var inv = 1d / chosen.Count;
                result.SetPixel(x, y, (float)(chosen.R * inv), (float)(chosen.G * inv),
                    (float)(chosen.B * inv), (float)(chosen.A * inv));
            }
        }
        return result;
    }

    private static float ColorDistance(
        (int Count, double R, double G, double B, double A) first,
        (int Count, double R, double G, double B, double A) second)
    {
        var firstInv = 1d / first.Count;
        var secondInv = 1d / second.Count;
        var dr = first.R * firstInv - second.R * secondInv;
        var dg = first.G * firstInv - second.G * secondInv;
        var db = first.B * firstInv - second.B * secondInv;
        return (float)(dr * dr * 0.30 + dg * dg * 0.59 + db * db * 0.11);
    }

    private static int PackCoarseColor(float r, float g, float b, float a)
        => ((a >= 0.5f ? 1 : 0) << 12)
           | ((int)(Math.Clamp(r, 0, 1) * 15f) << 8)
           | ((int)(Math.Clamp(g, 0, 1) * 15f) << 4)
           | (int)(Math.Clamp(b, 0, 1) * 15f);

    private static PixelStyleProfile GetStyleProfile(string visualStyle, int outputSize)
    {
        var profile = visualStyle switch
        {
            "detailed64" => new PixelStyleProfile(20, 0, 1, 0.45f, false),
            "retro16" => new PixelStyleProfile(10, 2, 3, 0.12f, true),
            "darkFantasy" => new PixelStyleProfile(14, 1, 2, 0.20f, true),
            "cozyRpg" => new PixelStyleProfile(14, 1, 2, 0.18f, true),
            "tacticalSciFi" => new PixelStyleProfile(12, 1, 2, 0.16f, true),
            _ => new PixelStyleProfile(12, 0, 2, 0.14f, false)
        };

        if (outputSize <= 32)
            return profile with
            {
                PaletteLimit = Math.Min(profile.PaletteLimit, 12),
                ClusterPasses = Math.Max(profile.ClusterPasses, 2),
                DitherStrength = Math.Min(profile.DitherStrength, 0.16f)
            };
        return profile;
    }

    private static PixelBuffer AreaResize(PixelBuffer source, int width, int height)
    {
        var result = PixelBufferPool.Borrow(width, height);
        for (var y = 0; y < height; y++)
        {
            var y0 = y * source.Height / height;
            var y1 = Math.Max(y0 + 1, (y + 1) * source.Height / height);
            for (var x = 0; x < width; x++)
            {
                var x0 = x * source.Width / width;
                var x1 = Math.Max(x0 + 1, (x + 1) * source.Width / width);
                double r = 0, g = 0, b = 0, a = 0;
                var count = 0;
                for (var sy = y0; sy < Math.Min(source.Height, y1); sy++)
                {
                    for (var sx = x0; sx < Math.Min(source.Width, x1); sx++)
                    {
                        var pixel = source.GetPixel(sx, sy);
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                        a += pixel.A;
                        count++;
                    }
                }

                var inv = count == 0 ? 0 : 1d / count;
                result.SetPixel(x, y, (float)(r * inv), (float)(g * inv), (float)(b * inv), (float)(a * inv));
            }
        }
        return result;
    }

    private static void RemoveBorderBackground(PixelBuffer image)
    {
        var histogram = new Dictionary<int, (int Count, double R, double G, double B)>();
        void Sample(int x, int y)
        {
            var (r, g, b, _) = image.GetPixel(x, y);
            var key = ((int)(Math.Clamp(r, 0, 1) * 15) << 8)
                    | ((int)(Math.Clamp(g, 0, 1) * 15) << 4)
                    | (int)(Math.Clamp(b, 0, 1) * 15);
            histogram.TryGetValue(key, out var entry);
            histogram[key] = (entry.Count + 1, entry.R + r, entry.G + g, entry.B + b);
        }

        for (var x = 0; x < image.Width; x++) { Sample(x, 0); Sample(x, image.Height - 1); }
        for (var y = 1; y < image.Height - 1; y++) { Sample(0, y); Sample(image.Width - 1, y); }
        if (histogram.Count == 0) return;

        var dominant = histogram.Values.OrderByDescending(v => v.Count).First();
        var background = ((float)(dominant.R / dominant.Count), (float)(dominant.G / dominant.Count), (float)(dominant.B / dominant.Count));
        var visited = new bool[image.Width * image.Height];
        var queue = new Queue<(int X, int Y)>();

        void Enqueue(int x, int y)
        {
            var index = y * image.Width + x;
            if (visited[index]) return;
            visited[index] = true;
            var (r, g, b, _) = image.GetPixel(x, y);
            var distance = (r - background.Item1) * (r - background.Item1)
                         + (g - background.Item2) * (g - background.Item2)
                         + (b - background.Item3) * (b - background.Item3);
            if (distance <= 0.040f)
                queue.Enqueue((x, y));
        }

        for (var x = 0; x < image.Width; x++) { Enqueue(x, 0); Enqueue(x, image.Height - 1); }
        for (var y = 1; y < image.Height - 1; y++) { Enqueue(0, y); Enqueue(image.Width - 1, y); }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            image.SetPixel(x, y, 0, 0, 0, 0);
            if (x > 0) Enqueue(x - 1, y);
            if (x + 1 < image.Width) Enqueue(x + 1, y);
            if (y > 0) Enqueue(x, y - 1);
            if (y + 1 < image.Height) Enqueue(x, y + 1);
        }
    }

    private static void SimplifyMicroTexture(PixelBuffer image, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            using var snapshot = image.Clone();
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var current = snapshot.GetPixel(x, y);
                    if (current.A < 0.5f) continue;

                    double r = current.R, g = current.G, b = current.B;
                    var count = 1;
                    for (var oy = -1; oy <= 1; oy++)
                    {
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            var nx = x + ox;
                            var ny = y + oy;
                            if (nx < 0 || nx >= image.Width || ny < 0 || ny >= image.Height) continue;
                            var neighbor = snapshot.GetPixel(nx, ny);
                            if (neighbor.A < 0.5f) continue;
                            var dr = current.R - neighbor.R;
                            var dg = current.G - neighbor.G;
                            var db = current.B - neighbor.B;
                            if (dr * dr * 0.30f + dg * dg * 0.59f + db * db * 0.11f > 0.012f) continue;
                            r += neighbor.R;
                            g += neighbor.G;
                            b += neighbor.B;
                            count++;
                        }
                    }

                    if (count < 3) continue;
                    const float blend = 0.55f;
                    image.SetPixel(x, y,
                        current.R * (1 - blend) + (float)(r / count) * blend,
                        current.G * (1 - blend) + (float)(g / count) * blend,
                        current.B * (1 - blend) + (float)(b / count) * blend,
                        current.A);
                }
            }
        }
    }

    private static void Quantize(PixelBuffer image, int paletteSize, bool dither, float ditherStrength, int seed)
    {
        var samples = new List<(float R, float G, float B)>();
        var histogram = new Dictionary<int, (int Count, float R, float G, float B)>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (r, g, b, a) = image.GetPixel(x, y);
                if (a < 0.5f) continue;
                samples.Add((r, g, b));
                var key = ((int)(Math.Clamp(r, 0, 1) * 31) << 10)
                        | ((int)(Math.Clamp(g, 0, 1) * 31) << 5)
                        | (int)(Math.Clamp(b, 0, 1) * 31);
                histogram.TryGetValue(key, out var entry);
                histogram[key] = (entry.Count + 1, r, g, b);
            }
        }

        if (samples.Count == 0) return;
        paletteSize = Math.Min(paletteSize, histogram.Count);
        var centers = histogram.Values
            .OrderByDescending(v => v.Count)
            .ThenBy(v => ((v.R * 3 + v.G * 5 + v.B * 7) * 1000 + seed) % 997)
            .Take(paletteSize)
            .Select(v => (v.R, v.G, v.B))
            .ToArray();

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var sums = new (double R, double G, double B, int Count)[centers.Length];
            foreach (var sample in samples)
            {
                var index = Nearest(centers, sample.R, sample.G, sample.B);
                sums[index].R += sample.R;
                sums[index].G += sample.G;
                sums[index].B += sample.B;
                sums[index].Count++;
            }

            for (var i = 0; i < centers.Length; i++)
            {
                if (sums[i].Count == 0) continue;
                centers[i] = ((float)(sums[i].R / sums[i].Count),
                    (float)(sums[i].G / sums[i].Count),
                    (float)(sums[i].B / sums[i].Count));
            }
        }

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (r, g, b, a) = image.GetPixel(x, y);
                if (a < 0.5f) { image.SetPixel(x, y, 0, 0, 0, 0); continue; }
                var offset = dither ? (Bayer4[y & 3, x & 3] - 7.5f) / 255f * ditherStrength : 0f;
                var index = Nearest(centers, r + offset, g + offset, b + offset);
                var color = centers[index];
                image.SetPixel(x, y,
                    MathF.Round(color.Item1 * 31f) / 31f,
                    MathF.Round(color.Item2 * 31f) / 31f,
                    MathF.Round(color.Item3 * 31f) / 31f,
                    1f);
            }
        }
    }

    private static void ConsolidatePixelClusters(PixelBuffer image, int passes, bool aggressive)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            using var snapshot = image.Clone();
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var current = snapshot.GetPixel(x, y);
                    if (current.A < 0.5f) continue;
                    var currentKey = PackColor(current.R, current.G, current.B);
                    var counts = new Dictionary<int, (int Count, float R, float G, float B)>();
                    var sameNeighbors = 0;

                    for (var oy = -1; oy <= 1; oy++)
                    {
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            var nx = x + ox;
                            var ny = y + oy;
                            if (nx < 0 || nx >= image.Width || ny < 0 || ny >= image.Height) continue;
                            var neighbor = snapshot.GetPixel(nx, ny);
                            if (neighbor.A < 0.5f) continue;
                            var key = PackColor(neighbor.R, neighbor.G, neighbor.B);
                            if (key == currentKey) sameNeighbors++;
                            counts.TryGetValue(key, out var entry);
                            counts[key] = (entry.Count + 1, neighbor.R, neighbor.G, neighbor.B);
                        }
                    }

                    if (counts.Count == 0 || sameNeighbors > (aggressive && pass > 0 ? 1 : 0)) continue;
                    var dominant = counts.Values.OrderByDescending(entry => entry.Count).First();
                    var required = aggressive && pass > 0 ? 4 : 3;
                    if (dominant.Count < required) continue;
                    image.SetPixel(x, y, dominant.R, dominant.G, dominant.B, 1f);
                }
            }
        }
    }

    private static void CleanAlphaIslands(PixelBuffer image)
    {
        using var snapshot = image.Clone();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var current = snapshot.GetPixel(x, y);
                var opaqueNeighbors = new List<(float R, float G, float B)>();
                for (var oy = -1; oy <= 1; oy++)
                {
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        var nx = x + ox;
                        var ny = y + oy;
                        if (nx < 0 || nx >= image.Width || ny < 0 || ny >= image.Height) continue;
                        var neighbor = snapshot.GetPixel(nx, ny);
                        if (neighbor.A >= 0.5f)
                            opaqueNeighbors.Add((neighbor.R, neighbor.G, neighbor.B));
                    }
                }

                if (current.A >= 0.5f && opaqueNeighbors.Count <= 1)
                    image.SetPixel(x, y, 0, 0, 0, 0);
                else if (current.A < 0.5f && opaqueNeighbors.Count >= 7)
                {
                    var fill = opaqueNeighbors
                        .GroupBy(color => PackColor(color.R, color.G, color.B))
                        .OrderByDescending(group => group.Count())
                        .First().First();
                    image.SetPixel(x, y, fill.R, fill.G, fill.B, 1f);
                }
            }
        }
    }

    private static int PackColor(float r, float g, float b)
        => ((int)MathF.Round(Math.Clamp(r, 0, 1) * 255f) << 16)
           | ((int)MathF.Round(Math.Clamp(g, 0, 1) * 255f) << 8)
           | (int)MathF.Round(Math.Clamp(b, 0, 1) * 255f);

    private static int Nearest((float R, float G, float B)[] centers, float r, float g, float b)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < centers.Length; i++)
        {
            var dr = r - centers[i].R;
            var dg = g - centers[i].G;
            var db = b - centers[i].B;
            // Perceptual weighting keeps small sprites from wasting palette slots on blue noise.
            var distance = dr * dr * 0.30f + dg * dg * 0.59f + db * db * 0.11f;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    private static void ApplyMask(PixelBuffer image, PixelBuffer mask)
    {
        for (var y = 0; y < image.Height; y++)
        {
            var maskY = Math.Min(mask.Height - 1, y * mask.Height / image.Height);
            for (var x = 0; x < image.Width; x++)
            {
                var maskX = Math.Min(mask.Width - 1, x * mask.Width / image.Width);
                var (mr, mg, mb, ma) = mask.GetPixel(maskX, maskY);
                var value = ma < 0.999f ? ma : 0.2126f * mr + 0.7152f * mg + 0.0722f * mb;
                var (r, g, b, a) = image.GetPixel(x, y);
                var alpha = a * Math.Clamp(value, 0f, 1f);
                image.SetPixel(x, y, r, g, b, alpha < 0.5f ? 0f : 1f);
            }
        }
    }

    private static void AddOutline(PixelBuffer image)
    {
        var alpha = new bool[image.Width * image.Height];
        float minLuminance = float.MaxValue;
        (float R, float G, float B) darkest = (0.08f, 0.07f, 0.11f);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (r, g, b, a) = image.GetPixel(x, y);
                alpha[y * image.Width + x] = a >= 0.5f;
                if (a < 0.5f) continue;
                var luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                if (luminance < minLuminance) { minLuminance = luminance; darkest = (r, g, b); }
            }
        }

        var outline = (Math.Clamp(darkest.R * 0.55f, 0.02f, 0.22f),
            Math.Clamp(darkest.G * 0.55f, 0.02f, 0.22f),
            Math.Clamp(darkest.B * 0.55f, 0.025f, 0.25f));
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = y * image.Width + x;
                if (alpha[index]) continue;
                var touches = (x > 0 && alpha[index - 1])
                    || (x + 1 < image.Width && alpha[index + 1])
                    || (y > 0 && alpha[index - image.Width])
                    || (y + 1 < image.Height && alpha[index + image.Width]);
                if (touches)
                    image.SetPixel(x, y, outline.Item1, outline.Item2, outline.Item3, 1f);
            }
        }
    }
}
