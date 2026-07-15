using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

public sealed class PixelateNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("图像", GraphPortType.Image, "image", IsRequired: true) };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Integer("blockSize", 3, 1, 16, 1, "像素块大小"),
        NodeParameterDefinition.Integer("paletteSteps", 0, 0, 32, 1, "调色板颜色数"),
        NodeParameterDefinition.Choice("sampleMode", "center", ["center", "average", "nearest"], ["中心", "平均", "最近邻"], "采样方式"),
        NodeParameterDefinition.Choice("ditherMode", "none", ["none", "ordered4x4", "floydSteinberg"], ["关闭", "有序4×4", "误差扩散"], "抖色方式"),
        NodeParameterDefinition.Number("outlineStrength", 0, 0, 1, 0.01, "块面描边"),
        NodeParameterDefinition.Color("outlineColor", Color.FromRgb(10, 20, 40), "描边颜色")
    };

    public override string TypeName => "Pixelate";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0f, 0f, 0f, 0f);

        var blockSize = Math.Clamp(GetInt(parameters, "blockSize", 3), 1, 16);
        var sampleMode = GetChoice(parameters, "sampleMode", "center");
        var blocked = PixelBufferPool.Borrow(source.Width, source.Height);

        for (var top = 0; top < source.Height; top += blockSize)
        {
            for (var left = 0; left < source.Width; left += blockSize)
            {
                var right = Math.Min(source.Width, left + blockSize);
                var bottom = Math.Min(source.Height, top + blockSize);
                float r;
                float g;
                float b;
                float a;

                if (string.Equals(sampleMode, "average", StringComparison.OrdinalIgnoreCase))
                {
                    r = g = b = a = 0f;
                    var count = 0;
                    for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                    {
                        var pixel = source.GetPixel(x, y);
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                        a += pixel.A;
                        count++;
                    }
                    var inverse = 1f / Math.Max(1, count);
                    r *= inverse;
                    g *= inverse;
                    b *= inverse;
                    a *= inverse;
                }
                else
                {
                    var sampleX = string.Equals(sampleMode, "nearest", StringComparison.OrdinalIgnoreCase)
                        ? left
                        : Math.Min(source.Width - 1, left + blockSize / 2);
                    var sampleY = string.Equals(sampleMode, "nearest", StringComparison.OrdinalIgnoreCase)
                        ? top
                        : Math.Min(source.Height - 1, top + blockSize / 2);
                    (r, g, b, a) = source.GetPixel(sampleX, sampleY);
                }

                for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                    blocked.SetPixel(x, y, r, g, b, a);
            }
        }

        PixelBuffer result = blocked;
        var paletteSteps = Math.Clamp(GetInt(parameters, "paletteSteps", 0), 0, 32);
        if (paletteSteps >= 2)
        {
            var dither = GetChoice(parameters, "ditherMode", "none");
            result = PixelArtKernel.Stylize(blocked, new PixelArtStyleProfile(
                paletteSteps, 1, 1.06f, 1.03f,
                string.Equals(dither, "none", StringComparison.OrdinalIgnoreCase) ? 0f : 0.45f));
            blocked.Dispose();
        }

        var outlineStrength = Math.Clamp(GetFloat(parameters, "outlineStrength", 0f), 0f, 1f);
        if (outlineStrength <= 0f)
            return result;

        var outline = GetColor(parameters, "outlineColor", Color.FromRgb(10, 20, 40));
        var edged = result.Clone();
        var threshold = 0.42f - outlineStrength * 0.3f;
        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var current = result.GetPixel(x, y);
            var left = result.GetPixelWrapped(x - 1, y);
            var top = result.GetPixelWrapped(x, y - 1);
            if (PixelProcessUtility.ColorDifference(current, left) > threshold
                || PixelProcessUtility.ColorDifference(current, top) > threshold)
            {
                PixelProcessUtility.Set(edged, x, y, outline, current.A);
            }
        }
        result.Dispose();
        return edged;
    }
}

public sealed class ColorQuantizeNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("输入", GraphPortType.Image, "input", IsRequired: true) };
    private static readonly GraphNodePort[] Outputs = { new("输出", GraphPortType.Image, "output") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Integer("numColors", 16, 2, 256, 1, "颜色数量"),
        NodeParameterDefinition.Choice("ditherMode", "floydSteinberg",
            ["none", "bayer4x4", "bayer8x8", "floydSteinberg", "atkinson"],
            ["关闭", "Bayer 4×4", "Bayer 8×8", "误差扩散", "Atkinson"], "抖色方式"),
        NodeParameterDefinition.Choice("colorSpace", "RGB", ["RGB", "HSVWeighted"], ["感知RGB", "HSV加权"], "颜色空间"),
        NodeParameterDefinition.Boolean("preserveAlpha", true, "保留透明度")
    };

    public override string TypeName => "ColorQuantize";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0f, 0f, 0f, 0f);

        // A 32 px asset becomes visually incoherent above roughly 32 colors.  Keep the
        // legacy range for project compatibility, but enforce the pixel-art ceiling.
        var colors = Math.Clamp(GetInt(parameters, "numColors", 16), 2, 32);
        var ditherMode = GetChoice(parameters, "ditherMode", "none");
        var colorSpace = GetChoice(parameters, "colorSpace", "RGB");
        return PixelArtKernel.Stylize(source, new PixelArtStyleProfile(
            colors,
            MinimumClusterSize: 1,
            Contrast: 1.04f,
            Saturation: 1.02f,
            DitherStrength: string.Equals(ditherMode, "none", StringComparison.OrdinalIgnoreCase) ? 0f : 0.42f,
            PreserveAlpha: GetBool(parameters, "preserveAlpha", true),
            HsvWeighted: string.Equals(colorSpace, "HSVWeighted", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class PixelOutlineNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("图像", GraphPortType.Image, "image", IsRequired: true) };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Integer("thickness", 2, 1, 8, 1, "粗细"),
        NodeParameterDefinition.Number("threshold", 0.3, 0, 1, 0.01, "阈值"),
        NodeParameterDefinition.Color("lineColor", Color.FromRgb(10, 10, 30), "线条颜色"),
        NodeParameterDefinition.Number("softness", 0, 0, 1, 0.01, "阶梯衰减"),
        NodeParameterDefinition.Choice("mode", "outside", ["outside", "inside", "both", "glow"], ["外描边", "内描边", "双侧", "像素光晕"], "模式"),
        NodeParameterDefinition.Choice("detectMode", "alpha", ["alpha", "luminance", "color"], ["透明度", "明度", "颜色"], "检测方式")
    };

    public override string TypeName => "Outline";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0f, 0f, 0f, 0f);

        var thickness = Math.Clamp(GetInt(parameters, "thickness", 2), 1, 8);
        var threshold = Math.Clamp(GetFloat(parameters, "threshold", 0.3f), 0f, 1f);
        var softness = Math.Clamp(GetFloat(parameters, "softness", 0f), 0f, 1f);
        var mode = GetChoice(parameters, "mode", "outside");
        var detectMode = GetChoice(parameters, "detectMode", "alpha");
        var lineColor = GetColor(parameters, "lineColor", Color.FromRgb(10, 10, 30));
        var result = source.Clone();

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var current = source.GetPixel(x, y);
            var currentInside = PixelProcessUtility.Signal(current, detectMode) >= threshold;
            var nearestEdge = int.MaxValue;

            for (var radius = 1; radius <= thickness && nearestEdge == int.MaxValue; radius++)
            {
                for (var dy = -radius; dy <= radius && nearestEdge == int.MaxValue; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) != radius)
                        continue;
                    var neighbour = source.GetPixelWrapped(x + dx, y + dy);
                    var differs = string.Equals(detectMode, "color", StringComparison.OrdinalIgnoreCase)
                        ? PixelProcessUtility.ColorDifference(current, neighbour) >= threshold
                        : (PixelProcessUtility.Signal(neighbour, detectMode) >= threshold) != currentInside;
                    if (differs)
                    {
                        nearestEdge = radius;
                        break;
                    }
                }
            }

            if (nearestEdge == int.MaxValue)
                continue;

            var drawOutside = !currentInside && (mode == "outside" || mode == "both" || mode == "glow");
            var drawInside = currentInside && (mode == "inside" || mode == "both");
            if (!drawOutside && !drawInside)
                continue;

            var ring = softness > 0f && nearestEdge == thickness ? 0.5f : 1f;
            if (mode == "glow") ring *= nearestEdge == 1 ? 0.9f : 0.45f;
            if (drawOutside)
            {
                PixelProcessUtility.Set(result, x, y, lineColor, ring);
            }
            else
            {
                var mixed = PixelProcessUtility.Mix(current, lineColor, ring);
                result.SetPixel(x, y, mixed.R, mixed.G, mixed.B, current.A);
            }
        }

        return result;
    }
}

public sealed class PixelLightingNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("漫反射", GraphPortType.Image, "diffuse", IsRequired: true),
        new("法线图", GraphPortType.Image, "normalMap"),
        new("高度图", GraphPortType.Image, "heightMap")
    };
    private static readonly GraphNodePort[] Outputs = { new("输出", GraphPortType.Image, "output") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("lightType", "directional", ["directional", "point", "hemisphere"], ["方向光", "点光", "半球光"], "光源类型"),
        NodeParameterDefinition.Number("lightAngleX", -0.5, -1, 1, 0.01, "光线X"),
        NodeParameterDefinition.Number("lightAngleY", -0.5, -1, 1, 0.01, "光线Y"),
        NodeParameterDefinition.Number("lightAngleZ", 1, -1, 1, 0.01, "光线Z"),
        NodeParameterDefinition.Color("lightColor", Color.FromRgb(255, 240, 220), "光源颜色"),
        NodeParameterDefinition.Number("ambient", 0.3, 0, 1, 0.01, "环境光"),
        NodeParameterDefinition.Number("specIntensity", 0.5, 0, 2, 0.01, "高光强度"),
        NodeParameterDefinition.Number("specPower", 8, 1, 64, 1, "高光硬度"),
        NodeParameterDefinition.Boolean("flipNormalY", false, "翻转法线Y")
    };

    public override string TypeName => "Lighting";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var diffuse = inputs.Length > 0 ? inputs[0] : null;
        if (diffuse == null)
            return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0f, 0f, 0f, 0f);
        var normalMap = inputs.Length > 1 ? inputs[1] : null;
        var heightMap = inputs.Length > 2 ? inputs[2] : null;
        var type = GetChoice(parameters, "lightType", "directional");
        var lx = GetFloat(parameters, "lightAngleX", -0.5f);
        var ly = GetFloat(parameters, "lightAngleY", -0.5f);
        var lz = GetFloat(parameters, "lightAngleZ", 1f);
        PixelProcessUtility.Normalize(ref lx, ref ly, ref lz);
        var ambient = Math.Clamp(GetFloat(parameters, "ambient", 0.3f), 0f, 1f);
        var specIntensity = Math.Clamp(GetFloat(parameters, "specIntensity", 0.5f), 0f, 2f);
        var specPower = Math.Clamp(GetFloat(parameters, "specPower", 8f), 1f, 64f);
        var flipY = GetBool(parameters, "flipNormalY", false);
        var lightColor = GetColor(parameters, "lightColor", Color.FromRgb(255, 240, 220));
        var continuous = PixelBufferPool.Borrow(diffuse.Width, diffuse.Height);

        for (var y = 0; y < diffuse.Height; y++)
        for (var x = 0; x < diffuse.Width; x++)
        {
            float nx;
            float ny;
            float nz;
            if (normalMap != null)
            {
                var normal = normalMap.GetPixelWrapped(x, y);
                nx = normal.R * 2f - 1f;
                ny = (normal.G * 2f - 1f) * (flipY ? -1f : 1f);
                nz = normal.B * 2f - 1f;
            }
            else
            {
                var height = heightMap ?? diffuse;
                nx = -(PixelProcessUtility.Luminance(height.GetPixelWrapped(x + 1, y))
                       - PixelProcessUtility.Luminance(height.GetPixelWrapped(x - 1, y))) * 1.6f;
                ny = -(PixelProcessUtility.Luminance(height.GetPixelWrapped(x, y + 1))
                       - PixelProcessUtility.Luminance(height.GetPixelWrapped(x, y - 1))) * 1.6f;
                if (flipY) ny = -ny;
                nz = 1f;
            }
            PixelProcessUtility.Normalize(ref nx, ref ny, ref nz);

            var localLx = lx;
            var localLy = ly;
            var localLz = lz;
            if (type == "point")
            {
                localLx = lx - (x / (float)Math.Max(1, diffuse.Width - 1) * 2f - 1f);
                localLy = ly - (y / (float)Math.Max(1, diffuse.Height - 1) * 2f - 1f);
                localLz = Math.Max(0.2f, lz + 1f);
                PixelProcessUtility.Normalize(ref localLx, ref localLy, ref localLz);
            }

            var diffuseLight = type == "hemisphere"
                ? ny * 0.5f + 0.5f
                : Math.Max(0f, nx * localLx + ny * localLy + nz * localLz);
            var specular = MathF.Pow(Math.Max(0f, nz * localLz), specPower) * specIntensity;
            var light = Math.Clamp(ambient + diffuseLight * (1f - ambient) + specular, 0f, 1.35f);
            light = MathF.Round(light * 4f) / 4f;

            var source = diffuse.GetPixel(x, y);
            var tint = Math.Clamp((light - 0.75f) * 0.35f, 0f, 0.25f);
            var r = Math.Clamp(source.R * light + lightColor.R / 255f * tint, 0f, 1f);
            var g = Math.Clamp(source.G * light + lightColor.G / 255f * tint, 0f, 1f);
            var b = Math.Clamp(source.B * light + lightColor.B / 255f * tint, 0f, 1f);
            continuous.SetPixel(x, y, r, g, b, source.A);
        }

        var result = PixelArtKernel.Stylize(continuous,
            new PixelArtStyleProfile(12, 1, 1.05f, 1.02f, 0f));
        continuous.Dispose();
        return result;
    }
}

internal static class PixelProcessUtility
{
    public static void Set(PixelBuffer buffer, int x, int y, Color color, float alpha)
        => buffer.SetPixel(x, y, color.R / 255f, color.G / 255f, color.B / 255f, Math.Clamp(alpha, 0f, 1f));

    public static float Luminance((float R, float G, float B, float A) color)
        => color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;

    public static float Signal((float R, float G, float B, float A) color, string mode)
        => string.Equals(mode, "alpha", StringComparison.OrdinalIgnoreCase)
            ? color.A
            : Luminance(color);

    public static float ColorDifference((float R, float G, float B, float A) left,
        (float R, float G, float B, float A) right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        var da = left.A - right.A;
        return MathF.Sqrt(dr * dr * 0.25f + dg * dg * 0.5f + db * db * 0.2f + da * da * 0.05f);
    }

    public static (float R, float G, float B, float A) Mix(
        (float R, float G, float B, float A) source, Color target, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return (
            source.R + (target.R / 255f - source.R) * amount,
            source.G + (target.G / 255f - source.G) * amount,
            source.B + (target.B / 255f - source.B) * amount,
            source.A);
    }

    public static void Normalize(ref float x, ref float y, ref float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length <= 0.0001f)
        {
            x = 0f;
            y = 0f;
            z = 1f;
            return;
        }
        x /= length;
        y /= length;
        z /= length;
    }
}
