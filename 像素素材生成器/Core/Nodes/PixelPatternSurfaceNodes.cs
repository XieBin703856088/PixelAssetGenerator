using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

internal static class PixelPatternSurfaceRenderer
{
    public static PixelBuffer RenderRain(int size, int seed, float density, float speed, float wind,
        int length, int pixelSize, float time)
    {
        var canvas = new PixelSpriteCanvas(size, size);
        var shadow = canvas.CreateMask();
        var body = canvas.CreateMask();
        var glint = canvas.CreateMask();
        var unit = Math.Clamp(pixelSize, 1, Math.Max(1, size / 8));
        var dropCount = Math.Max(4, (int)MathF.Round(size * size / 34f * density));
        var animation = time * speed;

        for (var i = 0; i < dropCount; i++)
        {
            var baseX = (int)MathF.Floor(GraphNodeBase.HashToUnit(i, seed, 1103) * size);
            var baseY = GraphNodeBase.HashToUnit(i, seed, 1109) * size;
            var phase = baseY / size + animation * (0.78f + GraphNodeBase.HashToUnit(i, seed, 1117) * 0.44f);
            phase -= MathF.Floor(phase);
            var headY = Snap((int)MathF.Round(phase * (size + length)) - length / 2, unit);
            var dropLength = Math.Max(unit * 2, Snap((int)MathF.Round(length *
                (0.72f + GraphNodeBase.HashToUnit(i, seed, 1123) * 0.5f)), unit));
            var shear = Snap((int)MathF.Round(wind * dropLength * 0.55f), unit);
            var headX = Snap(baseX + (int)MathF.Round(wind * animation * size * 0.24f), unit);
            var tailX = headX - shear;
            var tailY = headY - dropLength;

            AddWrappedLine(canvas, shadow, tailX + unit, tailY, headX + unit, headY, unit, size);
            AddWrappedLine(canvas, body, tailX, tailY, headX, headY, unit, size);
            AddWrappedLine(canvas, glint, tailX, tailY, tailX + Math.Sign(shear), tailY + unit, 1, size);

            if (headY >= size - unit * 2 && GraphNodeBase.HashToUnit(i, seed, 1129) < 0.42f)
            {
                AddWrappedLine(canvas, body, headX - unit * 2, size - unit,
                    headX - unit, size - unit * 2, 1, size);
                AddWrappedLine(canvas, body, headX + unit, size - unit * 2,
                    headX + unit * 2, size - unit, 1, size);
                AddWrappedLine(canvas, glint, headX - unit, size - unit,
                    headX + unit, size - unit, 1, size);
            }
        }

        canvas.Paint(shadow, Color.FromRgb(28, 52, 88));
        canvas.Paint(body, Color.FromRgb(102, 164, 218));
        canvas.Paint(glint, Color.FromRgb(208, 236, 255));
        return canvas.ToPixelBuffer();
    }

    public static PixelBuffer RenderMarble(int size, int seed, float scale, float veinFrequency,
        float sharpness, Color veinColor, Color background, float distortion, int octaves, bool invert)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(background, -0.16f)),
            Adjust(background),
            Adjust(PixelMaterialUtility.Shade(background, 0.1f)),
            Adjust(PixelMaterialUtility.Mix(background, veinColor, 0.42f)),
            Adjust(veinColor),
            Adjust(PixelMaterialUtility.Shade(veinColor, 0.22f))
        };
        var tile = new PixelTileCanvas(size, 1);
        var macroCells = Math.Clamp((int)MathF.Round(scale * 1.5f), 2, 12);
        var macroSize = Math.Max(2, size / macroCells);
        var period = Math.Max(2, size / macroSize);
        var frequency = Math.Clamp((int)MathF.Round(veinFrequency * 0.58f), 2, 8);
        var secondaryFrequency = Math.Max(2, frequency / 2 + 1);
        var veinWidth = 0.16f - Math.Clamp(sharpness, 0f, 1f) * 0.07f;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var u = (x + 0.5f) / size;
            var v = (y + 0.5f) / size;
            var warpA = GraphNodeBase.TileableFractalNoise(
                x / (float)macroSize, y / (float)macroSize, period,
                Math.Clamp(octaves, 1, 6), 0.55f, 2f, seed + 1201);
            var warpB = GraphNodeBase.TileableFractalNoise(
                x / (float)macroSize + 17f, y / (float)macroSize + 31f, period,
                Math.Clamp(octaves - 1, 1, 5), 0.58f, 2f, seed + 1213);
            var primaryPhase = MathF.Tau * (u * frequency + v * Math.Max(1, frequency - 1)) +
                               (warpA - 0.5f) * distortion * 2.8f;
            var branchPhase = MathF.Tau * (u * secondaryFrequency - v * (secondaryFrequency + 1)) +
                              (warpB - 0.5f) * distortion * 2.2f + 1.7f;
            var primary = MathF.Abs(MathF.Sin(primaryPhase));
            var branch = MathF.Abs(MathF.Sin(branchPhase));
            var branchGate = warpA > 0.47f && warpB < 0.7f;
            var distance = branchGate ? MathF.Min(primary, branch * 1.35f) : primary;

            byte index;
            if (distance < veinWidth * 0.28f)
                index = 5;
            else if (distance < veinWidth)
                index = 4;
            else if (distance < veinWidth * 2.15f)
                index = 3;
            else
            {
                var stone = warpA * 0.68f + warpB * 0.32f;
                index = stone < 0.34f ? (byte)0 : stone > 0.68f ? (byte)2 : (byte)1;
            }
            tile.Set(x, y, index);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderScales(int size, int seed, int rows, int columns, float overlap,
        float roundness, Color scaleColor, Color gapColor, float highlight, float shadow,
        float variation, bool invert)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(gapColor, -0.18f)),
            Adjust(PixelMaterialUtility.Shade(scaleColor, -0.34f - shadow * 0.16f)),
            Adjust(PixelMaterialUtility.Shade(scaleColor, -0.12f)),
            Adjust(scaleColor),
            Adjust(PixelMaterialUtility.Shade(scaleColor, 0.1f + variation * 0.18f)),
            Adjust(PixelMaterialUtility.Shade(scaleColor, 0.2f + highlight * 0.22f))
        };
        var tile = new PixelTileCanvas(size, 3);
        var detailLimit = size <= 32 ? Math.Max(3, size / 5) : Math.Max(3, size / 4);
        rows = Math.Clamp(rows, 3, detailLimit);
        columns = Math.Clamp(columns, 3, detailLimit);
        var cellWidth = size / (float)columns;
        var cellHeight = size / (float)rows;
        var gapX = Math.Clamp(0.55f / cellWidth, 0.055f, 0.18f);
        var gapY = Math.Clamp(0.65f / cellHeight, 0.06f, 0.2f);
        var curveDepth = 0.24f + Math.Clamp(overlap, 0f, 0.8f) * 0.28f;
        var exponent = 1.15f + Math.Clamp(roundness, 0.1f, 1f) * 1.65f;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var rowPhase = (y + 0.5f) * rows / size;
            var rawRow = (int)MathF.Floor(rowPhase);
            var row = GraphNodeBase.Mod(rawRow, rows);
            var localY = rowPhase - rawRow;
            var columnPhase = (x + 0.5f) * columns / size - ((row & 1) == 0 ? 0f : 0.5f);
            var rawColumn = (int)MathF.Floor(columnPhase);
            var column = GraphNodeBase.Mod(rawColumn, columns);
            var localX = columnPhase - rawColumn;
            var normalizedX = MathF.Abs(localX - 0.5f) * 2f;
            var bottom = 1f - curveDepth * MathF.Pow(normalizedX, exponent);

            if (localX < gapX || localX > 1f - gapX || localY > bottom - gapY)
            {
                tile.Set(x, y, 0);
                continue;
            }

            var tone = GraphNodeBase.HashToUnit(column, row, seed + 1301);
            byte index = tone < 0.28f - variation * 0.22f ? (byte)2
                : tone > 0.72f - variation * 0.18f ? (byte)4 : (byte)3;
            if (localY < gapY * 1.65f && normalizedX < 0.82f)
                index = 5;
            else if (localY > bottom - gapY * 2.15f || localX > 1f - gapX * 2.1f)
                index = 1;
            else if (localX < gapX * 2.1f)
                index = 4;

            if (GraphNodeBase.HashToUnit(column, row, seed + 1319) > 0.82f &&
                localY is > 0.28f and < 0.5f && MathF.Abs(localX - 0.34f) < gapX)
                index = 5;
            tile.Set(x, y, index);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderRust(int size, int seed, float amount, float scale,
        float edgeCorrosion, string corrosionType, Color orangeRust, Color brownRust,
        Color patina, float flakeSize, float pitting, float brightness, float contrast,
        bool invert, PixelBuffer? baseSurface)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var metal = Color.FromRgb(132, 142, 146);
        var rustLight = corrosionType == "patina"
            ? PixelMaterialUtility.Shade(patina, 0.17f)
            : corrosionType == "mixed" ? PixelMaterialUtility.Mix(orangeRust, patina, 0.38f) : orangeRust;
        var rustMid = corrosionType == "patina"
            ? patina
            : corrosionType == "mixed" ? PixelMaterialUtility.Mix(brownRust, patina, 0.28f) : brownRust;
        var rustDark = corrosionType == "patina"
            ? PixelMaterialUtility.Shade(patina, -0.38f)
            : PixelMaterialUtility.Shade(brownRust, -0.38f);
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(metal, -0.42f)),
            Adjust(PixelMaterialUtility.Shade(metal, -0.12f)),
            Adjust(metal),
            Adjust(rustDark),
            Adjust(rustMid),
            Adjust(rustLight)
        };
        var tile = new PixelTileCanvas(size, 2);
        var clusters = Math.Clamp((int)MathF.Round(scale * 1.6f), 2, 14);
        var clusterSize = Math.Max(2, size / clusters);
        var period = Math.Max(2, size / clusterSize);
        var threshold = 0.76f - Math.Clamp(amount, 0f, 1f) * 0.46f;

        var result = baseSurface == null ? null : PixelBufferPool.Borrow(size, size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var broad = GraphNodeBase.TileableFractalNoise(
                x / (float)clusterSize, y / (float)clusterSize, period, 3, 0.59f, 2f, seed + 1409);
            var flakes = GraphNodeBase.TileableValueNoise(
                x / (float)Math.Max(1, clusterSize / 2), y / (float)Math.Max(1, clusterSize / 2),
                Math.Max(2, period * 2), seed + 1423);
            var signal = broad * 0.74f + flakes * 0.26f;

            if (baseSurface != null && edgeCorrosion > 0f)
            {
                var center = Sample(baseSurface, x, y);
                var left = Sample(baseSurface, x - 1, y);
                var right = Sample(baseSurface, x + 1, y);
                var up = Sample(baseSurface, x, y - 1);
                var down = Sample(baseSurface, x, y + 1);
                var centerLum = Luminance(center.R, center.G, center.B);
                var edge = (MathF.Abs(centerLum - Luminance(left.R, left.G, left.B)) +
                            MathF.Abs(centerLum - Luminance(right.R, right.G, right.B)) +
                            MathF.Abs(centerLum - Luminance(up.R, up.G, up.B)) +
                            MathF.Abs(centerLum - Luminance(down.R, down.G, down.B))) * 0.7f;
                signal += Math.Clamp(edge, 0f, 1f) * edgeCorrosion * 0.38f;
            }

            byte index;
            if (signal <= threshold)
                index = broad < 0.37f ? (byte)1 : (byte)2;
            else
            {
                var normalized = Math.Clamp((signal - threshold) / Math.Max(0.05f, 1f - threshold), 0f, 1f);
                index = normalized < 0.32f ? (byte)5 : normalized < 0.7f ? (byte)4 : (byte)3;
                var pitCell = Math.Max(1, (int)MathF.Round(3f - pitting * 2f));
                var pit = GraphNodeBase.HashToUnit(x / pitCell, y / pitCell, seed + 1439);
                if (pitting > 0.05f && pit > 0.94f - pitting * 0.2f && normalized > 0.36f)
                    index = 0;
                else if (flakeSize > 0.05f && flakes > 0.82f - flakeSize * 0.16f)
                    index = 5;
            }

            if (result == null)
            {
                tile.Set(x, y, index);
                continue;
            }

            if (index <= 2)
            {
                var source = Sample(baseSurface!, x, y);
                var adjusted = AdjustChannels(source.R, source.G, source.B, brightness, contrast, invert);
                result.SetPixel(x, y, adjusted.R, adjusted.G, adjusted.B, source.A);
            }
            else
            {
                var color = palette[index];
                var sourceAlpha = Sample(baseSurface!, x, y).A;
                result.SetPixel(x, y, color.R / 255f, color.G / 255f, color.B / 255f, sourceAlpha);
            }
        }
        return result ?? tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderCircuit(int size, int seed, float density, int traceWidth,
        float roundness, Color boardColor, Color traceColor, Color padColor, bool invert)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(boardColor, -0.2f)),
            Adjust(PixelMaterialUtility.Shade(boardColor, 0.08f)),
            Adjust(PixelMaterialUtility.Shade(traceColor, -0.52f)),
            Adjust(traceColor),
            Adjust(PixelMaterialUtility.Shade(traceColor, 0.28f)),
            Adjust(PixelMaterialUtility.Shade(padColor, 0.18f))
        };
        var tile = new PixelTileCanvas(size, 0);
        var boardBlock = Math.Max(3, size / 10);
        for (var by = 0; by < size; by += boardBlock)
        for (var bx = 0; bx < size; bx += boardBlock)
        {
            if (GraphNodeBase.HashToUnit(bx / boardBlock, by / boardBlock, seed + 1501) < 0.68f)
                continue;
            var width = Math.Max(1, boardBlock / 2);
            var height = Math.Max(1, boardBlock / 3);
            for (var oy = 0; oy < height; oy++)
            for (var ox = 0; ox < width; ox++)
                tile.SetWrapped(bx + ox, by + oy, 1);
        }

        var grid = Math.Max(4, size / 8);
        var routeCount = Math.Clamp((int)MathF.Round(4 + density * 11), 4, 18);
        var widthPixels = Math.Clamp(traceWidth, 1, Math.Max(1, size / 8));
        var segments = new List<(int X0, int Y0, int X1, int Y1)>();
        var pads = new List<(int X, int Y)>();
        for (var route = 0; route < routeCount; route++)
        {
            var startX = SnapToGrid((int)(GraphNodeBase.HashToUnit(route, seed, 1511) * size), grid) + grid / 2;
            var startY = SnapToGrid((int)(GraphNodeBase.HashToUnit(route, seed, 1517) * size), grid) + grid / 2;
            var horizontalCells = 1 + (int)(GraphNodeBase.HashToUnit(route, seed, 1523) * 3);
            var verticalCells = 1 + (int)(GraphNodeBase.HashToUnit(route, seed, 1531) * 3);
            var signX = GraphNodeBase.HashToUnit(route, seed, 1543) < 0.5f ? -1 : 1;
            var signY = GraphNodeBase.HashToUnit(route, seed, 1549) < 0.5f ? -1 : 1;
            var bendX = startX + signX * horizontalCells * grid;
            var endY = startY + signY * verticalCells * grid;
            var verticalFirst = (route & 1) == 1;

            if (verticalFirst)
            {
                AddCircuitCorner(segments, startX, startY, startX, endY, bendX, endY, roundness);
                pads.Add((startX, startY));
                pads.Add((bendX, endY));
            }
            else
            {
                AddCircuitCorner(segments, startX, startY, bendX, startY, bendX, endY, roundness);
                pads.Add((startX, startY));
                pads.Add((bendX, endY));
            }
        }

        foreach (var segment in segments)
            tile.DrawLineWrapped(segment.X0, segment.Y0, segment.X1, segment.Y1, 2, widthPixels + 2);
        foreach (var segment in segments)
            tile.DrawLineWrapped(segment.X0, segment.Y0, segment.X1, segment.Y1, 3, widthPixels);
        if (widthPixels > 1)
        {
            foreach (var segment in segments)
                tile.DrawLineWrapped(segment.X0, segment.Y0 - 1, segment.X1, segment.Y1 - 1, 4, 1);
        }

        var padRadius = Math.Max(1, widthPixels);
        foreach (var pad in pads)
        {
            for (var oy = -padRadius - 1; oy <= padRadius + 1; oy++)
            for (var ox = -padRadius - 1; ox <= padRadius + 1; ox++)
                tile.SetWrapped(pad.X + ox, pad.Y + oy, 2);
            for (var oy = -padRadius; oy <= padRadius; oy++)
            for (var ox = -padRadius; ox <= padRadius; ox++)
                if (Math.Abs(ox) + Math.Abs(oy) <= padRadius + 1)
                    tile.SetWrapped(pad.X + ox, pad.Y + oy, 5);
            tile.SetWrapped(pad.X, pad.Y, 0);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderHoneycomb(int size, int seed, float scale, float wallThickness,
        float bevel, Color innerColor, Color wallColor, bool invert)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(wallColor, -0.24f)),
            Adjust(PixelMaterialUtility.Shade(wallColor, 0.2f)),
            Adjust(PixelMaterialUtility.Shade(innerColor, -0.18f)),
            Adjust(innerColor),
            Adjust(PixelMaterialUtility.Shade(innerColor, 0.12f))
        };
        var tile = new PixelTileCanvas(size, 3);
        var desiredWidth = Math.Clamp(size * scale, 5f, size * 0.45f);
        var idealColumns = Math.Max(2, (int)MathF.Round(size / desiredWidth));
        var idealRows = idealColumns * 1.1547005f;
        var rows = Math.Max(2, (int)MathF.Round(idealRows / 2f) * 2);
        var columns = Math.Max(2, (int)MathF.Round(rows / 1.1547005f));
        var cellWidth = size / (float)columns;
        var rowSpacing = size / (float)rows;
        var wallPixels = Math.Max(0.55f, wallThickness * MathF.Min(cellWidth, rowSpacing) * 0.9f);
        var bevelPixels = Math.Max(0.35f, bevel * wallPixels * 1.8f);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            FindHexCell(x + 0.5f, y + 0.5f, size, columns, rows, cellWidth, rowSpacing,
                out var nearest, out var second, out var cellX, out var cellY, out var localX, out var localY);
            var edge = second - nearest;
            byte index;
            if (edge < wallPixels)
                index = localX + localY < 0f ? (byte)1 : (byte)0;
            else if (edge < wallPixels + bevelPixels)
                index = localX + localY < 0f ? (byte)4 : (byte)2;
            else
            {
                var tone = GraphNodeBase.HashToUnit(cellX, cellY, seed + 1601);
                index = tone < 0.28f ? (byte)2 : tone > 0.76f ? (byte)4 : (byte)3;
            }
            tile.Set(x, y, index);
        }
        return tile.ToPixelBuffer(palette);
    }

    public static PixelBuffer RenderFence(int size, int seed, int slatCount, float slatWidth,
        float slatHeight, string topShape, int railCount, Color woodColor, Color darkWood,
        float variation, float wear, float grain, float brightness, float contrast, bool invert)
    {
        Color Adjust(Color color) => PixelMaterialUtility.Adjust(color, brightness, contrast, invert);
        var outline = Adjust(PixelMaterialUtility.Shade(darkWood, -0.38f));
        var railColor = Adjust(PixelMaterialUtility.Mix(darkWood, woodColor, 0.36f));
        var grainColor = Adjust(PixelMaterialUtility.Shade(darkWood, -0.08f));
        var highlightColor = Adjust(PixelMaterialUtility.Shade(woodColor, 0.24f));
        var nailColor = Adjust(Color.FromRgb(184, 192, 192));
        var canvas = new PixelSpriteCanvas(size, size);
        var cellWidth = size / (float)slatCount;
        var bottom = size - Math.Max(1, size / 32);
        var topBase = Math.Clamp((int)MathF.Round(size * (1f - slatHeight)), 1, size - 5);
        var railThickness = Math.Max(2, size / 18);

        var railYs = new int[railCount];
        for (var i = 0; i < railCount; i++)
        {
            railYs[i] = topBase + (int)MathF.Round((i + 1f) / (railCount + 1f) * (bottom - topBase));
            var railMask = canvas.CreateMask();
            canvas.AddRectangle(railMask, 0, railYs[i] - railThickness / 2,
                size - 1, railYs[i] + railThickness / 2);
            canvas.PaintOutline(railMask, outline, 1);
            canvas.Paint(railMask, railColor);
        }

        for (var slat = 0; slat < slatCount; slat++)
        {
            var center = (int)MathF.Round((slat + 0.5f) * cellWidth);
            var halfWidth = Math.Max(1, (int)MathF.Round(cellWidth * slatWidth * 0.5f));
            var left = Math.Max(0, center - halfWidth);
            var right = Math.Min(size - 1, center + halfWidth);
            var jitter = (int)MathF.Round((GraphNodeBase.HashToUnit(slat, seed, 1709) - 0.5f) *
                                          Math.Max(1f, size / 32f));
            var top = Math.Clamp(topBase + jitter, 0, bottom - 3);
            var tipHeight = Math.Max(2, Math.Min(halfWidth, size / 10));
            var slatMask = canvas.CreateMask();
            switch (topShape)
            {
                case "pointed":
                    canvas.AddPolygon(slatMask,
                        new SpritePoint(center, top), new SpritePoint(right, top + tipHeight),
                        new SpritePoint(right, bottom), new SpritePoint(left, bottom),
                        new SpritePoint(left, top + tipHeight));
                    break;
                case "rounded":
                    canvas.AddEllipse(slatMask, center, top + tipHeight, halfWidth, tipHeight);
                    canvas.AddRectangle(slatMask, left, top + tipHeight, right, bottom);
                    break;
                case "vShape":
                    canvas.AddPolygon(slatMask,
                        new SpritePoint(left, top), new SpritePoint(center, top + tipHeight),
                        new SpritePoint(right, top), new SpritePoint(right, bottom),
                        new SpritePoint(left, bottom));
                    break;
                default:
                    canvas.AddRectangle(slatMask, left, top, right, bottom);
                    break;
            }

            if (wear > 0.08f)
            {
                var chipCount = Math.Clamp((int)MathF.Round(wear * 3f), 0, 3);
                for (var chip = 0; chip < chipCount; chip++)
                {
                    if (GraphNodeBase.HashToUnit(slat, chip, seed + 1721) > wear)
                        continue;
                    var chipY = top + tipHeight + (int)(GraphNodeBase.HashToUnit(slat, chip, seed + 1723) *
                                                       Math.Max(1, bottom - top - tipHeight));
                    var chipX = GraphNodeBase.HashToUnit(slat, chip, seed + 1733) < 0.5f ? left : right;
                    var chipMask = canvas.CreateMask();
                    canvas.AddRectangle(chipMask, chipX - 1, chipY, chipX + 1, chipY + Math.Max(1, size / 32));
                    PixelSpriteCanvas.Subtract(slatMask, chipMask);
                }
            }

            var tone = GraphNodeBase.HashToUnit(slat, seed, 1741);
            var bodyColor = tone < 0.34f
                ? PixelMaterialUtility.Mix(darkWood, woodColor, 0.62f - variation * 0.25f)
                : tone > 0.72f ? PixelMaterialUtility.Shade(woodColor, variation * 0.28f) : woodColor;
            canvas.PaintOutline(slatMask, outline, 1);
            canvas.Paint(slatMask, Adjust(bodyColor));

            var highlightMask = canvas.CreateMask();
            canvas.AddLine(highlightMask, Math.Min(right, left + 1), top + tipHeight,
                Math.Min(right, left + 1), bottom - 1, 1);
            canvas.Paint(PixelSpriteCanvas.Intersect(highlightMask, slatMask), highlightColor);

            if (grain > 0.08f && right - left >= 3)
            {
                var grainMask = canvas.CreateMask();
                var grainX = Math.Clamp(center + (GraphNodeBase.HashToUnit(slat, seed, 1753) < 0.5f ? -1 : 1), left, right);
                var segmentLength = Math.Max(2, size / 12);
                for (var gy = top + tipHeight + 2; gy < bottom - 1; gy += segmentLength * 2)
                    canvas.AddLine(grainMask, grainX, gy, grainX, Math.Min(bottom - 1, gy + segmentLength), 1);
                canvas.Paint(PixelSpriteCanvas.Intersect(grainMask, slatMask), grainColor);
            }

            foreach (var railY in railYs)
            {
                var nailMask = canvas.CreateMask();
                canvas.AddRectangle(nailMask, center, railY, center, railY);
                canvas.Paint(PixelSpriteCanvas.Intersect(nailMask, slatMask), nailColor);
            }
        }
        return canvas.ToPixelBuffer();
    }

    private static int Snap(int value, int unit)
        => (int)MathF.Round(value / (float)Math.Max(1, unit)) * Math.Max(1, unit);

    private static void AddWrappedLine(PixelSpriteCanvas canvas, bool[] mask,
        int x0, int y0, int x1, int y1, int thickness, int size)
    {
        for (var offsetY = -size; offsetY <= size; offsetY += size)
        for (var offsetX = -size; offsetX <= size; offsetX += size)
            canvas.AddLine(mask, x0 + offsetX, y0 + offsetY, x1 + offsetX, y1 + offsetY, thickness);
    }

    private static (float R, float G, float B, float A) Sample(PixelBuffer source, int x, int y)
        => source.GetPixel(GraphNodeBase.Mod(x, source.Width), GraphNodeBase.Mod(y, source.Height));

    private static float Luminance(float r, float g, float b) => r * 0.299f + g * 0.587f + b * 0.114f;

    private static (float R, float G, float B) AdjustChannels(float r, float g, float b,
        float brightness, float contrast, bool invert)
    {
        float Channel(float value)
        {
            value = Math.Clamp((value - 0.5f) * (1f + contrast) + 0.5f + brightness, 0f, 1f);
            return invert ? 1f - value : value;
        }
        return (Channel(r), Channel(g), Channel(b));
    }

    private static int SnapToGrid(int value, int grid) => value / Math.Max(1, grid) * Math.Max(1, grid);

    private static void AddCircuitCorner(List<(int X0, int Y0, int X1, int Y1)> segments,
        int startX, int startY, int bendX, int bendY, int endX, int endY, float roundness)
    {
        if (roundness < 0.38f || startX == bendX && bendX == endX || startY == bendY && bendY == endY)
        {
            segments.Add((startX, startY, bendX, bendY));
            segments.Add((bendX, bendY, endX, endY));
            return;
        }

        var beforeX = bendX - Math.Sign(bendX - startX);
        var beforeY = bendY - Math.Sign(bendY - startY);
        var afterX = bendX + Math.Sign(endX - bendX);
        var afterY = bendY + Math.Sign(endY - bendY);
        segments.Add((startX, startY, beforeX, beforeY));
        segments.Add((beforeX, beforeY, afterX, afterY));
        segments.Add((afterX, afterY, endX, endY));
    }

    private static void FindHexCell(float x, float y, int size, int columns, int rows,
        float cellWidth, float rowSpacing, out float nearest, out float second,
        out int cellX, out int cellY, out float localX, out float localY)
    {
        nearest = float.MaxValue;
        second = float.MaxValue;
        cellX = cellY = 0;
        localX = localY = 0f;
        var baseRow = (int)MathF.Floor(y / rowSpacing);
        for (var row = baseRow - 2; row <= baseRow + 2; row++)
        {
            var offset = (row & 1) == 0 ? 0f : 0.5f;
            var baseColumn = (int)MathF.Floor(x / cellWidth - offset);
            for (var column = baseColumn - 2; column <= baseColumn + 2; column++)
            {
                var centerX = (column + 0.5f + offset) * cellWidth;
                var centerY = (row + 0.5f) * rowSpacing;
                var dx = x - centerX;
                var dy = y - centerY;
                dx -= MathF.Round(dx / size) * size;
                dy -= MathF.Round(dy / size) * size;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance < nearest)
                {
                    second = nearest;
                    nearest = distance;
                    cellX = GraphNodeBase.Mod(column, columns);
                    cellY = GraphNodeBase.Mod(row, rows);
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

public sealed class PixelRainNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("time", 0, 0, 1, 0.01, "时间"),
        NodeParameterDefinition.Number("density", 0.6, 0.1, 1, 0.01, "密度"),
        NodeParameterDefinition.Number("speed", 0.8, 0.1, 3, 0.1, "速度"),
        NodeParameterDefinition.Number("wind", 0, -1, 1, 0.1, "风力"),
        NodeParameterDefinition.Integer("length", 6, 2, 16, 1, "长度"),
        NodeParameterDefinition.Integer("pixelSize", 1, 1, 4, 1, "像素大小")
    };

    public override string TypeName => "Rain";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderRain(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetFloat(parameters, "density", 0.6f), 0.1f, 1f),
        Math.Clamp(GetFloat(parameters, "speed", 0.8f), 0.1f, 3f),
        Math.Clamp(GetFloat(parameters, "wind", 0f), -1f, 1f),
        Math.Clamp(GetInt(parameters, "length", 6), 2, 16),
        Math.Clamp(GetInt(parameters, "pixelSize", 1), 1, 4),
        context.AnimationTime ?? Math.Clamp(GetFloat(parameters, "time", 0f), 0f, 1f));
}

public sealed class PixelMarbleNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("scale", 3, 0.5, 10, 0.1, "缩放"),
        NodeParameterDefinition.Number("veinFrequency", 4, 1, 12, 0.1, "脉纹频率"),
        NodeParameterDefinition.Number("veinSharpness", 0.5, 0, 1, 0.01, "脉纹锐度"),
        NodeParameterDefinition.Color("veinColor", Color.FromRgb(74, 80, 88), "脉纹颜色"),
        NodeParameterDefinition.Color("bgColor", Color.FromRgb(237, 232, 224), "底色"),
        NodeParameterDefinition.Number("distortion", 2.5, 0, 5, 0.1, "扭曲"),
        NodeParameterDefinition.Integer("octaves", 4, 1, 8, 1, "层数"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Marble";
    public override string Category => "Noise";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderMarble(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetFloat(parameters, "scale", 3f), 0.5f, 10f),
        Math.Clamp(GetFloat(parameters, "veinFrequency", 4f), 1f, 12f),
        Math.Clamp(GetFloat(parameters, "veinSharpness", 0.5f), 0f, 1f),
        GetColor(parameters, "veinColor", Color.FromRgb(74, 80, 88)),
        GetColor(parameters, "bgColor", Color.FromRgb(237, 232, 224)),
        Math.Clamp(GetFloat(parameters, "distortion", 2.5f), 0f, 5f),
        Math.Clamp(GetInt(parameters, "octaves", 4), 1, 8), GetBool(parameters, "invert", false));
}

public sealed class PixelScalesNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("rows", 8, 3, 20, 1, "行数"),
        NodeParameterDefinition.Integer("cols", 8, 3, 20, 1, "列数"),
        NodeParameterDefinition.Number("overlap", 0.3, 0, 0.8, 0.01, "重叠"),
        NodeParameterDefinition.Number("roundness", 0.6, 0.1, 1, 0.01, "圆润度"),
        NodeParameterDefinition.Color("scaleColor", Color.FromRgb(74, 156, 90), "鳞片颜色"),
        NodeParameterDefinition.Color("gapColor", Color.FromRgb(20, 40, 24), "缝隙颜色"),
        NodeParameterDefinition.Number("highlight", 0.5, 0, 1, 0.01, "高光"),
        NodeParameterDefinition.Number("shadow", 0.35, 0, 1, 0.01, "阴影"),
        NodeParameterDefinition.Number("colorVariation", 0.3, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Scales";
    public override string Category => "Noise";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderScales(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetInt(parameters, "rows", 8), 3, 20), Math.Clamp(GetInt(parameters, "cols", 8), 3, 20),
        Math.Clamp(GetFloat(parameters, "overlap", 0.3f), 0f, 0.8f),
        Math.Clamp(GetFloat(parameters, "roundness", 0.6f), 0.1f, 1f),
        GetColor(parameters, "scaleColor", Color.FromRgb(74, 156, 90)),
        GetColor(parameters, "gapColor", Color.FromRgb(20, 40, 24)),
        Math.Clamp(GetFloat(parameters, "highlight", 0.5f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "shadow", 0.35f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "colorVariation", 0.3f), 0f, 0.5f),
        GetBool(parameters, "invert", false));
}

public sealed class PixelRustNode : PixelMaterialNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("基础表面", GraphPortType.Image, "baseSurface") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("rustAmount", 0.5, 0, 1, 0.01, "锈蚀量"),
        NodeParameterDefinition.Number("scale", 3, 0.5, 10, 0.1, "缩放"),
        NodeParameterDefinition.Number("edgeCorrosion", 0.5, 0, 1, 0.01, "边缘腐蚀"),
        NodeParameterDefinition.Choice("corrosionType", "ironRust", ["ironRust", "patina", "mixed"],
            ["铁锈", "铜绿", "混合"], "腐蚀类型"),
        NodeParameterDefinition.Color("orangeRust", Color.FromRgb(180, 100, 40), "橙色铁锈"),
        NodeParameterDefinition.Color("brownRust", Color.FromRgb(120, 60, 30), "棕色铁锈"),
        NodeParameterDefinition.Color("patinaColor", Color.FromRgb(70, 140, 110), "铜绿颜色"),
        NodeParameterDefinition.Number("flakeSize", 0.5, 0, 1, 0.01, "剥落大小"),
        NodeParameterDefinition.Number("pitting", 0.3, 0, 1, 0.01, "坑蚀"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0.2, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Rust";
    public override string Category => "Noise";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderRust(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetFloat(parameters, "rustAmount", 0.5f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "scale", 3f), 0.5f, 10f),
        Math.Clamp(GetFloat(parameters, "edgeCorrosion", 0.5f), 0f, 1f),
        GetChoice(parameters, "corrosionType", "ironRust"),
        GetColor(parameters, "orangeRust", Color.FromRgb(180, 100, 40)),
        GetColor(parameters, "brownRust", Color.FromRgb(120, 60, 30)),
        GetColor(parameters, "patinaColor", Color.FromRgb(70, 140, 110)),
        Math.Clamp(GetFloat(parameters, "flakeSize", 0.5f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "pitting", 0.3f), 0f, 1f),
        GetFloat(parameters, "brightness", 0f), GetFloat(parameters, "contrast", 0.2f),
        GetBool(parameters, "invert", false), inputs.Length > 0 ? inputs[0] : null);
}

public sealed class PixelCircuitNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("traceDensity", 0.5, 0.1, 1, 0.01, "走线密度"),
        NodeParameterDefinition.Integer("traceWidth", 2, 1, 6, 1, "走线宽度"),
        NodeParameterDefinition.Number("roundness", 0.5, 0, 1, 0.01, "拐角圆润度"),
        NodeParameterDefinition.Color("boardColor", Color.FromRgb(30, 50, 20), "电路板颜色"),
        NodeParameterDefinition.Color("traceColor", Color.FromRgb(200, 180, 100), "走线颜色"),
        NodeParameterDefinition.Color("padColor", Color.FromRgb(180, 160, 80), "焊盘颜色"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Circuit";
    public override string Category => "Pattern";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderCircuit(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetFloat(parameters, "traceDensity", 0.5f), 0.1f, 1f),
        Math.Clamp(GetInt(parameters, "traceWidth", 2), 1, 6),
        Math.Clamp(GetFloat(parameters, "roundness", 0.5f), 0f, 1f),
        GetColor(parameters, "boardColor", Color.FromRgb(30, 50, 20)),
        GetColor(parameters, "traceColor", Color.FromRgb(200, 180, 100)),
        GetColor(parameters, "padColor", Color.FromRgb(180, 160, 80)),
        GetBool(parameters, "invert", false));
}

public sealed class PixelHoneycombNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("scale", 0.18, 0.05, 0.5, 0.01, "缩放"),
        NodeParameterDefinition.Number("wallThickness", 0.15, 0.02, 0.5, 0.01, "壁厚"),
        NodeParameterDefinition.Number("bevel", 0.3, 0, 1, 0.01, "斜面"),
        NodeParameterDefinition.Color("innerColor", Color.FromRgb(218, 166, 62), "内部颜色"),
        NodeParameterDefinition.Color("wallColor", Color.FromRgb(92, 54, 24), "蜂窝壁颜色"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Honeycomb";
    public override string Category => "Pattern";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderHoneycomb(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetFloat(parameters, "scale", 0.18f), 0.05f, 0.5f),
        Math.Clamp(GetFloat(parameters, "wallThickness", 0.15f), 0.02f, 0.5f),
        Math.Clamp(GetFloat(parameters, "bevel", 0.3f), 0f, 1f),
        GetColor(parameters, "innerColor", Color.FromRgb(218, 166, 62)),
        GetColor(parameters, "wallColor", Color.FromRgb(92, 54, 24)),
        GetBool(parameters, "invert", false));
}

public sealed class PixelFenceNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Integer("slatCount", 6, 3, 16, 1, "板条数量"),
        NodeParameterDefinition.Number("slatWidth", 0.65, 0.3, 0.95, 0.01, "板条宽度"),
        NodeParameterDefinition.Number("slatHeight", 0.9, 0.3, 1, 0.01, "板条高度"),
        NodeParameterDefinition.Choice("topShape", "pointed", ["flat", "pointed", "rounded", "vShape"],
            ["平顶", "尖顶", "圆顶", "V形"], "顶部形状"),
        NodeParameterDefinition.Integer("railCount", 2, 0, 3, 1, "横栏数量"),
        NodeParameterDefinition.Color("woodColor", Color.FromRgb(158, 108, 62), "木材颜色"),
        NodeParameterDefinition.Color("darkWoodColor", Color.FromRgb(82, 50, 28), "深色木材"),
        NodeParameterDefinition.Number("colorVariation", 0.2, 0, 0.5, 0.01, "颜色变化"),
        NodeParameterDefinition.Number("wearAmount", 0.15, 0, 1, 0.01, "磨损程度"),
        NodeParameterDefinition.Number("woodGrain", 0.6, 0, 1, 0.01, "木纹"),
        NodeParameterDefinition.Number("brightness", 0, -0.3, 0.3, 0.01, "亮度"),
        NodeParameterDefinition.Number("contrast", 0, -0.5, 0.5, 0.01, "对比度"),
        NodeParameterDefinition.Boolean("invert", false, "反相")
    };

    public override string TypeName => "Fence";
    public override string Category => "Pattern";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context) => PixelPatternSurfaceRenderer.RenderFence(
        context.GetEffectiveSize(), GetInt(parameters, "seed", context.Seed),
        Math.Clamp(GetInt(parameters, "slatCount", 6), 3, 16),
        Math.Clamp(GetFloat(parameters, "slatWidth", 0.65f), 0.3f, 0.95f),
        Math.Clamp(GetFloat(parameters, "slatHeight", 0.9f), 0.3f, 1f),
        GetChoice(parameters, "topShape", "pointed"),
        Math.Clamp(GetInt(parameters, "railCount", 2), 0, 3),
        GetColor(parameters, "woodColor", Color.FromRgb(158, 108, 62)),
        GetColor(parameters, "darkWoodColor", Color.FromRgb(82, 50, 28)),
        Math.Clamp(GetFloat(parameters, "colorVariation", 0.2f), 0f, 0.5f),
        Math.Clamp(GetFloat(parameters, "wearAmount", 0.15f), 0f, 1f),
        Math.Clamp(GetFloat(parameters, "woodGrain", 0.6f), 0f, 1f),
        GetFloat(parameters, "brightness", 0f), GetFloat(parameters, "contrast", 0f),
        GetBool(parameters, "invert", false));
}
