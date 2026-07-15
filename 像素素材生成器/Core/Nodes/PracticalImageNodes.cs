using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.Nodes;

public sealed class AlphaToolsNode : GraphNodeBase
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
    [
        new("Image", GraphPortType.Image, "image", true),
        new("Mask", GraphPortType.Mask, "mask")
    ];
    private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [new GraphNodePort("Image", GraphPortType.Image, "image")];
    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Choice("mode", "applyMask",
            ["applyMask", "luminanceToAlpha", "invertAlpha", "premultiply", "unpremultiply"], "模式"),
        NodeParameterDefinition.Number("amount", 1, 0, 1, 0.01, "强度"),
        NodeParameterDefinition.Boolean("preserveTransparentRgb", true, "保留透明像素 RGB")
    ];

    public override string TypeName => "AlphaTools";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null) return PracticalNodeUtility.Empty(context);
        var mask = inputs.Length > 1 ? inputs[1] : null;
        var mode = GetChoice(parameters, "mode", "applyMask");
        var amount = Math.Clamp(GetFloat(parameters, "amount", 1), 0, 1);
        var preserveTransparentRgb = GetBool(parameters, "preserveTransparentRgb", true);
        var output = PixelBufferPool.Borrow(source.Width, source.Height);

        output.ForEachPixel((x, y) =>
        {
            var (r, g, b, a) = source.GetPixel(x, y);
            var outR = r;
            var outG = g;
            var outB = b;
            var outA = a;
            switch (mode)
            {
                case "luminanceToAlpha":
                    outA = Lerp(a, PracticalNodeUtility.Luminance(r, g, b), amount);
                    break;
                case "invertAlpha":
                    outA = Lerp(a, 1 - a, amount);
                    break;
                case "premultiply":
                    outR = Lerp(r, r * a, amount);
                    outG = Lerp(g, g * a, amount);
                    outB = Lerp(b, b * a, amount);
                    break;
                case "unpremultiply":
                    if (a > 0.0001f)
                    {
                        outR = Lerp(r, Math.Clamp(r / a, 0, 1), amount);
                        outG = Lerp(g, Math.Clamp(g / a, 0, 1), amount);
                        outB = Lerp(b, Math.Clamp(b / a, 0, 1), amount);
                    }
                    break;
                default:
                    var maskValue = mask == null ? 1 : PracticalNodeUtility.SampleMask(mask, x, y, source.Width, source.Height);
                    outA = Lerp(a, a * maskValue, amount);
                    break;
            }

            if (!preserveTransparentRgb && outA <= 0.0001f)
                outR = outG = outB = 0;
            output.SetPixel(x, y, outR, outG, outB, outA);
        });
        return output;
    }
}

public sealed class ColorReplaceNode : GraphNodeBase
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
        [new GraphNodePort("Image", GraphPortType.Image, "image", true)];
    private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [new GraphNodePort("Image", GraphPortType.Image, "image")];
    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Color("sourceColor", Colors.Magenta, "源颜色"),
        NodeParameterDefinition.Color("targetColor", Colors.Cyan, "目标颜色"),
        NodeParameterDefinition.Number("tolerance", 0.15, 0, 1, 0.01, "容差"),
        NodeParameterDefinition.Number("softness", 0.1, 0, 1, 0.01, "柔化"),
        NodeParameterDefinition.Number("amount", 1, 0, 1, 0.01, "强度"),
        NodeParameterDefinition.Boolean("preserveLuminance", false, "保留亮度")
    ];

    public override string TypeName => "ColorReplace";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null) return PracticalNodeUtility.Empty(context);
        var from = GetColor(parameters, "sourceColor", Colors.Magenta);
        var to = GetColor(parameters, "targetColor", Colors.Cyan);
        var tolerance = Math.Clamp(GetFloat(parameters, "tolerance", 0.15f), 0, 1);
        var softness = Math.Max(0.0001f, GetFloat(parameters, "softness", 0.1f));
        var amount = Math.Clamp(GetFloat(parameters, "amount", 1), 0, 1);
        var preserveLuminance = GetBool(parameters, "preserveLuminance", false);
        var fr = from.R / 255f; var fg = from.G / 255f; var fb = from.B / 255f;
        var tr = to.R / 255f; var tg = to.G / 255f; var tb = to.B / 255f;
        var targetLum = Math.Max(0.0001f, PracticalNodeUtility.Luminance(tr, tg, tb));
        var output = PixelBufferPool.Borrow(source.Width, source.Height);

        output.ForEachPixel((x, y) =>
        {
            var (r, g, b, a) = source.GetPixel(x, y);
            var dr = r - fr; var dg = g - fg; var db = b - fb;
            var distance = MathF.Sqrt((dr * dr + dg * dg + db * db) / 3f);
            var match = 1 - SmoothStep(tolerance, tolerance + softness, distance);
            match *= amount;
            var replaceR = tr; var replaceG = tg; var replaceB = tb;
            if (preserveLuminance)
            {
                var scale = PracticalNodeUtility.Luminance(r, g, b) / targetLum;
                replaceR = Math.Clamp(replaceR * scale, 0, 1);
                replaceG = Math.Clamp(replaceG * scale, 0, 1);
                replaceB = Math.Clamp(replaceB * scale, 0, 1);
            }
            output.SetPixel(x, y, Lerp(r, replaceR, match), Lerp(g, replaceG, match),
                Lerp(b, replaceB, match), a);
        });
        return output;
    }
}

public sealed class MaskMorphologyNode : GraphNodeBase
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
        [new GraphNodePort("Mask", GraphPortType.Mask, "mask", true)];
    private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [new GraphNodePort("Mask", GraphPortType.Mask, "mask")];
    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Choice("operation", "dilate", ["dilate", "erode", "open", "close"], "操作"),
        NodeParameterDefinition.Integer("radius", 1, 1, 16, 1, "半径"),
        NodeParameterDefinition.Integer("iterations", 1, 1, 8, 1, "迭代"),
        NodeParameterDefinition.Choice("shape", "diamond", ["diamond", "square"], "形状")
    ];

    public override string TypeName => "MaskMorphology";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null) return PracticalNodeUtility.Empty(context);
        var operation = GetChoice(parameters, "operation", "dilate");
        var radius = Math.Clamp(GetInt(parameters, "radius", 1), 1, 16);
        var iterations = Math.Clamp(GetInt(parameters, "iterations", 1), 1, 8);
        var square = GetChoice(parameters, "shape", "diamond") == "square";
        var values = PracticalNodeUtility.ReadMask(source);

        for (var i = 0; i < iterations; i++)
        {
            values = operation switch
            {
                "erode" => Morph(values, source.Width, source.Height, radius, square, false),
                "open" => Morph(Morph(values, source.Width, source.Height, radius, square, false),
                    source.Width, source.Height, radius, square, true),
                "close" => Morph(Morph(values, source.Width, source.Height, radius, square, true),
                    source.Width, source.Height, radius, square, false),
                _ => Morph(values, source.Width, source.Height, radius, square, true)
            };
        }
        return PracticalNodeUtility.WriteMask(values, source.Width, source.Height);
    }

    private static float[] Morph(float[] source, int width, int height, int radius, bool square, bool dilate)
    {
        var result = new float[source.Length];
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var value = dilate ? 0f : 1f;
                for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (!square && Math.Abs(dx) + Math.Abs(dy) > radius) continue;
                    var sx = Math.Clamp(x + dx, 0, width - 1);
                    var sy = Math.Clamp(y + dy, 0, height - 1);
                    value = dilate ? Math.Max(value, source[sy * width + sx]) : Math.Min(value, source[sy * width + sx]);
                }
                result[y * width + x] = value;
            }
        });
        return result;
    }
}

public sealed class DistanceFieldNode : GraphNodeBase
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
        [new GraphNodePort("Mask", GraphPortType.Mask, "mask", true)];
    private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [new GraphNodePort("Distance", GraphPortType.Mask, "distance")];
    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Choice("mode", "signed", ["signed", "inside", "outside"], "模式"),
        NodeParameterDefinition.Number("threshold", 0.5, 0, 1, 0.01, "阈值"),
        NodeParameterDefinition.Number("maxDistance", 8, 1, 64, 1, "最大距离"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    ];

    public override string TypeName => "DistanceField";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null) return PracticalNodeUtility.Empty(context);
        var threshold = Math.Clamp(GetFloat(parameters, "threshold", 0.5f), 0, 1);
        var maxDistance = Math.Max(1, GetFloat(parameters, "maxDistance", 8));
        var mode = GetChoice(parameters, "mode", "signed");
        var invert = GetBool(parameters, "invert", false);
        var inside = new bool[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            inside[y * source.Width + x] = PracticalNodeUtility.MaskValue(source, x, y) >= threshold;

        var toInside = DistanceTransform(inside, source.Width, source.Height, true);
        var toOutside = DistanceTransform(inside, source.Width, source.Height, false);
        var values = new float[inside.Length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = mode switch
            {
                "inside" => inside[i] ? Math.Clamp(toOutside[i] / maxDistance, 0, 1) : 0,
                "outside" => inside[i] ? 0 : Math.Clamp(toInside[i] / maxDistance, 0, 1),
                _ => Math.Clamp(0.5f + (inside[i] ? toOutside[i] : -toInside[i]) / (2 * maxDistance), 0, 1)
            };
            if (invert) values[i] = 1 - values[i];
        }
        return PracticalNodeUtility.WriteMask(values, source.Width, source.Height);
    }

    private static float[] DistanceTransform(bool[] inside, int width, int height, bool targetInside)
    {
        const float diagonal = 1.41421356f;
        var distances = new float[inside.Length];
        for (var i = 0; i < distances.Length; i++) distances[i] = inside[i] == targetInside ? 0 : 1_000_000;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (x > 0) distances[index] = Math.Min(distances[index], distances[index - 1] + 1);
            if (y > 0) distances[index] = Math.Min(distances[index], distances[index - width] + 1);
            if (x > 0 && y > 0) distances[index] = Math.Min(distances[index], distances[index - width - 1] + diagonal);
            if (x + 1 < width && y > 0) distances[index] = Math.Min(distances[index], distances[index - width + 1] + diagonal);
        }
        for (var y = height - 1; y >= 0; y--)
        for (var x = width - 1; x >= 0; x--)
        {
            var index = y * width + x;
            if (x + 1 < width) distances[index] = Math.Min(distances[index], distances[index + 1] + 1);
            if (y + 1 < height) distances[index] = Math.Min(distances[index], distances[index + width] + 1);
            if (x + 1 < width && y + 1 < height) distances[index] = Math.Min(distances[index], distances[index + width + 1] + diagonal);
            if (x > 0 && y + 1 < height) distances[index] = Math.Min(distances[index], distances[index + width - 1] + diagonal);
        }
        return distances;
    }
}

public sealed class SpriteExtrudeNode : GraphNodeBase
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
        [new GraphNodePort("Image", GraphPortType.Image, "image", true)];
    private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [new GraphNodePort("Image", GraphPortType.Image, "image")];
    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Integer("radius", 2, 1, 16, 1, "挤出半径"),
        NodeParameterDefinition.Number("alphaThreshold", 0.01, 0, 1, 0.01, "Alpha 阈值"),
        NodeParameterDefinition.Boolean("extendAlpha", false, "扩展 Alpha")
    ];

    public override string TypeName => "SpriteExtrude";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null) return PracticalNodeUtility.Empty(context);
        var radius = Math.Clamp(GetInt(parameters, "radius", 2), 1, 16);
        var threshold = Math.Clamp(GetFloat(parameters, "alphaThreshold", 0.01f), 0, 1);
        var extendAlpha = GetBool(parameters, "extendAlpha", false);
        var output = source.Clone();

        output.ForEachPixel((x, y) =>
        {
            var (r, g, b, a) = source.GetPixel(x, y);
            if (a > threshold) return;
            var bestDistance = int.MaxValue;
            (float R, float G, float B, float A) nearest = default;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = dx * dx + dy * dy;
                if (distance == 0 || distance > radius * radius || distance >= bestDistance) continue;
                var sx = x + dx; var sy = y + dy;
                if (sx < 0 || sx >= source.Width || sy < 0 || sy >= source.Height) continue;
                var sample = source.GetPixel(sx, sy);
                if (sample.A <= threshold) continue;
                bestDistance = distance;
                nearest = sample;
            }
            if (bestDistance != int.MaxValue)
                output.SetPixel(x, y, nearest.R, nearest.G, nearest.B, extendAlpha ? nearest.A : a);
        });
        return output;
    }
}

internal static class PracticalNodeUtility
{
    public static PixelBuffer Empty(PixelGraphContext context) =>
        PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0, 0, 0, 0);

    public static float Luminance(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    public static float MaskValue(PixelBuffer mask, int x, int y)
    {
        var (r, g, b, a) = mask.GetPixel(x, y);
        return a < 0.999f ? a : Luminance(r, g, b);
    }

    public static float SampleMask(PixelBuffer mask, int x, int y, int targetWidth, int targetHeight)
    {
        var sx = Math.Clamp(x * mask.Width / Math.Max(1, targetWidth), 0, mask.Width - 1);
        var sy = Math.Clamp(y * mask.Height / Math.Max(1, targetHeight), 0, mask.Height - 1);
        return MaskValue(mask, sx, sy);
    }

    public static float[] ReadMask(PixelBuffer mask)
    {
        var values = new float[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
            values[y * mask.Width + x] = MaskValue(mask, x, y);
        return values;
    }

    public static PixelBuffer WriteMask(float[] values, int width, int height)
    {
        var output = PixelBufferPool.Borrow(width, height);
        output.ForEachPixel((x, y) =>
        {
            var value = Math.Clamp(values[y * width + x], 0, 1);
            output.SetPixel(x, y, value, value, value, 1);
        });
        return output;
    }
}
