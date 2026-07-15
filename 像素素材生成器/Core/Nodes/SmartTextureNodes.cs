using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>
/// Adds clustered, pixel-art friendly surface ageing while preserving the source
/// structure and luminance. The second output is the generated effect mask.
/// </summary>
public sealed class SmartMaterialWeatheringNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("图像", GraphPortType.Image, "image", true),
        new("限制遮罩", GraphPortType.Mask, "mask")
    };

    private static readonly GraphNodePort[] Outputs =
    {
        new("图像", GraphPortType.Image, "image"),
        new("效果遮罩", GraphPortType.Mask, "effectMask")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("effect", "moss",
            ["moss", "corrosion", "damp", "frost", "soot", "dust"],
            ["苔藓生长", "腐蚀锈蚀", "潮湿水渍", "冰霜覆盖", "烟熏焦黑", "积尘风化"], "材质效果"),
        NodeParameterDefinition.Number("amount", 0.58, 0, 1, 0.01, "覆盖量"),
        NodeParameterDefinition.Number("clusterScale", 0.42, 0.08, 1, 0.01, "斑块大小"),
        NodeParameterDefinition.Number("edgeAffinity", 0.65, 0, 1, 0.01, "缝隙附着"),
        NodeParameterDefinition.Number("directionBias", 0.35, -1, 1, 0.01, "上下偏向"),
        NodeParameterDefinition.Number("colorStrength", 0.86, 0, 1, 0.01, "颜色强度"),
        NodeParameterDefinition.Number("preserveShading", 0.72, 0, 1, 0.01, "保留明暗"),
        NodeParameterDefinition.Choice("palette", "natural",
            ["natural", "dark", "vivid"], ["自然", "阴暗", "鲜明"], "色彩风格"),
        NodeParameterDefinition.Boolean("pixelClusters", true, "像素色阶"),
        NodeParameterDefinition.Boolean("seamless", true, "无缝平铺"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "SmartMaterialWeathering";
    public override string Category => "Smart";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        outputs[1].Dispose();
        return outputs[0];
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
        {
            var size = context.GetEffectiveSize();
            return
            [
                PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f),
                PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 1f)
            ];
        }

        var limiter = inputs.Length > 1 ? inputs[1] : null;
        var effect = GetChoice(parameters, "effect", "moss");
        var amount = Math.Clamp(GetFloat(parameters, "amount", 0.58f), 0f, 1f);
        var clusterScale = Math.Clamp(GetFloat(parameters, "clusterScale", 0.42f), 0.08f, 1f);
        var edgeAffinity = Math.Clamp(GetFloat(parameters, "edgeAffinity", 0.65f), 0f, 1f);
        var direction = Math.Clamp(GetFloat(parameters, "directionBias", 0.35f), -1f, 1f);
        var colorStrength = Math.Clamp(GetFloat(parameters, "colorStrength", 0.86f), 0f, 1f);
        var preserveShading = Math.Clamp(GetFloat(parameters, "preserveShading", 0.72f), 0f, 1f);
        var pixelClusters = GetBool(parameters, "pixelClusters", true);
        var seamless = GetBool(parameters, "seamless", true);
        var seed = GetInt(parameters, "seed", context.Seed);
        var paletteName = GetChoice(parameters, "palette", "natural");
        var (primary, secondary, accent) = GetPalette(effect, paletteName);
        var width = source.Width;
        var height = source.Height;
        var result = PixelBufferPool.Borrow(width, height);
        var maskOutput = PixelBufferPool.Borrow(width, height);
        var cells = Math.Clamp((int)MathF.Round(3f + (1f - clusterScale) * 9f), 3, 12);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var original = source.GetPixel(x, y);
            var luminance = Luma(original.R, original.G, original.B);
            var edge = SurfaceEdge(source, x, y, seamless);
            var nx = x * cells / (float)Math.Max(1, width);
            var ny = y * cells / (float)Math.Max(1, height);
            var coarse = TileableFractalNoise(nx, ny, cells, 3, 0.55f, 2f, seed);
            var fine = TileableValueNoise(nx * 2f, ny * 2f, cells * 2, seed + 977);
            var fleck = HashToUnit(Mod(x, width), Mod(y, height), seed + 1877);
            var vertical = height <= 1 ? 0.5f : y / (float)(height - 1);
            var directional = direction >= 0f ? vertical * direction : (1f - vertical) * -direction;

            var potential = effect switch
            {
                "corrosion" => coarse * 0.38f + fine * 0.16f + edge * (0.16f + edgeAffinity * 0.22f)
                               + (1f - luminance) * 0.15f + fleck * 0.08f,
                "damp" => coarse * 0.53f + (1f - luminance) * 0.18f + edge * edgeAffinity * 0.24f
                          + directional * 0.28f,
                "frost" => coarse * 0.48f + fine * 0.17f + luminance * 0.16f + edge * edgeAffinity * 0.25f
                           + directional * 0.16f,
                "soot" => coarse * 0.52f + (1f - luminance) * 0.15f + edge * edgeAffinity * 0.12f
                          + directional * 0.34f,
                "dust" => coarse * 0.48f + fine * 0.17f + luminance * 0.12f + directional * 0.30f,
                _ => coarse * 0.50f + fine * 0.12f + (1f - luminance) * 0.12f
                     + edge * (0.18f + edgeAffinity * 0.30f) + directional * 0.23f
            };

            // Coverage is intentionally conservative: small connected clusters read
            // as material ageing, while broad noise quickly destroys 32 px forms.
            var threshold = 0.94f - amount * 0.42f + (effect == "corrosion" ? -0.05f : 0f);
            var mask = SmoothStep(threshold, threshold + 0.20f, potential);
            if (effect == "corrosion" && fleck > 0.91f - amount * 0.12f)
                mask = MathF.Max(mask, (fleck - 0.78f) * 3.2f);
            if (limiter != null)
            {
                var limited = limiter.GetPixel(
                    Math.Min(limiter.Width - 1, x * limiter.Width / width),
                    Math.Min(limiter.Height - 1, y * limiter.Height / height));
                mask *= Math.Clamp(MathF.Max(limited.A, Luma(limited.R, limited.G, limited.B)), 0f, 1f);
            }
            if (original.A <= 0.001f) mask = 0f;
            if (pixelClusters) mask = MathF.Round(Math.Clamp(mask, 0f, 1f) * 4f) / 4f;

            var paletteMix = SmoothStep(0.34f, 0.72f, fine);
            var weatherR = Lerp(primary.R, secondary.R, paletteMix);
            var weatherG = Lerp(primary.G, secondary.G, paletteMix);
            var weatherB = Lerp(primary.B, secondary.B, paletteMix);
            if (mask > 0.72f && fleck > 0.82f)
            {
                weatherR = Lerp(weatherR, accent.R, 0.55f);
                weatherG = Lerp(weatherG, accent.G, 0.55f);
                weatherB = Lerp(weatherB, accent.B, 0.55f);
            }

            var shade = Lerp(1f, 0.48f + luminance * 0.74f, preserveShading);
            weatherR *= shade;
            weatherG *= shade;
            weatherB *= shade;
            var blend = Math.Clamp(mask * colorStrength, 0f, 1f);
            result.SetPixel(x, y,
                Math.Clamp(Lerp(original.R, weatherR, blend), 0f, 1f),
                Math.Clamp(Lerp(original.G, weatherG, blend), 0f, 1f),
                Math.Clamp(Lerp(original.B, weatherB, blend), 0f, 1f), original.A);
            maskOutput.SetPixel(x, y, mask, mask, mask, 1f);
        }

        return [result, maskOutput];
    }

    private static float SurfaceEdge(PixelBuffer source, int x, int y, bool seamless)
    {
        var center = source.GetPixel(x, y);
        var centerLuma = Luma(center.R, center.G, center.B);
        var total = 0f;
        foreach (var (dx, dy) in Neighbours)
        {
            var sx = seamless ? Mod(x + dx, source.Width) : Math.Clamp(x + dx, 0, source.Width - 1);
            var sy = seamless ? Mod(y + dy, source.Height) : Math.Clamp(y + dy, 0, source.Height - 1);
            var sample = source.GetPixel(sx, sy);
            total += MathF.Abs(centerLuma - Luma(sample.R, sample.G, sample.B));
        }
        return Math.Clamp(total * 1.8f, 0f, 1f);
    }

    private static readonly (int X, int Y)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static ((float R, float G, float B) Primary,
        (float R, float G, float B) Secondary, (float R, float G, float B) Accent)
        GetPalette(string effect, string style)
    {
        var palette = effect switch
        {
            "corrosion" => (Rgb(91, 54, 35), Rgb(166, 77, 33), Rgb(213, 125, 46)),
            "damp" => (Rgb(34, 54, 48), Rgb(50, 79, 68), Rgb(89, 112, 78)),
            "frost" => (Rgb(153, 205, 226), Rgb(220, 241, 244), Rgb(255, 255, 247)),
            "soot" => (Rgb(24, 23, 27), Rgb(52, 43, 39), Rgb(92, 65, 42)),
            "dust" => (Rgb(132, 104, 71), Rgb(188, 155, 103), Rgb(221, 193, 136)),
            _ => (Rgb(35, 82, 39), Rgb(76, 130, 54), Rgb(151, 174, 74))
        };
        if (style == "dark")
            return (Scale(palette.Item1, 0.68f), Scale(palette.Item2, 0.72f), Scale(palette.Item3, 0.78f));
        if (style == "vivid")
            return (Scale(palette.Item1, 1.10f), Scale(palette.Item2, 1.18f), Scale(palette.Item3, 1.20f));
        return palette;
    }

    private static (float R, float G, float B) Rgb(byte r, byte g, byte b) => (r / 255f, g / 255f, b / 255f);
    private static (float R, float G, float B) Scale((float R, float G, float B) value, float scale)
        => (Math.Clamp(value.R * scale, 0f, 1f), Math.Clamp(value.G * scale, 0f, 1f), Math.Clamp(value.B * scale, 0f, 1f));
    private static float Luma(float r, float g, float b) => r * 0.2126f + g * 0.7152f + b * 0.0722f;
}

/// <summary>Builds connected crack networks, chipped edges and optional holes.</summary>
public sealed class SmartCrackDamageNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs = { new("图像", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs =
    {
        new("图像", GraphPortType.Image, "image"), new("破损遮罩", GraphPortType.Mask, "damageMask")
    };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("material", "brick",
            ["brick", "stone", "plaster", "wood", "metal"],
            ["砖墙", "石材", "灰泥", "木材", "金属"], "材质类型"),
        NodeParameterDefinition.Number("damage", 0.58, 0, 1, 0.01, "破损程度"),
        NodeParameterDefinition.Number("crackWidth", 0.38, 0.05, 1, 0.01, "裂纹宽度"),
        NodeParameterDefinition.Number("chips", 0.42, 0, 1, 0.01, "崩边碎块"),
        NodeParameterDefinition.Number("depth", 0.72, 0, 1, 0.01, "凹陷深度"),
        NodeParameterDefinition.Integer("networkScale", 6, 3, 14, 1, "裂纹密度"),
        NodeParameterDefinition.Boolean("breakThrough", false, "局部击穿透明"),
        NodeParameterDefinition.Boolean("seamless", true, "无缝平铺"),
        NodeParameterDefinition.Seed("seed", 87, 0, 99999, "随机种子")
    };

    public override string TypeName => "SmartCrackDamage";
    public override string Category => "Smart";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        outputs[1].Dispose();
        return outputs[0];
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
        {
            var size = context.GetEffectiveSize();
            return [PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f), PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 1f)];
        }

        var width = source.Width;
        var height = source.Height;
        var damage = Math.Clamp(GetFloat(parameters, "damage", 0.58f), 0f, 1f);
        var crackWidth = Math.Clamp(GetFloat(parameters, "crackWidth", 0.38f), 0.05f, 1f);
        var chips = Math.Clamp(GetFloat(parameters, "chips", 0.42f), 0f, 1f);
        var depth = Math.Clamp(GetFloat(parameters, "depth", 0.72f), 0f, 1f);
        var cellCount = Math.Clamp(GetInt(parameters, "networkScale", 6), 3, 14);
        var seed = GetInt(parameters, "seed", 87);
        var breakThrough = GetBool(parameters, "breakThrough", false);
        var material = GetChoice(parameters, "material", "brick");
        var damageMask = new float[width * height];
        var crackPixelWidth = 0.45f + crackWidth * 0.80f;
        var branchOneY = height * (0.30f + HashToUnit(seed, 17, 4129) * 0.14f);
        var branchTwoY = height * (0.62f + HashToUnit(seed, 23, 4133) * 0.14f);
        var branchOneX = MainCrackX(branchOneY, width, height, cellCount, seed);
        var branchTwoX = MainCrackX(branchTwoY, width, height, cellCount, seed);
        var branchOneEndX = branchOneX + width * (HashToUnit(seed, 31, 4153) > 0.5f ? 0.32f : -0.32f);
        var branchTwoEndX = branchTwoX + width * (HashToUnit(seed, 37, 4157) > 0.5f ? 0.28f : -0.28f);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var nx = x * cellCount / (float)Math.Max(1, width);
            var ny = y * cellCount / (float)Math.Max(1, height);
            TileableVoronoi(nx, ny, 1, cellCount, seed, out var nearest, out var second, out _, out _);
            var gap = second - nearest;
            var boundary = 1f - SmoothStep(0.025f + crackWidth * 0.035f, 0.075f + crackWidth * 0.095f, gap);
            var region = TileableFractalNoise(nx * 0.55f, ny * 0.55f, cellCount, 2, 0.6f, 2f, seed + 313);
            var active = SmoothStep(0.78f - damage * 0.55f, 0.91f - damage * 0.20f, region);
            var chipNoise = TileableValueNoise(nx * 2f, ny * 2f, cellCount * 2, seed + 701);
            var chipMask = SmoothStep(0.82f - chips * 0.30f, 0.96f - chips * 0.12f, chipNoise)
                           * SmoothStep(0.12f, 0.72f, boundary + active * 0.25f);
            var mainDistance = MathF.Abs(x - MainCrackX(y, width, height, cellCount, seed));
            var mainCrack = 1f - SmoothStep(crackPixelWidth, crackPixelWidth + 1.05f, mainDistance);
            var branchOne = 1f - SmoothStep(crackPixelWidth * 0.72f, crackPixelWidth * 0.72f + 0.85f,
                DistanceToSegment(x, y, branchOneX, branchOneY, branchOneEndX, branchOneY - height * 0.19f));
            var branchTwo = 1f - SmoothStep(crackPixelWidth * 0.68f, crackPixelWidth * 0.68f + 0.82f,
                DistanceToSegment(x, y, branchTwoX, branchTwoY, branchTwoEndX, branchTwoY + height * 0.17f));
            var authoredNetwork = MathF.Max(mainCrack, MathF.Max(branchOne, branchTwo)) * (0.52f + damage * 0.48f);
            damageMask[y * width + x] = Math.Clamp(
                MathF.Max(authoredNetwork, MathF.Max(boundary * active, chipMask * chips)), 0f, 1f);
        }

        var result = PixelBufferPool.Borrow(width, height);
        var maskOutput = PixelBufferPool.Borrow(width, height);
        var crackTint = material switch
        {
            "metal" => (0.18f, 0.12f, 0.09f),
            "wood" => (0.13f, 0.075f, 0.035f),
            "plaster" => (0.20f, 0.18f, 0.16f),
            _ => (0.09f, 0.075f, 0.07f)
        };

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            var mask = MathF.Round(damageMask[index] * 4f) / 4f;
            var original = source.GetPixel(x, y);
            var neighbour = MathF.Max(ReadMask(damageMask, width, height, x + 1, y),
                MathF.Max(ReadMask(damageMask, width, height, x, y + 1),
                    ReadMask(damageMask, width, height, x - 1, y)));
            var rim = mask < 0.25f ? Math.Clamp(neighbour * depth * 0.42f, 0f, 0.32f) : 0f;
            var darken = mask * depth;
            var r = Lerp(original.R, crackTint.Item1, darken);
            var g = Lerp(original.G, crackTint.Item2, darken);
            var b = Lerp(original.B, crackTint.Item3, darken);
            r = Math.Clamp(r + rim, 0f, 1f);
            g = Math.Clamp(g + rim * 0.88f, 0f, 1f);
            b = Math.Clamp(b + rim * 0.65f, 0f, 1f);
            var alpha = breakThrough && mask > 0.74f && HashToUnit(x, y, seed + 1901) > 0.76f
                ? 0f : original.A;
            result.SetPixel(x, y, r, g, b, alpha);
            maskOutput.SetPixel(x, y, mask, mask, mask, 1f);
        }
        return [result, maskOutput];
    }

    private static float ReadMask(float[] mask, int width, int height, int x, int y)
        => mask[Mod(y, height) * width + Mod(x, width)];

    private static float MainCrackX(float y, int width, int height, int cells, int seed)
    {
        var t = y * cells / Math.Max(1f, height);
        var wander = TileableFractalNoise(t, 0.37f, cells, 3, 0.58f, 2f, seed + 1297);
        return width * (0.18f + wander * 0.64f);
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        var vx = bx - ax;
        var vy = by - ay;
        var lengthSquared = vx * vx + vy * vy;
        if (lengthSquared <= 0.0001f)
            return MathF.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / lengthSquared, 0f, 1f);
        var dx = px - (ax + vx * t);
        var dy = py - (ay + vy * t);
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
