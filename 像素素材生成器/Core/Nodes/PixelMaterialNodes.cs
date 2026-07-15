using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.Nodes;

public abstract class PixelMaterialNodeBase : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Outputs =
    {
        new("图像", GraphPortType.Image, "image"),
        new("遮罩", GraphPortType.Mask, "mask")
    };

    public override string Category => "Material";
    public override IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var image = Process(inputs, parameters, context);
        return [image, PixelBuffer.CreateMaskView(image)];
    }
}

public sealed class PixelGrassNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("density", 0.7, 0.1, 1, 0.01, "密度"),
        NodeParameterDefinition.Number("grassLength", 0.5, 0.1, 1, 0.01, "草叶长度"),
        NodeParameterDefinition.Number("grassWidth", 0.4, 0.1, 1, 0.01, "草叶宽度"),
        NodeParameterDefinition.Number("windAngle", 0, 0, 360, 1, "风向角度"),
        NodeParameterDefinition.Number("windStrength", 0.3, 0, 1, 0.01, "风力强度"),
        NodeParameterDefinition.Color("grassColor1", Color.FromRgb(60, 140, 40), "草色1"),
        NodeParameterDefinition.Color("grassColor2", Color.FromRgb(100, 180, 60), "草色2"),
        NodeParameterDefinition.Color("soilColor", Color.FromRgb(120, 90, 60), "土壤颜色"),
        NodeParameterDefinition.Number("colorVariation", 0.2, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("clumping", 0.5, 0, 1, 0.01, "丛生度"),
        NodeParameterDefinition.Number("detail", 0.3, 0, 1, 0.01, "细节"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0.15, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Grass";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var density = Math.Clamp(GetFloat(parameters, "density", 0.7f), 0.1f, 1f);
        var length = Math.Clamp(GetFloat(parameters, "grassLength", 0.5f), 0.1f, 1f);
        var width = Math.Clamp(GetFloat(parameters, "grassWidth", 0.4f), 0.1f, 1f);
        var clumping = Math.Clamp(GetFloat(parameters, "clumping", 0.5f), 0f, 1f);
        var detail = Math.Clamp(GetFloat(parameters, "detail", 0.3f), 0f, 1f);
        var variation = Math.Clamp(GetFloat(parameters, "colorVariation", 0.2f), 0f, 0.5f);
        var brightness = GetFloat(parameters, "brightness", 0f);
        var contrast = GetFloat(parameters, "contrast", 0.15f);
        var invert = GetBool(parameters, "invert", false);
        var windAngle = GetFloat(parameters, "windAngle", 0f) * MathF.PI / 180f;
        var wind = Math.Clamp(GetFloat(parameters, "windStrength", 0.3f), 0f, 1f);

        var grass1 = GetColor(parameters, "grassColor1", Color.FromRgb(60, 140, 40));
        var grass2 = GetColor(parameters, "grassColor2", Color.FromRgb(100, 180, 60));
        var soil = GetColor(parameters, "soilColor", Color.FromRgb(120, 90, 60));
        return PixelRpgTileRenderer.RenderGrass(size, seed, density, length, width,
            clumping, detail, variation, brightness, contrast, invert,
            windAngle, wind, grass1, grass2, soil);
    }
}

public sealed class PixelRockNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("shape", "round", ["round", "sharp", "flat", "rubble"], ["圆润", "尖锐", "扁平", "碎石"], "形状"),
        NodeParameterDefinition.Number("size", 0.6, 0.1, 1, 0.01, "大小"),
        NodeParameterDefinition.Number("roughness", 0.5, 0, 1, 0.01, "粗糙度"),
        NodeParameterDefinition.Integer("colorVariations", 3, 1, 8, 1, "颜色层级"),
        NodeParameterDefinition.Color("mainColor", Color.FromRgb(140, 130, 120), "主颜色"),
        NodeParameterDefinition.Color("highlightColor", Color.FromRgb(180, 170, 155), "高光颜色"),
        NodeParameterDefinition.Color("shadowColor", Color.FromRgb(80, 75, 70), "阴影颜色"),
        NodeParameterDefinition.Number("brightness", 0, -0.5, 0.5, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0, -0.5, 0.5, 0.01, "对比度")
    };

    public override string TypeName => "Rock";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var shape = GetChoice(parameters, "shape", "round");
        var scale = Math.Clamp(GetFloat(parameters, "size", 0.6f), 0.1f, 1f);
        var roughness = Math.Clamp(GetFloat(parameters, "roughness", 0.5f), 0f, 1f);
        var levels = Math.Clamp(GetInt(parameters, "colorVariations", 3), 1, 8);
        var main = GetColor(parameters, "mainColor", Color.FromRgb(140, 130, 120));
        var highlight = GetColor(parameters, "highlightColor", Color.FromRgb(180, 170, 155));
        var shadow = GetColor(parameters, "shadowColor", Color.FromRgb(80, 75, 70));
        var brightness = GetFloat(parameters, "brightness", 0f);
        var contrast = GetFloat(parameters, "contrast", 0f);

        var result = PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
        var radiusX = Math.Max(2f, size * scale * 0.42f);
        var radiusY = shape == "flat" ? radiusX * 0.58f : radiusX * 0.84f;
        var centerX = (size - 1) * 0.5f;
        var centerY = (size - 1) * 0.54f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = (x - centerX) / radiusX;
                var ny = (y - centerY) / radiusY;
                var angle = MathF.Atan2(ny, nx);
                var sector = Mod((int)MathF.Floor((angle + MathF.PI) / MathF.Tau * 16f), 16);
                var jagged = (HashToUnit(sector, seed, 211) - 0.5f) * roughness * 0.24f;
                var distance = shape switch
                {
                    "sharp" => MathF.Abs(nx) + MathF.Abs(ny),
                    "rubble" => MathF.Max(MathF.Abs(nx), MathF.Abs(ny)) * 0.78f + (MathF.Abs(nx) + MathF.Abs(ny)) * 0.22f,
                    _ => MathF.Sqrt(nx * nx + ny * ny)
                };
                var boundary = 1f + jagged;
                if (distance > boundary)
                    continue;

                var edgeWidth = 1.15f / Math.Max(radiusX, radiusY);
                Color color;
                if (distance > boundary - edgeWidth)
                {
                    color = PixelMaterialUtility.Shade(shadow, -0.18f);
                }
                else
                {
                    var lighting = Math.Clamp(0.52f - nx * 0.28f - ny * 0.42f, 0f, 1f);
                    var patchCell = Math.Max(2, size / 10);
                    var patch = HashToUnit(x / patchCell, y / patchCell, seed + 271);
                    lighting = Math.Clamp(lighting + (patch - 0.5f) * roughness * 0.34f, 0f, 1f);
                    var bands = Math.Max(2, levels);
                    lighting = MathF.Round(lighting * (bands - 1)) / (bands - 1);
                    color = lighting < 0.5f
                        ? PixelMaterialUtility.Mix(shadow, main, lighting * 2f)
                        : PixelMaterialUtility.Mix(main, highlight, (lighting - 0.5f) * 2f);
                }

                color = PixelMaterialUtility.Adjust(color, brightness, contrast, false);
                PixelMaterialUtility.Set(result, x, y, color, 1f);
            }
        }

        return result;
    }
}

public sealed class PixelCobblestoneNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("columns", 5, 3, 12, 1, "列数"),
        NodeParameterDefinition.Integer("rows", 5, 3, 12, 1, "行数"),
        NodeParameterDefinition.Number("irregularity", 0.4, 0, 1, 0.01, "不规则度"),
        NodeParameterDefinition.Number("gapWidth", 0.08, 0.01, 0.25, 0.01, "缝隙宽度"),
        NodeParameterDefinition.Number("roundness", 0.65, 0, 1, 0.01, "圆润度"),
        NodeParameterDefinition.Color("stoneColor", Color.FromRgb(160, 150, 140), "石块颜色"),
        NodeParameterDefinition.Color("gapColor", Color.FromRgb(70, 65, 60), "缝隙颜色"),
        NodeParameterDefinition.Number("colorVariation", 0.15, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("highlight", 0.3, 0, 1, 0.01, "高光"),
        NodeParameterDefinition.Number("shadow", 0.25, 0, 1, 0.01, "阴影"),
        NodeParameterDefinition.Number("bumpDepth", 0.3, 0, 1, 0.01, "凹凸深度"),
        NodeParameterDefinition.Number("edgeRoughness", 0.2, 0, 1, 0.01, "边缘粗糙度"),
        NodeParameterDefinition.Number("surfaceDetail", 0.3, 0, 1, 0.01, "表面细节"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Cobblestone";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var columns = Math.Clamp(GetInt(parameters, "columns", 5), 3, 12);
        var rows = Math.Clamp(GetInt(parameters, "rows", 5), 3, 12);
        var irregularity = Math.Clamp(GetFloat(parameters, "irregularity", 0.4f), 0f, 1f);
        var gapWidth = Math.Clamp(GetFloat(parameters, "gapWidth", 0.08f), 0.01f, 0.25f);
        var roundness = Math.Clamp(GetFloat(parameters, "roundness", 0.65f), 0f, 1f);
        var variation = Math.Clamp(GetFloat(parameters, "colorVariation", 0.15f), 0f, 0.5f);
        var highlight = Math.Clamp(GetFloat(parameters, "highlight", 0.3f), 0f, 1f);
        var shadow = Math.Clamp(GetFloat(parameters, "shadow", 0.25f), 0f, 1f);
        var bump = Math.Clamp(GetFloat(parameters, "bumpDepth", 0.3f), 0f, 1f);
        var edgeRoughness = Math.Clamp(GetFloat(parameters, "edgeRoughness", 0.2f), 0f, 1f);
        var surfaceDetail = Math.Clamp(GetFloat(parameters, "surfaceDetail", 0.3f), 0f, 1f);
        var invert = GetBool(parameters, "invert", false);
        var stone = GetColor(parameters, "stoneColor", Color.FromRgb(160, 150, 140));
        var gap = GetColor(parameters, "gapColor", Color.FromRgb(70, 65, 60));
        return PixelRpgTileRenderer.RenderCobblestone(size, seed, columns, rows,
            irregularity, gapWidth, roundness, variation, highlight, shadow,
            bump, edgeRoughness, surfaceDetail, invert, stone, gap);
    }
}

public sealed class PixelWaterFlowNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("scale", 2, 0.5, 10, 0.01, "尺度"),
        NodeParameterDefinition.Number("flowSpeed", 0.5, 0, 1, 0.01, "流速"),
        NodeParameterDefinition.Number("flowAngle", 0, 0, 360, 1, "流动角度"),
        NodeParameterDefinition.Number("waveStrength", 0.5, 0, 1, 0.01, "波浪强度"),
        NodeParameterDefinition.Number("foamAmount", 0.3, 0, 1, 0.01, "泡沫量"),
        NodeParameterDefinition.Color("deepColor", Color.FromRgb(10, 40, 100), "深水颜色"),
        NodeParameterDefinition.Color("shallowColor", Color.FromRgb(50, 120, 200), "浅水颜色"),
        NodeParameterDefinition.Color("foamColor", Color.FromRgb(240, 250, 255), "泡沫颜色"),
        NodeParameterDefinition.Number("rippleStrength", 0.3, 0, 1, 0.01, "涟漪强度"),
        NodeParameterDefinition.Number("opacity", 0.8, 0, 1, 0.01, "不透明度"),
        NodeParameterDefinition.Number("octaves", 3, 1, 6, 1, "层级"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "WaterFlow";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var scale = Math.Clamp(GetFloat(parameters, "scale", 2f), 0.5f, 10f);
        var speed = Math.Clamp(GetFloat(parameters, "flowSpeed", 0.5f), 0f, 1f);
        var direction = GetFloat(parameters, "flowAngle", 0f);
        var wave = Math.Clamp(GetFloat(parameters, "waveStrength", 0.5f), 0f, 1f);
        var foam = Math.Clamp(GetFloat(parameters, "foamAmount", 0.3f), 0f, 1f);
        var ripple = Math.Clamp(GetFloat(parameters, "rippleStrength", 0.3f), 0f, 1f);
        var opacity = Math.Clamp(GetFloat(parameters, "opacity", 0.8f), 0f, 1f);
        var deep = GetColor(parameters, "deepColor", Color.FromRgb(10, 40, 100));
        var shallow = GetColor(parameters, "shallowColor", Color.FromRgb(50, 120, 200));
        var foamColor = GetColor(parameters, "foamColor", Color.FromRgb(240, 250, 255));
        var octaves = Math.Clamp(GetInt(parameters, "octaves", 3), 1, 6);
        var invert = GetBool(parameters, "invert", false);
        var time = context.AnimationTime ?? context.GlobalTime * 0.1f;
        return PixelDirectionalEffectRenderer.RenderWaterFlow(size, seed, scale, speed, direction,
            wave, foam, deep, shallow, foamColor, ripple, opacity, octaves, invert, time);
    }
}

public sealed class PixelLavaFlowNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("scale", 2.5, 0.5, 8, 0.01, "尺度"),
        NodeParameterDefinition.Number("flowSpeed", 0.4, 0, 1, 0.01, "流速"),
        NodeParameterDefinition.Number("flowAngle", 0, 0, 360, 1, "流动角度"),
        NodeParameterDefinition.Number("glowIntensity", 0.7, 0, 1, 0.01, "发光强度"),
        NodeParameterDefinition.Number("veinDensity", 0.5, 0, 1, 0.01, "熔岩脉密度"),
        NodeParameterDefinition.Color("lavaColor", Color.FromRgb(255, 120, 20), "熔岩颜色"),
        NodeParameterDefinition.Color("hotColor", Color.FromRgb(255, 220, 80), "高温颜色"),
        NodeParameterDefinition.Color("crustColor", Color.FromRgb(35, 25, 20), "熔壳颜色"),
        NodeParameterDefinition.Number("emberAmount", 0.3, 0, 1, 0.01, "火星量"),
        NodeParameterDefinition.Number("octaves", 3, 1, 6, 1, "层级"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "LavaFlow";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var scale = Math.Clamp(GetFloat(parameters, "scale", 2.5f), 0.5f, 8f);
        var glow = Math.Clamp(GetFloat(parameters, "glowIntensity", 0.7f), 0f, 1f);
        var veins = Math.Clamp(GetFloat(parameters, "veinDensity", 0.5f), 0f, 1f);
        var embers = Math.Clamp(GetFloat(parameters, "emberAmount", 0.3f), 0f, 1f);
        var lava = GetColor(parameters, "lavaColor", Color.FromRgb(255, 120, 20));
        var hot = GetColor(parameters, "hotColor", Color.FromRgb(255, 220, 80));
        var crust = GetColor(parameters, "crustColor", Color.FromRgb(35, 25, 20));
        var invert = GetBool(parameters, "invert", false);
        var cellSize = Math.Max(3, (int)MathF.Round(size / Math.Max(2f, scale * 1.7f)));
        var period = Math.Max(1, size / cellSize);
        var result = PixelBufferPool.Borrow(size, size);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                TileableVoronoi(x, y, cellSize, period, seed + 601,
                    out var nearest, out var second, out _, out _);
                var edge = second - nearest;
                var cluster = TileableValueNoise(x / 4f, y / 4f, Math.Max(1, size / 4), seed + 617);
                var veinWidth = 0.08f + veins * 0.16f + (cluster - 0.5f) * 0.035f;

                Color color;
                if (edge < veinWidth * 0.42f)
                    color = PixelMaterialUtility.Mix(lava, hot, 0.62f + glow * 0.3f);
                else if (edge < veinWidth)
                    color = PixelMaterialUtility.Mix(crust, lava, 0.58f + glow * 0.22f);
                else
                    color = nearest > 0.58f
                        ? PixelMaterialUtility.Shade(crust, 0.13f)
                        : crust;

                if (embers > 0f && edge >= veinWidth && HashToUnit(x / 2, y / 2, seed + 643) > 1f - embers * 0.08f)
                    color = PixelMaterialUtility.Mix(color, lava, 0.55f);
                PixelMaterialUtility.Set(result, x, y, invert ? PixelMaterialUtility.Invert(color) : color, 1f);
            }
        }

        return result;
    }
}

internal static class PixelMaterialUtility
{
    public static void Set(PixelBuffer buffer, int x, int y, Color color, float alpha)
        => buffer.SetPixel(x, y, color.R / 255f, color.G / 255f, color.B / 255f,
            Math.Clamp(alpha, 0f, 1f));

    public static Color Mix(Color left, Color right, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromRgb(
            (byte)MathF.Round(left.R + (right.R - left.R) * amount),
            (byte)MathF.Round(left.G + (right.G - left.G) * amount),
            (byte)MathF.Round(left.B + (right.B - left.B) * amount));
    }

    public static Color Shade(Color color, float amount)
        => amount >= 0f
            ? Mix(color, Colors.White, amount)
            : Mix(color, Colors.Black, -amount);

    public static Color Invert(Color color)
        => Color.FromRgb((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));

    public static Color Adjust(Color color, float brightness, float contrast, bool invert)
    {
        var factor = 1f + contrast;
        byte Channel(byte value)
        {
            var normalized = ((value / 255f - 0.5f) * factor + 0.5f) + brightness;
            var channel = (byte)MathF.Round(Math.Clamp(normalized, 0f, 1f) * 255f);
            return invert ? (byte)(255 - channel) : channel;
        }

        return Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B));
    }

    public static float TileableDirectionalBands(float phaseX, float phaseY, float direction,
        int harmonicCount, float phaseOffset)
    {
        var wrappedDirection = direction - MathF.Floor(direction);
        var position = wrappedDirection * 4f;
        var sector = Math.Min(3, (int)MathF.Floor(position));
        var blend = GraphNodeBase.SmoothStep(position - sector);
        var harmonics = Math.Max(1, harmonicCount);
        var horizontal = 0.5f + 0.5f * MathF.Sin(phaseX * harmonics + phaseOffset);
        var diagonalDown = 0.5f + 0.5f * MathF.Sin((phaseX + phaseY) * harmonics + phaseOffset);
        var vertical = 0.5f + 0.5f * MathF.Sin(phaseY * harmonics + phaseOffset);
        var diagonalUp = 0.5f + 0.5f * MathF.Sin((phaseX - phaseY) * harmonics + phaseOffset);
        return sector switch
        {
            0 => GraphNodeBase.Lerp(horizontal, diagonalDown, blend),
            1 => GraphNodeBase.Lerp(diagonalDown, vertical, blend),
            2 => GraphNodeBase.Lerp(vertical, diagonalUp, blend),
            _ => GraphNodeBase.Lerp(diagonalUp, horizontal, blend)
        };
    }

    public static void FindWrappedCell(float px, float py, int columns, int rows,
        int seed, float irregularity, out float nearest, out float second,
        out int cellX, out int cellY, out float localX, out float localY, float roundness)
    {
        var baseX = (int)MathF.Floor(px);
        var baseY = (int)MathF.Floor(py);
        nearest = float.MaxValue;
        second = float.MaxValue;
        cellX = cellY = 0;
        localX = localY = 0f;

        for (var oy = -1; oy <= 1; oy++)
        {
            for (var ox = -1; ox <= 1; ox++)
            {
                var rawX = baseX + ox;
                var rawY = baseY + oy;
                var wrappedX = GraphNodeBase.Mod(rawX, columns);
                var wrappedY = GraphNodeBase.Mod(rawY, rows);
                var jitterX = (GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed) - 0.5f) * irregularity * 0.7f;
                var jitterY = (GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed + 19) - 0.5f) * irregularity * 0.7f;
                var centerX = rawX + 0.5f + jitterX;
                var centerY = rawY + 0.5f + jitterY;
                var dx = px - centerX;
                var dy = py - centerY;
                dx -= MathF.Round(dx / columns) * columns;
                dy -= MathF.Round(dy / rows) * rows;
                var euclidean = MathF.Sqrt(dx * dx + dy * dy);
                var square = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
                var distance = GraphNodeBase.Lerp(square, euclidean, roundness);

                if (distance < nearest)
                {
                    second = nearest;
                    nearest = distance;
                    cellX = wrappedX;
                    cellY = wrappedY;
                    localX = dx;
                    localY = dy;
                }
                else if (distance < second)
                {
                    second = distance;
                }
            }
        }
    }
}
