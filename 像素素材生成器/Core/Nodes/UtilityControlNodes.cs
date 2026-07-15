using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>Extracts a deterministic palette using uniform or k-means sampling.</summary>
public sealed class PaletteExtractionNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs = [new("图像", GraphPortType.Image, "image", true)];
    private static readonly GraphNodePort[] Outputs =
    [
        new("量化图像", GraphPortType.Image, "image"),
        new("调色板", GraphPortType.Image, "palette")
    ];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Integer("colorCount", 8, 1, 32, 1, "颜色数量"),
        NodeParameterDefinition.Choice("sampleMode", "uniform", ["uniform", "kmeans"],
            ["均匀采样", "K-Means 聚类"], "采样方式"),
        NodeParameterDefinition.Choice("sortMode", "luminance", ["luminance", "hue", "saturation"],
            ["按明度", "按色相", "按饱和度"], "排序方式"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    ];

    public override string TypeName => "ColorExtraction";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        var result = outputs[0].Clone();
        foreach (var output in outputs) output.Dispose();
        return result;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var outputSize = Math.Max(1, context.GetEffectiveSize());
        if (source == null)
            return
            [
                PixelBuffer.CreateSolid(outputSize, outputSize, 0, 0, 0, 0),
                PixelBuffer.CreateSolid(outputSize, outputSize, 0, 0, 0, 0)
            ];

        var requestedCount = Math.Clamp(GetInt(parameters, "colorCount", 8), 1, 32);
        var seed = GetInt(parameters, "seed", context.Seed);
        var samples = new List<PaletteColor>(source.Width * source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            if (pixel.A > 0.05f)
                samples.Add(new PaletteColor(pixel.R, pixel.G, pixel.B));
        }

        if (samples.Count == 0)
            return
            [
                PixelBuffer.CreateSolid(outputSize, outputSize, 0, 0, 0, 0),
                PixelBuffer.CreateSolid(outputSize, outputSize, 0, 0, 0, 0)
            ];

        var count = Math.Min(requestedCount, samples.Count);
        var colors = string.Equals(GetChoice(parameters, "sampleMode", "uniform"), "kmeans",
            StringComparison.OrdinalIgnoreCase)
            ? Cluster(samples, count, seed)
            : UniformSamples(samples, count, seed);

        var sortMode = GetChoice(parameters, "sortMode", "luminance");
        colors.Sort((left, right) => SortValue(left, sortMode).CompareTo(SortValue(right, sortMode)));
        var palette = PixelBufferPool.Borrow(outputSize, outputSize);
        for (var y = 0; y < outputSize; y++)
        for (var x = 0; x < outputSize; x++)
        {
            var index = Math.Min(colors.Count - 1, x * colors.Count / outputSize);
            var color = colors[index];
            palette.SetPixel(x, y, color.R, color.G, color.B, 1f);
        }

        var quantized = PixelBufferPool.Borrow(outputSize, outputSize);
        for (var y = 0; y < outputSize; y++)
        for (var x = 0; x < outputSize; x++)
        {
            var sourcePixel = source.GetPixel(
                Math.Min(source.Width - 1, x * source.Width / outputSize),
                Math.Min(source.Height - 1, y * source.Height / outputSize));
            var sample = new PaletteColor(sourcePixel.R, sourcePixel.G, sourcePixel.B);
            var nearest = colors[0];
            var nearestDistance = Distance(sample, nearest);
            for (var i = 1; i < colors.Count; i++)
            {
                var distance = Distance(sample, colors[i]);
                if (distance >= nearestDistance) continue;
                nearest = colors[i];
                nearestDistance = distance;
            }
            quantized.SetPixel(x, y, nearest.R, nearest.G, nearest.B, sourcePixel.A);
        }
        return [quantized, palette];
    }

    private static List<PaletteColor> UniformSamples(IReadOnlyList<PaletteColor> samples, int count, int seed)
    {
        var result = new List<PaletteColor>(count);
        var offset = (int)(HashToUnit(seed, count, 811) * samples.Count);
        for (var i = 0; i < count; i++)
            result.Add(samples[(offset + i * samples.Count / count) % samples.Count]);
        return result;
    }

    private static List<PaletteColor> Cluster(IReadOnlyList<PaletteColor> samples, int count, int seed)
    {
        var centers = new PaletteColor[count];
        for (var i = 0; i < count; i++)
        {
            var index = Math.Min(samples.Count - 1,
                (int)(HashToUnit(i, count, seed + 1709) * samples.Count));
            centers[i] = samples[index];
        }

        var assignments = new int[samples.Count];
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var sumR = new double[count]; var sumG = new double[count]; var sumB = new double[count];
            var totals = new int[count];
            for (var i = 0; i < samples.Count; i++)
            {
                var nearest = 0;
                var nearestDistance = float.MaxValue;
                for (var center = 0; center < count; center++)
                {
                    var distance = Distance(samples[i], centers[center]);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearest = center;
                }
                assignments[i] = nearest;
                sumR[nearest] += samples[i].R; sumG[nearest] += samples[i].G; sumB[nearest] += samples[i].B;
                totals[nearest]++;
            }

            for (var center = 0; center < count; center++)
                if (totals[center] > 0)
                    centers[center] = new PaletteColor((float)(sumR[center] / totals[center]),
                        (float)(sumG[center] / totals[center]), (float)(sumB[center] / totals[center]));
        }
        return centers.ToList();
    }

    private static float Distance(PaletteColor left, PaletteColor right)
    {
        var dr = left.R - right.R; var dg = left.G - right.G; var db = left.B - right.B;
        return dr * dr * 0.25f + dg * dg * 0.55f + db * db * 0.20f;
    }

    private static float SortValue(PaletteColor color, string mode)
    {
        var hsv = ToHsv(color);
        return mode switch { "hue" => hsv.H, "saturation" => hsv.S, _ => Luminance(color) };
    }

    private static float Luminance(PaletteColor color) => color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;

    private static (float H, float S) ToHsv(PaletteColor color)
    {
        var maximum = MathF.Max(color.R, MathF.Max(color.G, color.B));
        var minimum = MathF.Min(color.R, MathF.Min(color.G, color.B));
        var delta = maximum - minimum;
        if (delta <= 0.00001f) return (0f, 0f);
        var hue = maximum == color.R ? ((color.G - color.B) / delta) % 6f
            : maximum == color.G ? (color.B - color.R) / delta + 2f
            : (color.R - color.G) / delta + 4f;
        hue /= 6f;
        if (hue < 0f) hue += 1f;
        return (hue, maximum <= 0f ? 0f : delta / maximum);
    }

    private readonly record struct PaletteColor(float R, float G, float B);
}

/// <summary>A single pixel-art brush stamp. Path spacing was removed because this node has no path input.</summary>
public sealed class BrushStampNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    [
        new("位置", GraphPortType.Float, "position"),
        new("颜色", GraphPortType.Color, "color")
    ];
    private static readonly GraphNodePort[] Outputs = [new("图像", GraphPortType.Image, "image")];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Color("color", Colors.White, "颜色"),
        NodeParameterDefinition.Integer("brushSize", 4, 1, 64, 1, "画笔大小"),
        NodeParameterDefinition.Number("hardness", 1.0, 0, 1, 0.01, "硬度")
    ];

    public override string TypeName => "Brush";
    public override string Category => "Logic";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var position = inputs.Length > 0 && inputs[0] != null ? inputs[0]!.GetPixel(0, 0) : (0.5f, 0.5f, 0f, 1f);
        var selectedColor = GetColor(parameters, "color", Colors.White);
        var r = selectedColor.R / 255f; var g = selectedColor.G / 255f;
        var b = selectedColor.B / 255f; var baseAlpha = selectedColor.A / 255f;
        if (inputs.Length > 1 && inputs[1] != null)
        {
            var inputColor = inputs[1]!.GetPixel(0, 0);
            r = inputColor.R; g = inputColor.G; b = inputColor.B; baseAlpha = inputColor.A;
        }
        var radius = Math.Max(0.5f, GetInt(parameters, "brushSize", 4) * 0.5f);
        var hardness = Math.Clamp(GetFloat(parameters, "hardness", 1f), 0f, 1f);
        var centerX = Math.Clamp(position.Item1, 0f, 1f) * (size - 1);
        var centerY = Math.Clamp(position.Item2, 0f, 1f) * (size - 1);
        var result = PixelBuffer.CreateSolid(size, size, 0, 0, 0, 0);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)) / radius;
            if (distance > 1f) continue;
            var coverage = distance <= hardness || hardness >= 0.999f
                ? 1f : 1f - (distance - hardness) / Math.Max(0.001f, 1f - hardness);
            result.SetPixel(x, y, r, g, b, Math.Clamp(baseAlpha * coverage, 0f, 1f));
        }
        return result;
    }
}

/// <summary>Caches an image per user-selected slot for a configurable number of animation frames.</summary>
public sealed class ImageCacheNode : GraphNodeBase, IMultiOutputNode, IPersistentStateNode
{
    private static readonly GraphNodePort[] Inputs = [new("源图像", GraphPortType.Image, "image")];
    private static readonly GraphNodePort[] Outputs =
    [
        new("图像", GraphPortType.Image, "image"), new("遮罩", GraphPortType.Mask, "mask")
    ];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("cacheKey", "default", ["default", "cache1", "cache2", "cache3", "cache4", "cache5"],
            ["默认", "缓存 1", "缓存 2", "缓存 3", "缓存 4", "缓存 5"], "缓存槽"),
        NodeParameterDefinition.Integer("expireFrames", 0, 0, 100, 1, "过期帧数")
    ];

    public override string TypeName => "Cache";
    public override string Category => "Logic";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful;
    public string PersistentStateKey => "ImageCache";
    public object? PersistentState { get; set; }

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var state = PersistentState as CacheState ?? new CacheState();
        PersistentState = state;
        var key = GetChoice(parameters, "cacheKey", "default");
        var expireFrames = Math.Clamp(GetInt(parameters, "expireFrames", 0), 0, 100);
        var frame = context.AnimationFrame ?? (int)MathF.Round(context.GlobalTime * 60f);
        if (state.Entries.TryGetValue(key, out var entry)
            && (expireFrames == 0 || frame - entry.Frame < expireFrames))
            return entry.Image.Clone();

        if (entry != null)
        {
            entry.Image.Dispose();
            state.Entries.Remove(key);
        }
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0, 0, 0, 0);
        var cached = source.Clone();
        state.Entries[key] = new CacheEntry(cached, frame);
        return cached.Clone();
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var image = Process(inputs, parameters, context);
        return [image, PixelBuffer.CreateMaskView(image)];
    }

    public sealed class CacheState : IDisposable
    {
        internal Dictionary<string, CacheEntry> Entries { get; } = new(StringComparer.Ordinal);
        public void Dispose()
        {
            foreach (var entry in Entries.Values) entry.Image.Dispose();
            Entries.Clear();
        }
    }

    internal sealed record CacheEntry(PixelBuffer Image, int Frame);
}

/// <summary>Outputs synchronized scalar and color representations of a constant.</summary>
public sealed class ConstantValueNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Outputs =
    [
        new("数值", GraphPortType.Float, "value"), new("颜色", GraphPortType.Color, "color")
    ];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("outputType", "float", ["float", "color"], ["数值", "颜色"], "常量类型"),
        NodeParameterDefinition.Number("floatValue", 0.5, -10, 10, 0.01, "数值"),
        NodeParameterDefinition.Color("colorValue", Colors.White, "颜色")
    ];

    public override string TypeName => "Constant";
    public override string Category => "Logic";
    public override IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        var selected = string.Equals(GetChoice(parameters, "outputType", "float"), "color", StringComparison.OrdinalIgnoreCase)
            ? outputs[1].Clone() : outputs[0].Clone();
        outputs[0].Dispose(); outputs[1].Dispose();
        return selected;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var mode = GetChoice(parameters, "outputType", "float");
        var value = GetFloat(parameters, "floatValue", 0.5f);
        var color = GetColor(parameters, "colorValue", Colors.White);
        var colorR = color.R / 255f; var colorG = color.G / 255f;
        var colorB = color.B / 255f; var colorA = color.A / 255f;
        if (string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase))
            value = colorR * 0.2126f + colorG * 0.7152f + colorB * 0.0722f;
        else
            colorR = colorG = colorB = value;
        return
        [
            PixelBuffer.CreateSolid(1, 1, value, value, value, 1f),
            PixelBuffer.CreateSolid(1, 1, colorR, colorG, colorB, colorA)
        ];
    }
}

/// <summary>Computes image statistics and exposes each metric through a real scalar output.</summary>
public sealed class ImageAnalysisNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs = [new("图像", GraphPortType.Image, "image", true)];
    private static readonly GraphNodePort[] Outputs =
    [
        new("平均明度", GraphPortType.Float, "averageLuminance"),
        new("对比度", GraphPortType.Float, "contrast"),
        new("熵", GraphPortType.Float, "entropy"),
        new("边缘密度", GraphPortType.Float, "edgeDensity"),
        new("主色相", GraphPortType.Float, "dominantHue"),
        new("饱和度", GraphPortType.Float, "saturation"),
        new("图像", GraphPortType.Image, "image")
    ];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("analysisMode", "all", ["all", "basic", "color", "structure"],
            ["全部", "基础统计", "颜色", "结构"], "分析模式")
    ];

    public override string TypeName => "ImageAnalysis";
    public override string Category => "Logic";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        var result = outputs[0].Clone();
        foreach (var output in outputs) output.Dispose();
        return result;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            source = PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0, 0, 0, 0);
        var ownsSource = inputs.Length == 0 || inputs[0] == null;
        try
        {
            var mode = GetChoice(parameters, "analysisMode", "all");
            var includeBasic = mode is "all" or "basic";
            var includeColor = mode is "all" or "color";
            var includeStructure = mode is "all" or "structure";
            var metrics = Analyze(source);
            return
            [
                Scalar(includeBasic ? metrics.AverageLuminance : 0f),
                Scalar(includeBasic ? metrics.Contrast : 0f),
                Scalar(includeBasic ? metrics.Entropy : 0f),
                Scalar(includeStructure ? metrics.EdgeDensity : 0f),
                Scalar(includeColor ? metrics.DominantHue : 0f),
                Scalar(includeColor ? metrics.Saturation : 0f),
                source.Clone()
            ];
        }
        finally
        {
            if (ownsSource) source.Dispose();
        }
    }

    private static AnalysisMetrics Analyze(PixelBuffer source)
    {
        var count = Math.Max(1, source.Width * source.Height);
        var histogram = new int[64];
        var hueHistogram = new float[36];
        double luminanceSum = 0; double luminanceSquareSum = 0; double saturationSum = 0;
        var edges = 0;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            var luminance = pixel.R * 0.2126f + pixel.G * 0.7152f + pixel.B * 0.0722f;
            luminanceSum += luminance; luminanceSquareSum += luminance * luminance;
            histogram[Math.Clamp((int)(luminance * 63f), 0, 63)]++;
            var (hue, saturation) = ToHsv(pixel.R, pixel.G, pixel.B);
            saturationSum += saturation;
            hueHistogram[Math.Clamp((int)(hue * 36f), 0, 35)] += Math.Max(0.001f, saturation);
            if (x > 0 && MathF.Abs(luminance - Luminance(source.GetPixel(x - 1, y))) > 0.12f) edges++;
            if (y > 0 && MathF.Abs(luminance - Luminance(source.GetPixel(x, y - 1))) > 0.12f) edges++;
        }
        var average = (float)(luminanceSum / count);
        var variance = Math.Max(0d, luminanceSquareSum / count - average * average);
        double entropy = 0;
        foreach (var bin in histogram)
        {
            if (bin == 0) continue;
            var probability = bin / (double)count;
            entropy -= probability * Math.Log2(probability);
        }
        var dominantBin = Array.IndexOf(hueHistogram, hueHistogram.Max());
        return new AnalysisMetrics(average, (float)Math.Sqrt(variance), (float)(entropy / 6d),
            edges / (float)Math.Max(1, count * 2), (dominantBin + 0.5f) / 36f,
            (float)(saturationSum / count));
    }

    private static float Luminance((float R, float G, float B, float A) pixel)
        => pixel.R * 0.2126f + pixel.G * 0.7152f + pixel.B * 0.0722f;

    private static (float H, float S) ToHsv(float r, float g, float b)
    {
        var maximum = MathF.Max(r, MathF.Max(g, b));
        var minimum = MathF.Min(r, MathF.Min(g, b));
        var delta = maximum - minimum;
        if (delta <= 0.00001f) return (0f, 0f);
        var hue = maximum == r ? ((g - b) / delta) % 6f
            : maximum == g ? (b - r) / delta + 2f : (r - g) / delta + 4f;
        hue /= 6f;
        if (hue < 0f) hue += 1f;
        return (hue, maximum <= 0f ? 0f : delta / maximum);
    }

    private static PixelBuffer Scalar(float value) => PixelBuffer.CreateSolid(1, 1, value, value, value, 1f);
    private readonly record struct AnalysisMetrics(float AverageLuminance, float Contrast, float Entropy,
        float EdgeDensity, float DominantHue, float Saturation);
}

/// <summary>Final graph output with real size, alpha and background controls.</summary>
public sealed class GraphOutputNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = [new("图像", GraphPortType.Image, "image", true)];
    private static readonly GraphNodePort[] Outputs = [new("图像", GraphPortType.Image, "image")];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("outputSize", "32", ["8", "16", "32", "48", "64", "96", "128", "256", "512"], "输出尺寸"),
        NodeParameterDefinition.Number("outputScale", 1.0, 0.25, 4, 0.25, "输出倍率"),
        NodeParameterDefinition.Choice("outputFormat", "PNG", ["PNG", "BMP"], ["PNG", "BMP"], "输出格式"),
        NodeParameterDefinition.Boolean("premultipliedAlpha", false, "预乘透明度"),
        NodeParameterDefinition.Choice("background", "transparent", ["transparent", "black", "white", "checkerboard"],
            ["透明", "黑色", "白色", "棋盘格"], "背景")
    ];

    public override string TypeName => "Output";
    public override string Category => "Tool";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var sizeChoice = GetChoice(parameters, "outputSize", context.GetEffectiveSize().ToString(CultureInfo.InvariantCulture));
        var baseSize = int.TryParse(sizeChoice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : context.GetEffectiveSize();
        var targetSize = Math.Clamp((int)MathF.Round(baseSize * Math.Clamp(GetFloat(parameters, "outputScale", 1f), 0.25f, 4f)), 8, 512);
        if (source == null)
            return PixelBuffer.CreateSolid(targetSize, targetSize, 0, 0, 0, 0);
        var format = GetChoice(parameters, "outputFormat", "PNG");
        var background = GetChoice(parameters, "background", "transparent");
        if (string.Equals(format, "BMP", StringComparison.OrdinalIgnoreCase) && background == "transparent")
            background = "black";
        var premultiply = GetBool(parameters, "premultipliedAlpha", false);
        var result = PixelBufferPool.Borrow(targetSize, targetSize);
        for (var y = 0; y < targetSize; y++)
        for (var x = 0; x < targetSize; x++)
        {
            var pixel = source.GetPixel(Math.Min(source.Width - 1, x * source.Width / targetSize),
                Math.Min(source.Height - 1, y * source.Height / targetSize));
            var bg = Background(background, x, y);
            if (background != "transparent")
            {
                pixel.R = pixel.R * pixel.A + bg.R * (1f - pixel.A);
                pixel.G = pixel.G * pixel.A + bg.G * (1f - pixel.A);
                pixel.B = pixel.B * pixel.A + bg.B * (1f - pixel.A);
                pixel.A = 1f;
            }
            else if (premultiply)
            {
                pixel.R *= pixel.A; pixel.G *= pixel.A; pixel.B *= pixel.A;
            }
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }

    private static (float R, float G, float B) Background(string mode, int x, int y) => mode switch
    {
        "white" => (1f, 1f, 1f), "checkerboard" => ((x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f,
            (x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f, (x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f),
        _ => (0f, 0f, 0f)
    };
}

/// <summary>Inspectable preview transform for channels, tiling, background and zoom.</summary>
public sealed class GraphPreviewNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = [new("输入", GraphPortType.Any, "input")];
    private static readonly GraphNodePort[] Outputs = [new("图像", GraphPortType.Image, "image")];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("displayMode", "color", ["color", "R", "G", "B", "alpha", "grayscale"],
            ["彩色", "红通道", "绿通道", "蓝通道", "透明度", "灰度"], "显示模式"),
        NodeParameterDefinition.Integer("tilePreview", 1, 1, 8, 1, "平铺数量"),
        NodeParameterDefinition.Choice("background", "checkerboard", ["checkerboard", "black", "white", "transparent"],
            ["棋盘格", "黑色", "白色", "透明"], "背景"),
        NodeParameterDefinition.Number("scale", 1.0, 0.25, 4, 0.05, "预览缩放")
    ];

    public override string TypeName => "Preview";
    public override string Category => "Tool";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var size = context.GetEffectiveSize();
        if (source == null)
            return PixelBuffer.CreateSolid(size, size, 0, 0, 0, 0);
        var mode = GetChoice(parameters, "displayMode", "color");
        var tiles = Math.Clamp(GetInt(parameters, "tilePreview", 1), 1, 8);
        var background = GetChoice(parameters, "background", "checkerboard");
        var scale = Math.Clamp(GetFloat(parameters, "scale", 1f), 0.25f, 4f);
        var result = PixelBufferPool.Borrow(size, size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var centeredX = (x + 0.5f - size * 0.5f) / scale + size * 0.5f;
            var centeredY = (y + 0.5f - size * 0.5f) / scale + size * 0.5f;
            var u = PositiveFraction(centeredX / size * tiles);
            var v = PositiveFraction(centeredY / size * tiles);
            var pixel = source.GetPixel(Math.Min(source.Width - 1, (int)(u * source.Width)),
                Math.Min(source.Height - 1, (int)(v * source.Height)));
            var value = mode switch
            {
                "R" => pixel.R, "G" => pixel.G, "B" => pixel.B, "alpha" => pixel.A,
                "grayscale" => pixel.R * 0.2126f + pixel.G * 0.7152f + pixel.B * 0.0722f,
                _ => -1f
            };
            if (value >= 0f) pixel.R = pixel.G = pixel.B = value;
            var bg = GraphOutputNodeBackground(background, x, y);
            if (background != "transparent")
            {
                pixel.R = pixel.R * pixel.A + bg.R * (1f - pixel.A);
                pixel.G = pixel.G * pixel.A + bg.G * (1f - pixel.A);
                pixel.B = pixel.B * pixel.A + bg.B * (1f - pixel.A);
                pixel.A = 1f;
            }
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }

    private static float PositiveFraction(float value) => value - MathF.Floor(value);
    private static (float R, float G, float B) GraphOutputNodeBackground(string mode, int x, int y) => mode switch
    {
        "white" => (1f, 1f, 1f), "checkerboard" => ((x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f,
            (x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f, (x / 4 + y / 4) % 2 == 0 ? 0.28f : 0.48f),
        _ => (0f, 0f, 0f)
    };
}

/// <summary>Pixel-snapped text with explicit per-character spacing.</summary>
public sealed class PixelTextNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Outputs =
    [
        new("图像", GraphPortType.Image, "image"),
        new("遮罩", GraphPortType.Mask, "mask")
    ];
    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Text("text", "PIXEL", "文本"),
        NodeParameterDefinition.Color("fontColor", Colors.White, "字体颜色"),
        NodeParameterDefinition.Color("bgColor", Colors.Transparent, "背景颜色"),
        NodeParameterDefinition.Integer("fontSize", 24, 4, 64, 1, "字体大小"),
        NodeParameterDefinition.Boolean("showBg", true, "显示背景"),
        NodeParameterDefinition.Number("border", 0, 0, 16, 0.5, "边距"),
        NodeParameterDefinition.Integer("spacing", 1, -2, 12, 1, "字符间距")
    ];

    public override string TypeName => "Text";
    public override string Category => "Material";
    public override IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var text = GetString(parameters, "text", "PIXEL");
        var fontColor = GetColor(parameters, "fontColor", Colors.White);
        var bgColor = GetColor(parameters, "bgColor", Colors.Transparent);
        var fontSize = Math.Clamp(GetInt(parameters, "fontSize", 24), 4, 64);
        var spacing = Math.Clamp(GetInt(parameters, "spacing", 1), -2, 12);
        var border = Math.Clamp(GetFloat(parameters, "border", 0f), 0f, 16f);
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        using (var drawing = visual.RenderOpen())
        {
            if (GetBool(parameters, "showBg", true))
                drawing.DrawRectangle(new SolidColorBrush(bgColor), null, new Rect(0, 0, size, size));
            var glyphs = text.Select(character => new FormattedText(character.ToString(), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Consolas"), fontSize,
                new SolidColorBrush(fontColor), 1.0)).ToArray();
            var totalWidth = glyphs.Sum(glyph => glyph.WidthIncludingTrailingWhitespace)
                             + Math.Max(0, glyphs.Length - 1) * spacing;
            var x = Math.Max(border, MathF.Round((float)(size - totalWidth) * 0.5f));
            foreach (var glyph in glyphs)
            {
                var y = Math.Max(border, MathF.Round((float)(size - glyph.Height) * 0.5f));
                drawing.DrawText(glyph, new Point(x, y));
                x += (float)glyph.WidthIncludingTrailingWhitespace + spacing;
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bytes = new byte[size * size * 4];
        bitmap.CopyPixels(bytes, size * 4, 0);
        var result = PixelBufferPool.Borrow(size, size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var index = (y * size + x) * 4;
            var alpha = bytes[index + 3] / 255f;
            var divisor = Math.Max(alpha, 0.001f);
            result.SetPixel(x, y, Math.Clamp(bytes[index + 2] / 255f / divisor, 0f, 1f),
                Math.Clamp(bytes[index + 1] / 255f / divisor, 0f, 1f),
                Math.Clamp(bytes[index] / 255f / divisor, 0f, 1f), alpha);
        }
        return result;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var image = Process(inputs, parameters, context);
        return [image, PixelBuffer.CreateMaskView(image)];
    }
}

/// <summary>Visual annotation node; font sizing belongs to the canvas annotation UI, not image processing.</summary>
public sealed class GraphCommentNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = [new("输入", GraphPortType.Any, "input")];
    private static readonly GraphNodePort[] Outputs = [new("输出", GraphPortType.Any, "output")];
    public override string TypeName => "Comment";
    public override string Category => "Tool";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Array.Empty<NodeParameterDefinition>();
    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
        => inputs.Length > 0 && inputs[0] != null ? inputs[0]!.Clone()
            : PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0, 0, 0, 0);
}
