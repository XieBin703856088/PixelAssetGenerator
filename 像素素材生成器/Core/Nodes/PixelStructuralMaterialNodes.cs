using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

internal static class PixelStructuralMaterialRenderer
{
    public static PixelBuffer RenderFloor(int size, int seed, string floorType, Color primary,
        Color secondary, float width, float height, PixelBuffer? background)
    {
        var palette = new[]
        {
            PixelMaterialUtility.Shade(primary, -0.46f),
            PixelMaterialUtility.Shade(primary, -0.24f),
            primary,
            PixelMaterialUtility.Mix(primary, secondary, 0.58f),
            PixelMaterialUtility.Shade(secondary, 0.18f)
        };
        var tile = new PixelTileCanvas(size, 2);
        switch (floorType)
        {
            case "herringbone":
                RenderHerringbone(tile, size, seed);
                break;
            case "stoneTile":
                RenderStoneTiles(tile, size, seed);
                break;
            case "carpet":
                RenderCarpet(tile, size, seed);
                break;
            default:
                RenderPlanks(tile, size, seed, horizontal: true);
                break;
        }
        return CompositeRegion(tile.ToPixelBuffer(palette), background, width, height);
    }

    public static PixelBuffer RenderFlagstone(int size, int seed, int tilesX, int tilesY,
        float irregularity, float mergeChance, float gapWidth, Color stone, Color gap,
        float variation, float highlight, float shadow, float roughness,
        float brightness, float contrast, bool invert)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(gap, -0.16f)),
            Adjust(PixelMaterialUtility.Shade(stone, -0.34f - shadow * 0.12f)),
            Adjust(PixelMaterialUtility.Shade(stone, -0.13f)),
            Adjust(stone),
            Adjust(PixelMaterialUtility.Shade(stone, 0.12f + variation * 0.16f)),
            Adjust(PixelMaterialUtility.Shade(stone, 0.24f + highlight * 0.18f))
        };
        var tile = new PixelTileCanvas(size);
        var rowHeight = size / (float)tilesY;
        var gapPixels = Math.Max(1, (int)MathF.Round(gapWidth * Math.Min(size / (float)tilesX, rowHeight)));

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var rowPhase = (y + 0.5f) * tilesY / size;
            var rawRow = (int)MathF.Floor(rowPhase);
            var row = GraphNodeBase.Mod(rawRow, tilesY);
            var localRow = rowPhase - rawRow;
            var edgeJitter = (GraphNodeBase.HashToUnit(row, 0, seed + 701) - 0.5f) * irregularity * 0.12f;
            var rowPixel = localRow * rowHeight + edgeJitter * rowHeight;
            if (rowPixel < gapPixels || rowPixel > rowHeight - gapPixels)
            {
                tile.Set(x, y, 0);
                continue;
            }

            var stagger = (row & 1) == 0 ? 0f : 0.5f;
            var phaseX = (x + 0.5f) * tilesX / size + stagger;
            var rawColumn = (int)MathF.Floor(phaseX);
            var column = GraphNodeBase.Mod(rawColumn, tilesX);
            var pairStart = column & ~1;
            var pairMerged = GraphNodeBase.HashToUnit(pairStart, row, seed + 709) < mergeChance;
            var previousPair = GraphNodeBase.Mod(column - 1, tilesX) & ~1;
            var mergedFromLeft = (column & 1) == 1 &&
                                 GraphNodeBase.HashToUnit(previousPair, row, seed + 709) < mergeChance;
            var groupStartRaw = mergedFromLeft ? rawColumn - 1 : rawColumn;
            var groupWidth = pairMerged && (column & 1) == 0 || mergedFromLeft ? 2f : 1f;
            var localColumn = phaseX - groupStartRaw;
            var cellWidth = size / (float)tilesX;
            var sideJitter = (GraphNodeBase.HashToUnit(column, row, seed + 719) - 0.5f) * irregularity * 0.16f;
            var sidePixel = (localColumn + sideJitter) * cellWidth;
            var groupPixels = groupWidth * cellWidth;
            if (sidePixel < gapPixels || sidePixel > groupPixels - gapPixels)
            {
                tile.Set(x, y, 0);
                continue;
            }

            var stoneId = GraphNodeBase.Mod(groupStartRaw, tilesX);
            var tone = GraphNodeBase.HashToUnit(stoneId, row, seed + 727);
            byte index = tone < 0.26f - variation * 0.18f ? (byte)2
                : tone > 0.78f - variation * 0.2f ? (byte)4 : (byte)3;
            var bevel = Math.Max(1f, 1f + roughness * 1.5f);
            if (rowPixel < gapPixels + bevel || sidePixel < gapPixels + bevel)
                index = 5;
            else if (rowPixel > rowHeight - gapPixels - bevel || sidePixel > groupPixels - gapPixels - bevel)
                index = 1;

            var crackRoll = GraphNodeBase.HashToUnit(stoneId, row, seed + 733);
            if (crackRoll > 0.78f && roughness > 0.15f)
            {
                var nx = localColumn / groupWidth - 0.5f;
                var ny = localRow - 0.5f;
                var slope = (GraphNodeBase.HashToUnit(stoneId, row, seed + 739) - 0.5f) * 1.2f;
                if (MathF.Abs(ny - nx * slope) < 0.025f + roughness * 0.02f && MathF.Abs(nx) < 0.32f)
                    index = 2;
            }
            tile.Set(x, y, index);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderWall(int size, int seed, string wallType, Color main,
        Color mortar, float width, float height, PixelBuffer? background)
    {
        var palette = new[]
        {
            PixelMaterialUtility.Shade(mortar, -0.18f),
            PixelMaterialUtility.Shade(main, -0.32f),
            PixelMaterialUtility.Shade(main, -0.12f),
            main,
            PixelMaterialUtility.Shade(main, 0.15f),
            PixelMaterialUtility.Mix(main, mortar, 0.45f)
        };
        var tile = new PixelTileCanvas(size, 3);
        switch (wallType)
        {
            case "plaster":
                RenderPlaster(tile, size, seed);
                break;
            case "planks":
                RenderPlanks(tile, size, seed, horizontal: false);
                break;
            case "adobe":
                RenderMasonry(tile, size, seed, largeBlocks: true);
                break;
            default:
                RenderMasonry(tile, size, seed, largeBlocks: false);
                break;
        }
        return CompositeRegion(tile.ToPixelBuffer(palette), background, width, height);
    }

    public static PixelBuffer RenderChainmail(int size, int seed, int density, float ringThickness,
        float metallic, float shadowStrength, Color metal, Color dark,
        float brightness, float contrast, bool invert)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(dark, -0.22f)),
            Adjust(dark),
            Adjust(PixelMaterialUtility.Mix(dark, metal, 0.48f)),
            Adjust(metal),
            Adjust(PixelMaterialUtility.Shade(metal, 0.18f + metallic * 0.16f)),
            Adjust(PixelMaterialUtility.Shade(metal, 0.22f + metallic * 0.12f))
        };
        var tile = new PixelTileCanvas(size, 0);
        var ringCount = size <= 40 ? Math.Max(4, density / 2) : density;
        var spacing = size / (float)ringCount;
        var radiusX = Math.Max(2f, spacing * 0.49f);
        var radiusY = Math.Max(2f, spacing * 0.39f);
        var wire = 0.10f + ringThickness * 0.28f;

        for (var parity = 0; parity < 2; parity++)
        for (var rawRow = -1; rawRow <= ringCount; rawRow++)
        for (var rawColumn = -1; rawColumn <= ringCount; rawColumn++)
        {
            if (((rawRow + rawColumn) & 1) != parity)
                continue;
            var row = GraphNodeBase.Mod(rawRow, ringCount);
            var column = GraphNodeBase.Mod(rawColumn, ringCount);
            var centerX = (rawColumn + 0.5f + ((row & 1) == 0 ? 0f : 0.5f)) * spacing;
            var centerY = (rawRow + 0.5f) * spacing;
            var minX = (int)MathF.Floor(centerX - radiusX - 1);
            var maxX = (int)MathF.Ceiling(centerX + radiusX + 1);
            var minY = (int)MathF.Floor(centerY - radiusY - 1);
            var maxY = (int)MathF.Ceiling(centerY + radiusY + 1);
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var nx = (x + 0.5f - centerX) / radiusX;
                var ny = (y + 0.5f - centerY) / radiusY;
                var radial = MathF.Sqrt(nx * nx + ny * ny);
                if (MathF.Abs(radial - 1f) > wire)
                    continue;
                var gapQuadrant = parity == 0 ? ny > 0.64f && MathF.Abs(nx) < 0.24f
                    : ny < -0.64f && MathF.Abs(nx) < 0.24f;
                if (gapQuadrant)
                    continue;
                byte index = nx + ny < -0.5f ? (byte)5
                    : nx + ny > 0.62f ? (byte)1
                    : MathF.Abs(nx + ny) < 0.18f && metallic > 0.45f ? (byte)4 : (byte)3;
                if (shadowStrength > 0.55f && ny > 0.45f)
                    index = 2;
                tile.SetWrapped(x, y, index);
            }
        }
        return tile.ToPixelBuffer(palette);
    }

    private static void RenderPlanks(PixelTileCanvas tile, int size, int seed, bool horizontal)
    {
        var crossCount = Math.Max(4, size / 8);
        var plankLength = Math.Max(8, size / 3);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var across = horizontal ? y : x;
            var along = horizontal ? x : y;
            var band = across * crossCount / size;
            var bandStart = band * size / crossCount;
            var localAcross = across - bandStart;
            var bandSize = Math.Max(1, (band + 1) * size / crossCount - bandStart);
            var offset = (band & 1) == 0 ? 0 : plankLength / 2;
            var localAlong = GraphNodeBase.Mod(along + offset, plankLength);
            byte index = localAcross == 0 || localAlong == 0 ? (byte)0
                : localAcross == 1 ? (byte)4
                : localAcross >= bandSize - 1 ? (byte)1
                : GraphNodeBase.HashToUnit(localAlong / 2, band, seed + 751) > 0.78f ? (byte)3 : (byte)2;
            if (localAlong == plankLength / 2 && localAcross == bandSize / 2 &&
                GraphNodeBase.HashToUnit(band, along / plankLength, seed + 757) > 0.58f)
                index = 1;
            tile.Set(x, y, index);
        }
    }

    private static void RenderHerringbone(PixelTileCanvas tile, int size, int seed)
    {
        var block = Math.Max(6, size / 4);
        var strip = Math.Max(3, block / 2);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var blockX = x / block;
            var blockY = y / block;
            var turn = ((blockX + blockY) & 1) == 0;
            var along = turn ? x + y : x - y;
            var across = turn ? x - y : x + y;
            var seam = GraphNodeBase.Mod(along, block) == 0 || GraphNodeBase.Mod(across, strip) == 0;
            byte index = seam ? (byte)0
                : GraphNodeBase.Mod(across, strip) == 1 ? (byte)4
                : GraphNodeBase.HashToUnit(blockX, blockY, seed + 769) > 0.5f ? (byte)3 : (byte)2;
            tile.Set(x, y, index);
        }
    }

    private static void RenderStoneTiles(PixelTileCanvas tile, int size, int seed)
    {
        var count = Math.Max(3, size / 10);
        var cell = size / (float)count;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var phaseX = (x + 0.5f) * count / size;
            var phaseY = (y + 0.5f) * count / size;
            var cellX = (int)MathF.Floor(phaseX);
            var cellY = (int)MathF.Floor(phaseY);
            var localX = (phaseX - cellX) * cell;
            var localY = (phaseY - cellY) * cell;
            byte index = localX < 1f || localY < 1f ? (byte)0
                : localX < 2f || localY < 2f ? (byte)4
                : localX > cell - 1.5f || localY > cell - 1.5f ? (byte)1
                : GraphNodeBase.HashToUnit(cellX, cellY, seed + 773) > 0.66f ? (byte)3 : (byte)2;
            tile.Set(x, y, index);
        }
    }

    private static void RenderCarpet(PixelTileCanvas tile, int size, int seed)
    {
        var motif = Math.Max(4, size / 6);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var macro = GraphNodeBase.TileableValueNoise(x / (float)motif, y / (float)motif,
                Math.Max(2, size / motif), seed + 787);
            var localX = GraphNodeBase.Mod(x, motif) - motif / 2;
            var localY = GraphNodeBase.Mod(y, motif) - motif / 2;
            var diamond = Math.Abs(localX) + Math.Abs(localY);
            byte index = diamond == motif / 2 ? (byte)4
                : diamond < motif / 3 ? (byte)3
                : macro < 0.35f ? (byte)1 : (byte)2;
            tile.Set(x, y, index);
        }
    }

    private static void RenderMasonry(PixelTileCanvas tile, int size, int seed, bool largeBlocks)
    {
        var rows = largeBlocks ? Math.Max(3, size / 12) : Math.Max(4, size / 8);
        var columns = largeBlocks ? Math.Max(3, size / 12) : Math.Max(4, size / 9);
        var rowHeight = size / (float)rows;
        var columnWidth = size / (float)columns;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var rowPhase = (y + 0.5f) * rows / size;
            var row = (int)MathF.Floor(rowPhase);
            var localY = (rowPhase - row) * rowHeight;
            var stagger = (row & 1) == 0 ? 0f : 0.5f;
            var columnPhase = (x + 0.5f) * columns / size + stagger;
            var column = (int)MathF.Floor(columnPhase);
            var localX = (columnPhase - column) * columnWidth;
            byte index = localX < 1f || localY < 1f ? (byte)0
                : localX < 2f || localY < 2f ? (byte)4
                : localX > columnWidth - 1.2f || localY > rowHeight - 1.2f ? (byte)1
                : GraphNodeBase.HashToUnit(column, row, seed + 797) > 0.72f ? (byte)2 : (byte)3;
            tile.Set(x, y, index);
        }
    }

    private static void RenderPlaster(PixelTileCanvas tile, int size, int seed)
    {
        var macroCell = Math.Max(4, size / 6);
        var period = Math.Max(2, size / macroCell);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var macro = GraphNodeBase.TileableFractalNoise(x / (float)macroCell, y / (float)macroCell,
                period, 2, 0.55f, 2f, seed + 809);
            byte index = macro < 0.30f ? (byte)2 : macro > 0.72f ? (byte)4 : (byte)3;
            var crack = PixelMaterialUtility.TileableDirectionalBands(x / (float)size * MathF.Tau,
                y / (float)size * MathF.Tau, 0.62f, 2, seed * 0.013f);
            if (crack > 0.965f && macro < 0.52f)
                index = 1;
            if (GraphNodeBase.HashToUnit(x / 3, y / 3, seed + 811) > 0.985f && macro < 0.4f)
                index = 5;
            tile.Set(x, y, index);
        }
    }

    private static PixelBuffer CompositeRegion(PixelBuffer generated, PixelBuffer? background,
        float width, float height)
    {
        var size = generated.Width;
        var left = Math.Clamp((int)MathF.Round(size * (1f - width) * 0.5f), 0, size - 1);
        var right = Math.Clamp(size - 1 - left, 0, size - 1);
        var top = Math.Clamp((int)MathF.Round(size * (1f - height) * 0.5f), 0, size - 1);
        var bottom = Math.Clamp(size - 1 - top, 0, size - 1);
        var result = PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            if (x >= left && x <= right && y >= top && y <= bottom)
            {
                var pixel = generated.GetPixel(x, y);
                result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, 1f);
            }
            else if (background is not null)
            {
                var sourceX = Math.Clamp(x * background.Width / size, 0, background.Width - 1);
                var sourceY = Math.Clamp(y * background.Height / size, 0, background.Height - 1);
                var pixel = background.GetPixel(sourceX, sourceY);
                result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }
        generated.Dispose();
        return result;
    }
}

public sealed class PixelFloorNode : PixelMaterialNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("背景", GraphPortType.Image, "background") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("floorType", "planks", ["planks", "herringbone", "stoneTile", "carpet"],
            ["木板", "人字拼", "石砖", "地毯"], "地板类型"),
        NodeParameterDefinition.Color("primaryColor", Color.FromRgb(160, 120, 80), "主颜色"),
        NodeParameterDefinition.Color("secondaryColor", Color.FromRgb(200, 180, 150), "辅助颜色"),
        NodeParameterDefinition.Number("width", 0.9, 0.3, 1, 0.01, "区域宽度"),
        NodeParameterDefinition.Number("height", 0.8, 0.3, 1, 0.01, "区域高度")
    };

    public override string TypeName => "Floor";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelStructuralMaterialRenderer.RenderFloor(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        GetChoice(parameters, "floorType", "planks"),
        GetColor(parameters, "primaryColor", Color.FromRgb(160, 120, 80)),
        GetColor(parameters, "secondaryColor", Color.FromRgb(200, 180, 150)),
        Math.Clamp(GetFloat(parameters, "width", 0.9f), 0.3f, 1f),
        Math.Clamp(GetFloat(parameters, "height", 0.8f), 0.3f, 1f), inputs.FirstOrDefault());
}

public sealed class PixelFlagstoneNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("tilesX", 4, 2, 10, 1, "横向石板"),
        NodeParameterDefinition.Integer("tilesY", 4, 2, 10, 1, "纵向石板"),
        NodeParameterDefinition.Number("irregularity", 0.3, 0, 0.8, 0.01, "不规则度"),
        NodeParameterDefinition.Number("mergeChance", 0.2, 0, 0.6, 0.01, "合并概率"),
        NodeParameterDefinition.Number("gapWidth", 0.06, 0.01, 0.2, 0.01, "缝隙宽度"),
        NodeParameterDefinition.Color("stoneColor", Color.FromRgb(180, 170, 155), "石板颜色"),
        NodeParameterDefinition.Color("gapColor", Color.FromRgb(65, 60, 55), "缝隙颜色"),
        NodeParameterDefinition.Number("colorVariation", 0.2, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("highlight", 0.2, 0, 1, 0.01, "高光"),
        NodeParameterDefinition.Number("shadow", 0.2, 0, 1, 0.01, "阴影"),
        NodeParameterDefinition.Number("roughness", 0.3, 0, 1, 0.01, "粗糙度"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0.15, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Flagstone";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelStructuralMaterialRenderer.RenderFlagstone(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetInt(parameters, "tilesX", 4), 2, 10), Math.Clamp(GetInt(parameters, "tilesY", 4), 2, 10),
        Math.Clamp(GetFloat(parameters, "irregularity", 0.3f), 0f, 0.8f),
        Math.Clamp(GetFloat(parameters, "mergeChance", 0.2f), 0f, 0.6f),
        Math.Clamp(GetFloat(parameters, "gapWidth", 0.06f), 0.01f, 0.2f),
        GetColor(parameters, "stoneColor", Color.FromRgb(180, 170, 155)),
        GetColor(parameters, "gapColor", Color.FromRgb(65, 60, 55)),
        Math.Clamp(GetFloat(parameters, "colorVariation", 0.2f), 0f, 0.5f),
        Math.Clamp(GetFloat(parameters, "highlight", 0.2f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "shadow", 0.2f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "roughness", 0.3f), 0f, 1f),
        GetFloat(parameters, "brightness", 0f), GetFloat(parameters, "contrast", 0.15f),
        GetBool(parameters, "invert", false));
}

public sealed class PixelWallNode : PixelMaterialNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("背景", GraphPortType.Image, "background") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("wallType", "stone", ["stone", "plaster", "planks", "adobe"],
            ["石墙", "灰泥", "木墙", "土坯"], "墙壁类型"),
        NodeParameterDefinition.Color("mainColor", Color.FromRgb(180, 170, 155), "主颜色"),
        NodeParameterDefinition.Color("mortarColor", Color.FromRgb(140, 130, 115), "灰浆颜色"),
        NodeParameterDefinition.Number("width", 0.9, 0.3, 1, 0.01, "区域宽度"),
        NodeParameterDefinition.Number("height", 0.8, 0.3, 1, 0.01, "区域高度")
    };

    public override string TypeName => "Wall";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelStructuralMaterialRenderer.RenderWall(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        GetChoice(parameters, "wallType", "stone"),
        GetColor(parameters, "mainColor", Color.FromRgb(180, 170, 155)),
        GetColor(parameters, "mortarColor", Color.FromRgb(140, 130, 115)),
        Math.Clamp(GetFloat(parameters, "width", 0.9f), 0.3f, 1f),
        Math.Clamp(GetFloat(parameters, "height", 0.8f), 0.3f, 1f), inputs.FirstOrDefault());
}

public sealed class PixelChainmailNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("ringDensity", 8, 3, 16, 1, "环密度"),
        NodeParameterDefinition.Number("ringThickness", 0.3, 0.1, 0.6, 0.01, "环粗细"),
        NodeParameterDefinition.Number("metallic", 0.75, 0, 1, 0.01, "金属度"),
        NodeParameterDefinition.Number("shadowStrength", 0.5, 0, 1, 0.01, "阴影强度"),
        NodeParameterDefinition.Color("metalColor", Color.FromRgb(200, 192, 184), "金属颜色"),
        NodeParameterDefinition.Color("darkColor", Color.FromRgb(58, 53, 48), "暗部颜色"),
        NodeParameterDefinition.Number("brightness", 0.05, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0.2, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Chainmail";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelStructuralMaterialRenderer.RenderChainmail(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetInt(parameters, "ringDensity", 8), 3, 16),
        Math.Clamp(GetFloat(parameters, "ringThickness", 0.3f), 0.1f, 0.6f),
        Math.Clamp(GetFloat(parameters, "metallic", 0.75f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "shadowStrength", 0.5f), 0f, 1f),
        GetColor(parameters, "metalColor", Color.FromRgb(200, 192, 184)),
        GetColor(parameters, "darkColor", Color.FromRgb(58, 53, 48)),
        GetFloat(parameters, "brightness", 0.05f), GetFloat(parameters, "contrast", 0.2f),
        GetBool(parameters, "invert", false));
}
