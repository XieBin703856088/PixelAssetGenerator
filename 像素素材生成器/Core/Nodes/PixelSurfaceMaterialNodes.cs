using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

internal static class PixelRpgTileRenderer
{
    public static PixelBuffer RenderGrass(int size, int seed, float density, float length, float width,
        float clumping, float detail, float variation, float brightness, float contrast, bool invert,
        float windAngle, float wind, Color grass1, Color grass2, Color soil)
    {
        var palette = new[]
        {
            PixelMaterialUtility.Adjust(PixelMaterialUtility.Shade(soil, -0.18f), brightness, contrast, invert),
            PixelMaterialUtility.Adjust(PixelMaterialUtility.Shade(grass1, -0.34f), brightness, contrast, invert),
            PixelMaterialUtility.Adjust(grass1, brightness, contrast, invert),
            PixelMaterialUtility.Adjust(PixelMaterialUtility.Mix(grass1, grass2, 0.42f + variation), brightness, contrast, invert),
            PixelMaterialUtility.Adjust(PixelMaterialUtility.Shade(grass2, 0.18f), brightness, contrast, invert)
        };
        var tile = new PixelTileCanvas(size, 2);
        var macroCell = Math.Max(3, size / 6);
        var macroPeriod = Math.Max(2, size / macroCell);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var macro = GraphNodeBase.TileableFractalNoise(
                x / (float)macroCell, y / (float)macroCell, macroPeriod, 2, 0.58f, 2f, seed + 17);
            var broad = GraphNodeBase.TileableValueNoise(
                x / (float)(macroCell * 2), y / (float)(macroCell * 2),
                Math.Max(1, macroPeriod / 2), seed + 43);
            var coverage = GraphNodeBase.Lerp(macro, broad, clumping * 0.45f);
            byte index = coverage switch
            {
                < 0.24f => 1,
                < 0.52f => 2,
                < 0.78f => 3,
                _ => 4
            };
            if (coverage < 0.27f - density * 0.16f)
                index = 0;
            tile.Set(x, y, index);
        }

        var tuftCell = Math.Max(4, size / 8);
        var cellCount = Math.Max(1, size / tuftCell);
        var directionX = MathF.Cos(windAngle);
        for (var cellY = 0; cellY < cellCount; cellY++)
        for (var cellX = 0; cellX < cellCount; cellX++)
        {
            var roll = GraphNodeBase.HashToUnit(cellX, cellY, seed + 101);
            if (roll > density * (0.26f + clumping * 0.18f))
                continue;

            var anchorX = cellX * tuftCell + 1 +
                (int)MathF.Round(GraphNodeBase.HashToUnit(cellX, cellY, seed + 107) * Math.Max(1, tuftCell - 2));
            var anchorY = cellY * tuftCell + tuftCell - 1;
            var bladeHeight = Math.Clamp((int)MathF.Round(tuftCell * (0.35f + length * 0.72f)), 2, tuftCell + 2);
            var lean = (int)MathF.Round(directionX * wind * Math.Max(1, bladeHeight / 2f));
            var thickness = width > 0.72f ? 2 : 1;

            tile.DrawLineWrapped(anchorX, anchorY, anchorX + lean, anchorY - bladeHeight, 3, thickness);
            tile.DrawLineWrapped(anchorX - 1, anchorY, anchorX - 2 + lean / 2,
                anchorY - Math.Max(2, bladeHeight * 2 / 3), 1);
            tile.DrawLineWrapped(anchorX + 1, anchorY, anchorX + 2 + lean / 2,
                anchorY - Math.Max(2, bladeHeight * 3 / 4), 3);
            tile.SetWrapped(anchorX + lean, anchorY - bladeHeight, 4);
            tile.SetWrapped(anchorX, anchorY, 1);

            if (detail > 0.55f && GraphNodeBase.HashToUnit(cellX, cellY, seed + 127) < detail * 0.5f)
            {
                tile.DrawLineWrapped(anchorX + 2, anchorY,
                    anchorX + 3 - lean / 2, anchorY - Math.Max(2, bladeHeight / 2), 4);
            }
        }

        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderCobblestone(int size, int seed, int columns, int rows,
        float irregularity, float gapWidth, float roundness, float variation, float highlight,
        float shadow, float bump, float edgeRoughness, float surfaceDetail, bool invert,
        Color stone, Color gap)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(gap, -0.14f)),
            Adjust(PixelMaterialUtility.Shade(stone, -0.42f - shadow * 0.16f)),
            Adjust(PixelMaterialUtility.Shade(stone, -0.22f)),
            Adjust(PixelMaterialUtility.Mix(stone, gap, variation * 0.34f)),
            Adjust(stone),
            Adjust(PixelMaterialUtility.Shade(stone, 0.13f + highlight * 0.12f)),
            Adjust(PixelMaterialUtility.Shade(stone, 0.26f + highlight * 0.18f))
        };
        var tile = new PixelTileCanvas(size);
        var cellWidth = size / (float)columns;
        var cellHeight = size / (float)rows;
        var exponent = 1.55f + roundness * 0.8f;
        var radiusScale = Math.Clamp(0.49f - gapWidth * 0.30f, 0.36f, 0.49f);
        var rimStart = 0.78f - bump * 0.12f;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var baseCellX = (int)MathF.Floor((x + 0.5f) / cellWidth);
            var baseCellY = (int)MathF.Floor((y + 0.5f) / cellHeight);
            var bestMetric = float.MaxValue;
            var selectedCellX = 0;
            var selectedCellY = 0;
            var selectedX = 0f;
            var selectedY = 0f;
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var rawCellX = baseCellX + ox;
                var rawCellY = baseCellY + oy;
                var wrappedX = GraphNodeBase.Mod(rawCellX, columns);
                var wrappedY = GraphNodeBase.Mod(rawCellY, rows);
                var stagger = (wrappedY & 1) == 0 ? 0f : 0.5f;
                var jitterX = (GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed + 401) - 0.5f)
                              * irregularity * cellWidth * 0.68f;
                var jitterY = (GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed + 409) - 0.5f)
                              * irregularity * cellHeight * 0.58f;
                var centerX = (rawCellX + 0.5f + stagger) * cellWidth + jitterX;
                var centerY = (rawCellY + 0.5f) * cellHeight + jitterY;
                var deltaX = x + 0.5f - centerX;
                var deltaY = y + 0.5f - centerY;
                deltaX -= MathF.Round(deltaX / size) * size;
                deltaY -= MathF.Round(deltaY / size) * size;
                var radiusVariation = 0.74f + GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed + 419) * 0.42f;
                var radiusX = Math.Max(1f, cellWidth * radiusScale * radiusVariation);
                var radiusY = Math.Max(1f, cellHeight * (radiusScale - 0.025f) *
                    (0.76f + GraphNodeBase.HashToUnit(wrappedX, wrappedY, seed + 421) * 0.38f));
                var normalizedX = deltaX / radiusX;
                var normalizedY = deltaY / radiusY;
                var metric = MathF.Pow(MathF.Abs(normalizedX), exponent) +
                             MathF.Pow(MathF.Abs(normalizedY), exponent);
                var sector = GraphNodeBase.Mod((int)MathF.Floor(
                    (MathF.Atan2(normalizedY, normalizedX) + MathF.PI) / MathF.Tau * 12f), 12);
                metric *= 1f + (GraphNodeBase.HashToUnit(sector, wrappedX + wrappedY * columns, seed + 427) - 0.5f)
                    * edgeRoughness * 0.13f;
                if (metric >= bestMetric)
                    continue;
                bestMetric = metric;
                selectedCellX = wrappedX;
                selectedCellY = wrappedY;
                selectedX = normalizedX;
                selectedY = normalizedY;
            }

            if (bestMetric > 1f)
            {
                tile.Set(x, y, 0);
                continue;
            }

            var cellTone = GraphNodeBase.HashToUnit(selectedCellX, selectedCellY, seed + 431);
            byte index = cellTone < 0.28f - variation * 0.18f ? (byte)3
                : cellTone > 0.78f - variation * 0.2f ? (byte)5 : (byte)4;
            if (bestMetric > rimStart)
                index = selectedX + selectedY < 0f ? (byte)6 : (byte)1;
            else if (selectedX + selectedY > 0.72f)
                index = 2;

            var crackRoll = GraphNodeBase.HashToUnit(selectedCellX, selectedCellY, seed + 463);
            if (surfaceDetail > 0.08f && crackRoll > 1f - surfaceDetail * 0.42f)
            {
                var slope = (GraphNodeBase.HashToUnit(selectedCellX, selectedCellY, seed + 467) - 0.5f) * 1.4f;
                var offset = (GraphNodeBase.HashToUnit(selectedCellX, selectedCellY, seed + 471) - 0.5f) * 0.18f;
                var crackDistance = MathF.Abs(selectedY - selectedX * slope - offset);
                if (crackDistance < 0.025f + surfaceDetail * 0.018f &&
                    MathF.Abs(selectedX) < 0.48f && bestMetric < rimStart)
                    index = 2;
            }
            tile.Set(x, y, index);
        }

        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderFabric(int size, int seed, int density, float thickness,
        string weaveType, Color warp, Color weft, float variation, float softness,
        float fuzziness, float glossiness, float brightness, float contrast, bool invert)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var deep = PixelMaterialUtility.Shade(PixelMaterialUtility.Mix(warp, weft, 0.5f), -0.30f);
        var palette = new[]
        {
            Adjust(deep),
            Adjust(PixelMaterialUtility.Shade(warp, -0.26f)),
            Adjust(warp),
            Adjust(PixelMaterialUtility.Shade(warp, 0.20f + glossiness * 0.12f)),
            Adjust(PixelMaterialUtility.Shade(weft, -0.26f)),
            Adjust(weft),
            Adjust(PixelMaterialUtility.Shade(weft, 0.20f + glossiness * 0.12f))
        };
        var tile = new PixelTileCanvas(size);
        var threadCount = Math.Clamp(density, 4, 20);
        var threadHalfWidth = 0.15f + thickness * 0.25f;
        var foldCell = Math.Max(2, size / threadCount * 3);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var phaseX = (x + 0.5f) * threadCount / size;
            var phaseY = (y + 0.5f) * threadCount / size;
            var cellX = (int)MathF.Floor(phaseX);
            var cellY = (int)MathF.Floor(phaseY);
            var centeredX = phaseX - cellX - 0.5f;
            var centeredY = phaseY - cellY - 0.5f;
            var onWarp = MathF.Abs(centeredX) <= threadHalfWidth;
            var onWeft = MathF.Abs(centeredY) <= threadHalfWidth;
            if (!onWarp && !onWeft)
            {
                var fuzz = GraphNodeBase.HashToUnit(x, y, seed + 557);
                tile.Set(x, y, fuzz < fuzziness * 0.055f ? (byte)(fuzz < 0.025f ? 2 : 5) : (byte)0);
                continue;
            }

            var warpOver = weaveType switch
            {
                "twill" => GraphNodeBase.Mod(cellX - cellY, 3) != 2,
                "satin" => GraphNodeBase.Mod(cellX * 2 + cellY * 3, 5) == 0,
                _ => ((cellX + cellY) & 1) == 0
            };
            byte index;
            if (onWarp && (!onWeft || warpOver))
                index = centeredX < -threadHalfWidth * 0.3f ? (byte)3
                    : centeredX > threadHalfWidth * 0.3f ? (byte)1 : (byte)2;
            else
                index = centeredY < -threadHalfWidth * 0.3f ? (byte)6
                    : centeredY > threadHalfWidth * 0.3f ? (byte)4 : (byte)5;

            var fold = GraphNodeBase.TileableValueNoise(
                x / (float)foldCell, y / (float)foldCell,
                Math.Max(1, size / foldCell), seed + 577);
            if (fold < 0.22f * softness && index is 2 or 3)
                index = 1;
            else if (fold > 0.84f - variation * 0.22f && index is 2 or 5)
                index = index == 2 ? (byte)3 : (byte)6;
            tile.Set(x, y, index);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderLeather(int size, int seed, float scale, float wrinkleStrength,
        float wrinkleAngle, float grainAmount, Color leather, Color deep, Color highlight,
        float variation, float glossiness, float brightness, float contrast, bool invert)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(deep, -0.18f)),
            Adjust(deep),
            Adjust(PixelMaterialUtility.Mix(deep, leather, 0.58f)),
            Adjust(leather),
            Adjust(PixelMaterialUtility.Mix(leather, highlight, 0.35f + variation)),
            Adjust(PixelMaterialUtility.Mix(leather, highlight, 0.68f + glossiness * 0.22f))
        };
        var tile = new PixelTileCanvas(size, 3);
        var cells = Math.Clamp((int)MathF.Round(scale * 3f), 3, 18);
        var macroCell = Math.Max(2, size / Math.Max(3, cells / 2));
        var macroPeriod = Math.Max(2, size / macroCell);
        var direction = Math.Clamp(wrinkleAngle / 180f, 0f, 1f);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            PixelMaterialUtility.FindWrappedCell(
                (x + 0.5f) * cells / size,
                (y + 0.5f) * cells / size,
                cells, cells, seed + 601, 0.78f,
                out var nearest, out var second, out var cellX, out var cellY,
                out var localX, out var localY, 0.92f);
            var ridge = second - nearest;
            var macro = GraphNodeBase.TileableFractalNoise(
                x / (float)macroCell, y / (float)macroCell, macroPeriod, 2, 0.56f, 2f, seed + 617);
            byte index = macro < 0.30f ? (byte)2 : macro > 0.72f ? (byte)4 : (byte)3;

            var creaseWidth = 0.025f + wrinkleStrength * 0.065f;
            if (ridge < creaseWidth)
                index = 1;
            else if (ridge < creaseWidth + 0.035f * (0.4f + glossiness) && localX + localY < 0f)
                index = 5;

            var warpNoise = GraphNodeBase.TileableValueNoise(
                x / (float)Math.Max(2, macroCell * 2), y / (float)Math.Max(2, macroCell * 2),
                Math.Max(1, macroPeriod / 2), seed + 631);
            var phaseX = x / (float)size * MathF.Tau + (warpNoise - 0.5f) * 0.9f * wrinkleStrength;
            var phaseY = y / (float)size * MathF.Tau + (macro - 0.5f) * 0.9f * wrinkleStrength;
            var fold = PixelMaterialUtility.TileableDirectionalBands(
                phaseX, phaseY, direction, Math.Max(1, cells / 3), seed * 0.017f);
            if (wrinkleStrength > 0.72f && fold > 0.965f - wrinkleStrength * 0.055f && macro < 0.58f)
                index = 0;
            else if (glossiness > 0.12f && fold > 0.89f && fold < 0.93f && macro > 0.48f)
                index = 5;
            tile.Set(x, y, index);
        }

        var poreCell = Math.Max(4, size / 8);
        var poreCount = Math.Max(1, size / poreCell);
        for (var cy = 0; cy < poreCount; cy++)
        for (var cx = 0; cx < poreCount; cx++)
        {
            if (GraphNodeBase.HashToUnit(cx, cy, seed + 661) > grainAmount * 0.62f)
                continue;
            var px = cx * poreCell + (int)(GraphNodeBase.HashToUnit(cx, cy, seed + 667) * poreCell);
            var py = cy * poreCell + (int)(GraphNodeBase.HashToUnit(cx, cy, seed + 673) * poreCell);
            tile.SetWrapped(px, py, 1);
            if (glossiness > 0.35f)
                tile.SetWrapped(px - 1, py - 1, 4);
        }
        return tile.ToPixelBuffer(palette);
    }
}

public sealed class PixelFabricNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("density", 8, 4, 20, 1, "织线密度"),
        NodeParameterDefinition.Number("threadThickness", 0.4, 0.1, 0.9, 0.01, "织线粗细"),
        NodeParameterDefinition.Choice("weaveType", "plain", ["plain", "twill", "satin"],
            ["平纹", "斜纹", "缎纹"], "织法"),
        NodeParameterDefinition.Color("warpColor", Color.FromRgb(160, 100, 60), "经线颜色"),
        NodeParameterDefinition.Color("weftColor", Color.FromRgb(140, 80, 50), "纬线颜色"),
        NodeParameterDefinition.Number("colorVariation", 0.15, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("softness", 0.5, 0, 1, 0.01, "柔软度"),
        NodeParameterDefinition.Number("fuzziness", 0.3, 0, 1, 0.01, "绒毛"),
        NodeParameterDefinition.Number("glossiness", 0.2, 0, 1, 0.01, "光泽"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0.15, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Fabric";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
        => PixelRpgTileRenderer.RenderFabric(
            context.GetEffectiveSize(),
            GetInt(parameters, "seed", context.Seed),
            Math.Clamp(GetInt(parameters, "density", 8), 4, 20),
            Math.Clamp(GetFloat(parameters, "threadThickness", 0.4f), 0.1f, 0.9f),
            GetChoice(parameters, "weaveType", "plain"),
            GetColor(parameters, "warpColor", Color.FromRgb(160, 100, 60)),
            GetColor(parameters, "weftColor", Color.FromRgb(140, 80, 50)),
            Math.Clamp(GetFloat(parameters, "colorVariation", 0.15f), 0f, 0.5f),
            Math.Clamp(GetFloat(parameters, "softness", 0.5f), 0f, 1f),
            Math.Clamp(GetFloat(parameters, "fuzziness", 0.3f), 0f, 1f),
            Math.Clamp(GetFloat(parameters, "glossiness", 0.2f), 0f, 1f),
            GetFloat(parameters, "brightness", 0f),
            GetFloat(parameters, "contrast", 0.15f),
            GetBool(parameters, "invert", false));
}

public sealed class PixelLeatherNode : PixelMaterialNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("基础表面", GraphPortType.Image, "baseSurface")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("scale", 2, 0.5, 8, 0.01, "纹理尺度"),
        NodeParameterDefinition.Number("wrinkleStrength", 0.6, 0, 1, 0.01, "褶皱强度"),
        NodeParameterDefinition.Number("wrinkleAngle", 0, 0, 180, 1, "褶皱角度"),
        NodeParameterDefinition.Number("grainAmount", 0.4, 0, 1, 0.01, "皮纹颗粒"),
        NodeParameterDefinition.Color("leatherColor", Color.FromRgb(140, 80, 50), "皮革颜色"),
        NodeParameterDefinition.Color("deepColor", Color.FromRgb(80, 45, 30), "深色"),
        NodeParameterDefinition.Color("highlightColor", Color.FromRgb(200, 170, 140), "高光"),
        NodeParameterDefinition.Number("colorVariation", 0.15, 0, 0.4, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("glossiness", 0.3, 0, 1, 0.01, "光泽"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Leather";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var generated = PixelRpgTileRenderer.RenderLeather(
            context.GetEffectiveSize(),
            GetInt(parameters, "seed", context.Seed),
            Math.Clamp(GetFloat(parameters, "scale", 2f), 0.5f, 8f),
            Math.Clamp(GetFloat(parameters, "wrinkleStrength", 0.6f), 0f, 1f),
            Math.Clamp(GetFloat(parameters, "wrinkleAngle", 0f), 0f, 180f),
            Math.Clamp(GetFloat(parameters, "grainAmount", 0.4f), 0f, 1f),
            GetColor(parameters, "leatherColor", Color.FromRgb(140, 80, 50)),
            GetColor(parameters, "deepColor", Color.FromRgb(80, 45, 30)),
            GetColor(parameters, "highlightColor", Color.FromRgb(200, 170, 140)),
            Math.Clamp(GetFloat(parameters, "colorVariation", 0.15f), 0f, 0.4f),
            Math.Clamp(GetFloat(parameters, "glossiness", 0.3f), 0f, 1f),
            GetFloat(parameters, "brightness", 0f),
            GetFloat(parameters, "contrast", 0f),
            GetBool(parameters, "invert", false));
        var baseSurface = inputs.FirstOrDefault();
        if (baseSurface is null)
            return generated;

        var result = PixelBufferPool.Borrow(generated.Width, generated.Height);
        for (var y = 0; y < generated.Height; y++)
        for (var x = 0; x < generated.Width; x++)
        {
            var sourceX = Math.Clamp(x * baseSurface.Width / generated.Width, 0, baseSurface.Width - 1);
            var sourceY = Math.Clamp(y * baseSurface.Height / generated.Height, 0, baseSurface.Height - 1);
            var leatherPixel = generated.GetPixel(x, y);
            var basePixel = baseSurface.GetPixel(sourceX, sourceY);
            result.SetPixel(x, y,
                leatherPixel.R * 0.78f + basePixel.R * 0.22f,
                leatherPixel.G * 0.78f + basePixel.G * 0.22f,
                leatherPixel.B * 0.78f + basePixel.B * 0.22f,
                Math.Max(leatherPixel.A, basePixel.A));
        }
        generated.Dispose();
        return result;
    }
}
