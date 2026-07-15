using System;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services.AiImage;

/// <summary>
/// Builds low-resolution structural guides for subjects that diffusion models
/// commonly fail to compose from text alone. The guide is consumed through the
/// existing img2img path and never replaces the generated texture or palette.
/// </summary>
internal static class AiCompositionGuideFactory
{
    public static PixelBuffer? TryCreate(string prompt, string viewAngle, int outputSize)
    {
        if (!PixelArtPromptComposer.IsBuildingPrompt(prompt))
            return null;

        var size = Math.Clamp(outputSize * 2, 64, 128);
        var image = PixelBufferPool.Borrow(size, size);
        Fill(image, 0.72f, 0.70f, 0.78f, 1f);

        switch (viewAngle)
        {
            case "topDown": DrawTopDownBuilding(image); break;
            case "front": DrawFrontBuilding(image, elevated: false); break;
            case "frontHigh": DrawFrontBuilding(image, elevated: true); break;
            case "side": DrawSideBuilding(image); break;
            default: DrawIsometricBuilding(image); break;
        }
        return image;
    }

    private static void DrawIsometricBuilding(PixelBuffer image)
    {
        var s = image.Width / 64f;
        var left = Scale(13, s); var right = Scale(43, s);
        var roofY = Scale(17, s); var frontTop = Scale(24, s); var bottom = Scale(55, s);
        var depth = Scale(10, s);
        var outline = (0.10f, 0.12f, 0.19f);

        FillPolygon(image, [(left, frontTop), (right, frontTop), (right, bottom), (left, bottom)], 0.43f, 0.50f, 0.62f);
        FillPolygon(image, [(right, frontTop), (right + depth, roofY), (right + depth, bottom - Scale(7, s)), (right, bottom)], 0.29f, 0.36f, 0.49f);
        FillPolygon(image, [(left, frontTop), (right, frontTop), (right + depth, roofY), (left + depth, roofY)], 0.56f, 0.61f, 0.70f);
        DrawPolygon(image, [(left, frontTop), (right, frontTop), (right, bottom), (left, bottom)], outline);
        DrawPolygon(image, [(right, frontTop), (right + depth, roofY), (right + depth, bottom - Scale(7, s)), (right, bottom)], outline);
        DrawPolygon(image, [(left, frontTop), (right, frontTop), (right + depth, roofY), (left + depth, roofY)], outline);

        DrawWindowGrid(image, left + Scale(4, s), frontTop + Scale(5, s), right - Scale(4, s), bottom - Scale(10, s), 3, 4);
        for (var row = 0; row < 4; row++)
        {
            var y = frontTop + Scale(5 + row * 7, s);
            FillRect(image, right + Scale(3, s), y, right + depth - Scale(2, s), y + Scale(3, s), 0.16f, 0.35f, 0.48f);
        }
        DrawEntrance(image, (left + right) / 2, bottom, s);
    }

    private static void DrawFrontBuilding(PixelBuffer image, bool elevated)
    {
        var s = image.Width / 64f;
        var left = Scale(14, s); var right = Scale(50, s);
        var top = Scale(elevated ? 20 : 15, s); var bottom = Scale(56, s);
        FillRect(image, left, top, right, bottom, 0.43f, 0.50f, 0.62f);
        DrawRect(image, left, top, right, bottom, 0.10f, 0.12f, 0.19f);
        if (elevated)
        {
            FillPolygon(image, [(left, top), (right, top), (right - Scale(6, s), Scale(13, s)), (left + Scale(6, s), Scale(13, s))], 0.56f, 0.61f, 0.70f);
            DrawPolygon(image, [(left, top), (right, top), (right - Scale(6, s), Scale(13, s)), (left + Scale(6, s), Scale(13, s))], (0.10f, 0.12f, 0.19f));
        }
        DrawWindowGrid(image, left + Scale(4, s), top + Scale(5, s), right - Scale(4, s), bottom - Scale(10, s), 4, 4);
        DrawEntrance(image, (left + right) / 2, bottom, s);
    }

    private static void DrawTopDownBuilding(PixelBuffer image)
    {
        var s = image.Width / 64f;
        var left = Scale(12, s); var right = Scale(52, s);
        var top = Scale(15, s); var bottom = Scale(49, s);
        FillRect(image, left, top, right, bottom, 0.52f, 0.57f, 0.66f);
        DrawRect(image, left, top, right, bottom, 0.10f, 0.12f, 0.19f);
        FillRect(image, Scale(24, s), Scale(25, s), Scale(40, s), Scale(37, s), 0.31f, 0.37f, 0.48f);
        DrawRect(image, Scale(24, s), Scale(25, s), Scale(40, s), Scale(37, s), 0.13f, 0.16f, 0.23f);
        FillRect(image, Scale(28, s), Scale(29, s), Scale(36, s), Scale(33, s), 0.17f, 0.32f, 0.43f);
    }

    private static void DrawSideBuilding(PixelBuffer image)
    {
        var s = image.Width / 64f;
        var left = Scale(10, s); var right = Scale(54, s);
        var top = Scale(16, s); var bottom = Scale(56, s);
        FillRect(image, left, top, right, bottom, 0.35f, 0.43f, 0.56f);
        DrawRect(image, left, top, right, bottom, 0.10f, 0.12f, 0.19f);
        DrawWindowGrid(image, left + Scale(4, s), top + Scale(5, s), right - Scale(4, s), bottom - Scale(7, s), 5, 4);
    }

    private static void DrawWindowGrid(PixelBuffer image, int left, int top, int right, int bottom, int columns, int rows)
    {
        var cellWidth = Math.Max(2, (right - left) / columns);
        var cellHeight = Math.Max(2, (bottom - top) / rows);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x0 = left + column * cellWidth + Math.Max(1, cellWidth / 5);
            var y0 = top + row * cellHeight + Math.Max(1, cellHeight / 5);
            var x1 = left + (column + 1) * cellWidth - Math.Max(1, cellWidth / 5);
            var y1 = top + (row + 1) * cellHeight - Math.Max(1, cellHeight / 4);
            FillRect(image, x0, y0, x1, y1, 0.13f, 0.33f, 0.48f);
            if ((row + column) % 3 == 0)
                FillRect(image, x0, y0, x1, y0, 0.43f, 0.71f, 0.78f);
        }
    }

    private static void DrawEntrance(PixelBuffer image, int centerX, int bottom, float scale)
    {
        var halfWidth = Scale(4, scale);
        FillRect(image, centerX - halfWidth, bottom - Scale(10, scale), centerX + halfWidth, bottom, 0.055f, 0.10f, 0.16f);
        FillRect(image, centerX, bottom - Scale(9, scale), centerX, bottom - Scale(1, scale), 0.55f, 0.76f, 0.82f);
    }

    private static int Scale(int value, float scale) => Math.Max(1, (int)MathF.Round(value * scale));

    private static void Fill(PixelBuffer image, float r, float g, float b, float a)
    {
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            image.SetPixel(x, y, r, g, b, a);
    }

    private static void FillRect(PixelBuffer image, int left, int top, int right, int bottom, float r, float g, float b)
    {
        for (var y = Math.Max(0, top); y <= Math.Min(image.Height - 1, bottom); y++)
        for (var x = Math.Max(0, left); x <= Math.Min(image.Width - 1, right); x++)
            image.SetPixel(x, y, r, g, b, 1f);
    }

    private static void DrawRect(PixelBuffer image, int left, int top, int right, int bottom, float r, float g, float b)
    {
        DrawLine(image, left, top, right, top, r, g, b);
        DrawLine(image, right, top, right, bottom, r, g, b);
        DrawLine(image, right, bottom, left, bottom, r, g, b);
        DrawLine(image, left, bottom, left, top, r, g, b);
    }

    private static void FillPolygon(PixelBuffer image, (int X, int Y)[] points, float r, float g, float b)
    {
        var minX = Math.Max(0, points.Min(point => point.X));
        var maxX = Math.Min(image.Width - 1, points.Max(point => point.X));
        var minY = Math.Max(0, points.Min(point => point.Y));
        var maxY = Math.Min(image.Height - 1, points.Max(point => point.Y));
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
            if (Contains(points, x + 0.5, y + 0.5))
                image.SetPixel(x, y, r, g, b, 1f);
    }

    private static bool Contains((int X, int Y)[] points, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < points.Length; i++)
        {
            var j = (i + points.Length - 1) % points.Length;
            if ((points[i].Y > y) == (points[j].Y > y)) continue;
            var crossingX = (points[j].X - points[i].X) * (y - points[i].Y)
                / (points[j].Y - points[i].Y) + points[i].X;
            if (x < crossingX) inside = !inside;
        }
        return inside;
    }

    private static void DrawPolygon(PixelBuffer image, (int X, int Y)[] points, (float R, float G, float B) color)
    {
        for (var i = 0; i < points.Length; i++)
        {
            var next = (i + 1) % points.Length;
            DrawLine(image, points[i].X, points[i].Y, points[next].X, points[next].Y, color.R, color.G, color.B);
        }
    }

    private static void DrawLine(PixelBuffer image, int x0, int y0, int x1, int y1, float r, float g, float b)
    {
        var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < image.Width && y0 >= 0 && y0 < image.Height)
                image.SetPixel(x0, y0, r, g, b, 1f);
            if (x0 == x1 && y0 == y1) break;
            var twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }
}
