using System;
using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>
/// Detects the visible subject, crops empty space and places it consistently on
/// the tile.  It supports both transparent sprites and opaque images whose corner
/// colour acts as a background key.
/// </summary>
public sealed class SmartSpriteFramerNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("图像", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("detectMode", "auto",
            ["auto", "alpha", "cornerColor"], ["自动", "透明度", "角落背景色"], "主体检测"),
        NodeParameterDefinition.Integer("padding", 2, 0, 16, 1, "边距（像素）"),
        NodeParameterDefinition.Choice("alignment", "ground",
            ["center", "ground", "top"], ["居中", "底部落地", "顶部对齐"], "主体对齐"),
        NodeParameterDefinition.Number("occupancy", 0.88, 0.2, 1.0, 0.01, "画布占用率"),
        NodeParameterDefinition.Number("alphaThreshold", 0.08, 0, 1, 0.01, "透明度阈值"),
        NodeParameterDefinition.Number("backgroundTolerance", 0.10, 0, 0.6, 0.01, "背景容差"),
        NodeParameterDefinition.Boolean("allowUpscale", true, "允许放大"),
        NodeParameterDefinition.Boolean("pixelSnap", true, "像素吸附")
    };

    public override string TypeName => "SmartSpriteFramer";
    public override string Category => "Smart";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var size = Math.Max(1, context.GetEffectiveSize());
        if (source == null)
            return PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);

        var mode = GetChoice(parameters, "detectMode", "auto");
        var alphaThreshold = Math.Clamp(GetFloat(parameters, "alphaThreshold", 0.08f), 0f, 1f);
        var tolerance = Math.Clamp(GetFloat(parameters, "backgroundTolerance", 0.1f), 0f, 0.6f);
        var hasTransparency = false;
        var data = source.AsReadOnlySpan();
        for (var i = 3; i < data.Length; i += 4)
        {
            if (data[i] < 0.98f) { hasTransparency = true; break; }
        }
        if (mode == "auto")
            mode = hasTransparency ? "alpha" : "cornerColor";

        var corners = new[]
        {
            source.GetPixel(0, 0), source.GetPixel(source.Width - 1, 0),
            source.GetPixel(0, source.Height - 1), source.GetPixel(source.Width - 1, source.Height - 1)
        };
        var background = (
            R: corners.Average(c => c.R), G: corners.Average(c => c.G),
            B: corners.Average(c => c.B), A: corners.Average(c => c.A));
        var toleranceSq = tolerance * tolerance;
        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            var foreground = mode == "alpha"
                ? pixel.A > alphaThreshold
                : pixel.A > alphaThreshold && ColorDistanceSq(pixel, background) > toleranceSq;
            if (!foreground) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (maxX < minX || maxY < minY)
            return ResizeNearest(source, size, size);

        var subjectWidth = maxX - minX + 1;
        var subjectHeight = maxY - minY + 1;
        var padding = Math.Clamp(GetInt(parameters, "padding", 2), 0, Math.Max(0, size / 3));
        var occupancy = Math.Clamp(GetFloat(parameters, "occupancy", 0.88f), 0.2f, 1f);
        var available = Math.Max(1f, (size - padding * 2) * occupancy);
        var scale = Math.Min(available / subjectWidth, available / subjectHeight);
        if (!GetBool(parameters, "allowUpscale", true))
            scale = Math.Min(1f, scale);
        scale = Math.Max(scale, 1f / Math.Max(subjectWidth, subjectHeight));
        var destinationWidth = Math.Max(1, (int)MathF.Round(subjectWidth * scale));
        var destinationHeight = Math.Max(1, (int)MathF.Round(subjectHeight * scale));
        var destinationX = (size - destinationWidth) / 2;
        var alignment = GetChoice(parameters, "alignment", "ground");
        var destinationY = alignment switch
        {
            "top" => padding,
            "center" => (size - destinationHeight) / 2,
            _ => size - padding - destinationHeight
        };
        destinationY = Math.Clamp(destinationY, 0, Math.Max(0, size - destinationHeight));
        var pixelSnap = GetBool(parameters, "pixelSnap", true);

        var result = PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
        for (var y = 0; y < destinationHeight; y++)
        for (var x = 0; x < destinationWidth; x++)
        {
            var pixel = pixelSnap
                ? source.GetPixel(
                    minX + Math.Min(subjectWidth - 1, x * subjectWidth / destinationWidth),
                    minY + Math.Min(subjectHeight - 1, y * subjectHeight / destinationHeight))
                : SampleBilinear(source,
                    minX + (x + 0.5f) * subjectWidth / destinationWidth - 0.5f,
                    minY + (y + 0.5f) * subjectHeight / destinationHeight - 0.5f);
            if (mode == "cornerColor" && ColorDistanceSq(pixel, background) <= toleranceSq)
                pixel = (pixel.R, pixel.G, pixel.B, 0f);
            result.SetPixel(destinationX + x, destinationY + y, pixel.R, pixel.G, pixel.B,
                pixel.A <= alphaThreshold ? 0f : pixel.A);
        }
        return result;
    }

    private static (float R, float G, float B, float A) SampleBilinear(PixelBuffer source, float x, float y)
    {
        var x0 = Math.Clamp((int)MathF.Floor(x), 0, source.Width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(y), 0, source.Height - 1);
        var x1 = Math.Min(source.Width - 1, x0 + 1);
        var y1 = Math.Min(source.Height - 1, y0 + 1);
        var tx = Math.Clamp(x - MathF.Floor(x), 0f, 1f);
        var ty = Math.Clamp(y - MathF.Floor(y), 0f, 1f);
        var a = source.GetPixel(x0, y0);
        var b = source.GetPixel(x1, y0);
        var c = source.GetPixel(x0, y1);
        var d = source.GetPixel(x1, y1);
        return (
            Lerp(Lerp(a.R, b.R, tx), Lerp(c.R, d.R, tx), ty),
            Lerp(Lerp(a.G, b.G, tx), Lerp(c.G, d.G, tx), ty),
            Lerp(Lerp(a.B, b.B, tx), Lerp(c.B, d.B, tx), ty),
            Lerp(Lerp(a.A, b.A, tx), Lerp(c.A, d.A, tx), ty));
    }

    private static float ColorDistanceSq((float R, float G, float B, float A) a,
        (double R, double G, double B, double A) b)
    {
        var dr = a.R - (float)b.R;
        var dg = a.G - (float)b.G;
        var db = a.B - (float)b.B;
        return dr * dr * 0.25f + dg * dg * 0.55f + db * db * 0.20f;
    }

    private static PixelBuffer ResizeNearest(PixelBuffer source, int width, int height)
    {
        var result = PixelBufferPool.Borrow(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = source.GetPixel(
                Math.Min(source.Width - 1, x * source.Width / width),
                Math.Min(source.Height - 1, y * source.Height / height));
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }
}

/// <summary>
/// Automatically constrains palette complexity and removes tiny alpha islands.
/// This is intentionally conservative: it cleans noisy generated art without
/// resampling the image or blurring deliberate pixel clusters.
/// </summary>
public sealed class SmartPixelPolishNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("图像", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("targetStyle", "auto",
            ["auto", "crisp32", "detailed64", "icon", "tile"],
            ["自动判断", "清晰 32 像素", "细致 64 像素", "图标", "无缝图块"], "目标风格"),
        NodeParameterDefinition.Choice("palette", "auto",
            ["auto", "6", "8", "12", "16", "24"], ["自动", "6 色", "8 色", "12 色", "16 色", "24 色"], "调色板"),
        NodeParameterDefinition.Number("cleanup", 0.7, 0, 1, 0.01, "杂点清理"),
        NodeParameterDefinition.Number("contrast", 1.08, 0.7, 1.5, 0.01, "像素对比度"),
        NodeParameterDefinition.Number("saturation", 1.02, 0, 1.8, 0.01, "饱和度"),
        NodeParameterDefinition.Number("alphaThreshold", 0.10, 0, 1, 0.01, "透明度阈值"),
        NodeParameterDefinition.Boolean("dither", false, "有限抖色"),
        NodeParameterDefinition.Boolean("preserveAlpha", true, "保留透明度")
    };

    public override string TypeName => "SmartPixelPolish";
    public override string Category => "Smart";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);

        var prepared = source.Clone();
        var alphaThreshold = Math.Clamp(GetFloat(parameters, "alphaThreshold", 0.1f), 0f, 1f);
        var span = prepared.AsSpan();
        for (var i = 3; i < span.Length; i += 4)
            if (span[i] < alphaThreshold) span[i] = 0f;

        var cleanup = Math.Clamp(GetFloat(parameters, "cleanup", 0.7f), 0f, 1f);
        RemoveTinyAlphaIslands(prepared, cleanup < 0.15f ? 0 : 1 + (int)MathF.Round(cleanup * 3f));
        var style = GetChoice(parameters, "targetStyle", "auto");
        var paletteChoice = GetChoice(parameters, "palette", "auto");
        var paletteSize = int.TryParse(paletteChoice, out var explicitPalette)
            ? explicitPalette
            : style switch
            {
                "crisp32" => 8,
                "detailed64" => 16,
                "icon" => 8,
                "tile" => 10,
                _ => Math.Max(source.Width, source.Height) <= 32 ? 8 : 14
            };
        var minimumCluster = style == "detailed64"
            ? 1 + (int)MathF.Round(cleanup * 2f)
            : 1 + (int)MathF.Round(cleanup * 3f);

        var result = PixelArtKernel.Stylize(prepared, new PixelArtStyleProfile(
            Math.Clamp(paletteSize, 4, 24), Math.Clamp(minimumCluster, 1, 4),
            Math.Clamp(GetFloat(parameters, "contrast", 1.08f), 0.7f, 1.5f),
            Math.Clamp(GetFloat(parameters, "saturation", 1.02f), 0f, 1.8f),
            GetBool(parameters, "dither", false) ? 0.28f : 0f,
            GetBool(parameters, "preserveAlpha", true)));
        prepared.Dispose();
        return result;
    }

    private static void RemoveTinyAlphaIslands(PixelBuffer buffer, int maximumIslandSize)
    {
        if (maximumIslandSize <= 0) return;
        var width = buffer.Width;
        var height = buffer.Height;
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        var component = new List<int>();
        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        for (var start = 0; start < visited.Length; start++)
        {
            if (visited[start] || buffer.GetPixel(start % width, start / width).A <= 0f) continue;
            queue.Clear();
            component.Clear();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                var x = current % width;
                var y = current / width;
                foreach (var (dx, dy) in directions)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    var next = ny * width + nx;
                    if (visited[next] || buffer.GetPixel(nx, ny).A <= 0f) continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (component.Count > maximumIslandSize) continue;
            foreach (var index in component)
            {
                var pixel = buffer.GetPixel(index % width, index / width);
                buffer.SetPixel(index % width, index / width, pixel.R, pixel.G, pixel.B, 0f);
            }
        }
    }
}

/// <summary>
/// Extracts a compact palette from a reference image and maps the source to it
/// using perceptual colour distance.  This provides a predictable local style
/// transfer for tile sets and sprite families without an AI model.
/// </summary>
public sealed class SmartPaletteTransferNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("图像", GraphPortType.Image, "image", true),
        new("风格参考", GraphPortType.Image, "reference", true)
    };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Integer("paletteSize", 10, 3, 24, 1, "参考色数量"),
        NodeParameterDefinition.Number("strength", 0.85, 0, 1, 0.01, "匹配强度"),
        NodeParameterDefinition.Number("preserveLuminance", 0.65, 0, 1, 0.01, "保留原明度"),
        NodeParameterDefinition.Number("alphaThreshold", 0.05, 0, 1, 0.01, "透明度阈值"),
        NodeParameterDefinition.Boolean("finalCleanup", true, "最终像素整理")
    };

    public override string TypeName => "SmartPaletteTransfer";
    public override string Category => "Smart";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var reference = inputs.Length > 1 ? inputs[1] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);
        if (reference == null)
            return source.Clone();

        var paletteSize = Math.Clamp(GetInt(parameters, "paletteSize", 10), 3, 24);
        var alphaThreshold = Math.Clamp(GetFloat(parameters, "alphaThreshold", 0.05f), 0f, 1f);
        var palette = ExtractPalette(reference, paletteSize, alphaThreshold);
        if (palette.Count == 0) return source.Clone();
        var strength = Math.Clamp(GetFloat(parameters, "strength", 0.85f), 0f, 1f);
        var preserveLuminance = Math.Clamp(GetFloat(parameters, "preserveLuminance", 0.65f), 0f, 1f);
        var mapped = PixelBufferPool.Borrow(source.Width, source.Height);

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            if (pixel.A <= alphaThreshold)
            {
                mapped.SetPixel(x, y, 0f, 0f, 0f, 0f);
                continue;
            }

            var nearest = palette[0];
            var nearestDistance = float.MaxValue;
            foreach (var candidate in palette)
            {
                var distance = ColorDistance(pixel.R, pixel.G, pixel.B, candidate.R, candidate.G, candidate.B);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = candidate;
            }
            var sourceLum = Luminance(pixel.R, pixel.G, pixel.B);
            var targetLum = Math.Max(0.001f, Luminance(nearest.R, nearest.G, nearest.B));
            var lumScale = sourceLum / targetLum;
            var adjustedR = Mix(nearest.R, Math.Clamp(nearest.R * lumScale, 0f, 1f), preserveLuminance);
            var adjustedG = Mix(nearest.G, Math.Clamp(nearest.G * lumScale, 0f, 1f), preserveLuminance);
            var adjustedB = Mix(nearest.B, Math.Clamp(nearest.B * lumScale, 0f, 1f), preserveLuminance);
            mapped.SetPixel(x, y,
                Mix(pixel.R, adjustedR, strength), Mix(pixel.G, adjustedG, strength),
                Mix(pixel.B, adjustedB, strength), pixel.A);
        }

        if (!GetBool(parameters, "finalCleanup", true))
            return mapped;
        var cleaned = PixelArtKernel.Stylize(mapped,
            new PixelArtStyleProfile(paletteSize, 1, 1.03f, 1.02f, 0f, true));
        mapped.Dispose();
        return cleaned;
    }

    private static List<(float R, float G, float B)> ExtractPalette(PixelBuffer image, int count, float alphaThreshold)
    {
        var histogram = new Dictionary<int, (double R, double G, double B, int Count)>();
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var pixel = image.GetPixel(x, y);
            if (pixel.A <= alphaThreshold) continue;
            var r = Math.Clamp((int)MathF.Round(pixel.R * 15f), 0, 15);
            var g = Math.Clamp((int)MathF.Round(pixel.G * 15f), 0, 15);
            var b = Math.Clamp((int)MathF.Round(pixel.B * 15f), 0, 15);
            var key = (r << 8) | (g << 4) | b;
            histogram.TryGetValue(key, out var accumulator);
            histogram[key] = (accumulator.R + pixel.R, accumulator.G + pixel.G,
                accumulator.B + pixel.B, accumulator.Count + 1);
        }
        var samples = histogram.Values
            .Select(value => (R: (float)(value.R / value.Count), G: (float)(value.G / value.Count),
                B: (float)(value.B / value.Count), Weight: value.Count))
            .OrderByDescending(value => value.Weight).ToList();
        if (samples.Count == 0) return new();

        var palette = new List<(float R, float G, float B)> { (samples[0].R, samples[0].G, samples[0].B) };
        while (palette.Count < Math.Min(count, samples.Count))
        {
            var bestScore = -1f;
            var best = samples[0];
            foreach (var sample in samples)
            {
                var distance = palette.Min(color => ColorDistance(sample.R, sample.G, sample.B, color.R, color.G, color.B));
                var score = distance * MathF.Sqrt(sample.Weight);
                if (score <= bestScore) continue;
                bestScore = score;
                best = sample;
            }
            palette.Add((best.R, best.G, best.B));
        }
        return palette;
    }

    private static float ColorDistance(float r1, float g1, float b1, float r2, float g2, float b2)
    {
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        var dl = Luminance(r1, g1, b1) - Luminance(r2, g2, b2);
        return dr * dr * 0.24f + dg * dg * 0.48f + db * db * 0.18f + dl * dl * 0.10f;
    }

    private static float Luminance(float r, float g, float b) => r * 0.2126f + g * 0.7152f + b * 0.0722f;
    private static float Mix(float a, float b, float t) => a + (b - a) * t;
}
