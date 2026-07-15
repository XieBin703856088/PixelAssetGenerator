using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.PixelArt;

/// <summary>
/// Small hard-edge rasterizer for game sprites. It deliberately works with binary masks:
/// silhouettes, outlines and light clusters never introduce anti-aliased edge pixels.
/// </summary>
internal sealed class PixelSpriteCanvas
{
    private readonly int[] _pixels;

    public PixelSpriteCanvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = new int[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public bool[] CreateMask() => new bool[Width * Height];

    public void AddRectangle(bool[] mask, int left, int top, int right, int bottom)
    {
        ValidateMask(mask);
        left = Math.Clamp(left, 0, Width - 1);
        right = Math.Clamp(right, 0, Width - 1);
        top = Math.Clamp(top, 0, Height - 1);
        bottom = Math.Clamp(bottom, 0, Height - 1);
        if (left > right || top > bottom)
            return;

        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            mask[y * Width + x] = true;
    }

    public void AddEllipse(bool[] mask, int centerX, int centerY, int radiusX, int radiusY)
    {
        ValidateMask(mask);
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        var left = Math.Max(0, centerX - radiusX);
        var right = Math.Min(Width - 1, centerX + radiusX);
        var top = Math.Max(0, centerY - radiusY);
        var bottom = Math.Min(Height - 1, centerY + radiusY);
        var radiusXSquared = radiusX * radiusX;
        var radiusYSquared = radiusY * radiusY;
        var limit = (long)radiusXSquared * radiusYSquared;

        for (var y = top; y <= bottom; y++)
        {
            var dy = y - centerY;
            for (var x = left; x <= right; x++)
            {
                var dx = x - centerX;
                if ((long)dx * dx * radiusYSquared + (long)dy * dy * radiusXSquared <= limit)
                    mask[y * Width + x] = true;
            }
        }
    }

    public void AddPolygon(bool[] mask, params SpritePoint[] points)
    {
        ValidateMask(mask);
        if (points.Length < 3)
            return;

        var minX = Math.Clamp(points.Min(point => point.X), 0, Width - 1);
        var maxX = Math.Clamp(points.Max(point => point.X), 0, Width - 1);
        var minY = Math.Clamp(points.Min(point => point.Y), 0, Height - 1);
        var maxY = Math.Clamp(points.Max(point => point.Y), 0, Height - 1);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (Contains(points, x + 0.5f, y + 0.5f))
                    mask[y * Width + x] = true;
            }
        }
    }

    public void AddLine(bool[] mask, int x0, int y0, int x1, int y1, int thickness)
    {
        ValidateMask(mask);
        thickness = Math.Max(1, thickness);
        var radius = Math.Max(0, (thickness - 1) / 2);
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            StampSquare(mask, x0, y0, radius, thickness % 2 == 0);
            if (x0 == x1 && y0 == y1)
                break;
            var doubled = error * 2;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    public void Paint(bool[] mask, Color color)
    {
        ValidateMask(mask);
        var packed = Pack(color);
        for (var i = 0; i < mask.Length; i++)
            if (mask[i])
                _pixels[i] = packed;
    }

    public void PaintOutline(bool[] mask, Color color, int thickness)
    {
        if (thickness <= 0)
            return;
        Paint(Dilate(mask, Width, Height, thickness), color);
    }

    public PixelBuffer ToPixelBuffer()
    {
        var result = PixelBuffer.CreateSolid(Width, Height, 0f, 0f, 0f, 0f);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var packed = _pixels[y * Width + x];
                if (packed == 0)
                    continue;
                result.SetPixel(x, y,
                    ((packed >> 16) & 0xff) / 255f,
                    ((packed >> 8) & 0xff) / 255f,
                    (packed & 0xff) / 255f,
                    ((packed >> 24) & 0xff) / 255f);
            }
        }
        return result;
    }

    public static bool[] Intersect(bool[] left, bool[] right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Mask dimensions must match.");
        var result = new bool[left.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = left[i] && right[i];
        return result;
    }

    public static void Subtract(bool[] target, bool[] subtract)
    {
        if (target.Length != subtract.Length)
            throw new ArgumentException("Mask dimensions must match.");
        for (var i = 0; i < target.Length; i++)
            target[i] &= !subtract[i];
    }

    public static bool[] Dilate(bool[] source, int width, int height, int radius)
    {
        if (source.Length != width * height)
            throw new ArgumentException("Mask dimensions do not match the canvas.");
        if (radius <= 0)
            return (bool[])source.Clone();

        var current = (bool[])source.Clone();
        for (var iteration = 0; iteration < radius; iteration++)
        {
            var expanded = (bool[])current.Clone();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!current[y * width + x])
                    continue;
                for (var oy = -1; oy <= 1; oy++)
                for (var ox = -1; ox <= 1; ox++)
                {
                    var px = x + ox;
                    var py = y + oy;
                    if ((uint)px < (uint)width && (uint)py < (uint)height)
                        expanded[py * width + px] = true;
                }
            }
            current = expanded;
        }
        return current;
    }

    private void StampSquare(bool[] mask, int centerX, int centerY, int radius, bool extendPositive)
    {
        var rightExtension = extendPositive ? 1 : 0;
        for (var y = centerY - radius; y <= centerY + radius + rightExtension; y++)
        for (var x = centerX - radius; x <= centerX + radius + rightExtension; x++)
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
                mask[y * Width + x] = true;
    }

    private void ValidateMask(bool[] mask)
    {
        if (mask.Length != _pixels.Length)
            throw new ArgumentException("Mask dimensions do not match the canvas.", nameof(mask));
    }

    private static bool Contains(IReadOnlyList<SpritePoint> points, float x, float y)
    {
        var inside = false;
        for (var i = 0; i < points.Count; i++)
        {
            var j = i == 0 ? points.Count - 1 : i - 1;
            var current = points[i];
            var previous = points[j];
            if ((current.Y > y) == (previous.Y > y))
                continue;
            var crossingX = (previous.X - current.X) * (y - current.Y) /
                            (previous.Y - current.Y) + current.X;
            if (x < crossingX)
                inside = !inside;
        }
        return inside;
    }

    private static int Pack(Color color)
        => color.A << 24 | color.R << 16 | color.G << 8 | color.B;
}

internal readonly record struct SpritePoint(int X, int Y);
