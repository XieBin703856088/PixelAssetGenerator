using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.PixelArt;

/// <summary>
/// Palette-index canvas for seamless pixel-art tiles. Every write wraps at the tile
/// boundary, so authored tufts, seams and wrinkles remain continuous when repeated.
/// </summary>
internal sealed class PixelTileCanvas
{
    private readonly byte[] _indices;

    public PixelTileCanvas(int size, byte fill = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        Size = size;
        _indices = new byte[size * size];
        if (fill != 0)
            Array.Fill(_indices, fill);
    }

    public int Size { get; }

    public byte GetWrapped(int x, int y)
        => _indices[GraphNodeBase.Mod(y, Size) * Size + GraphNodeBase.Mod(x, Size)];

    public void Set(int x, int y, byte paletteIndex)
    {
        if ((uint)x < (uint)Size && (uint)y < (uint)Size)
            _indices[y * Size + x] = paletteIndex;
    }

    public void SetWrapped(int x, int y, byte paletteIndex)
        => _indices[GraphNodeBase.Mod(y, Size) * Size + GraphNodeBase.Mod(x, Size)] = paletteIndex;

    public void DrawLineWrapped(int x0, int y0, int x1, int y1, byte paletteIndex, int thickness = 1)
    {
        thickness = Math.Max(1, thickness);
        var radius = Math.Max(0, (thickness - 1) / 2);
        var extend = thickness % 2 == 0 ? 1 : 0;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            for (var oy = -radius; oy <= radius + extend; oy++)
            for (var ox = -radius; ox <= radius + extend; ox++)
                SetWrapped(x0 + ox, y0 + oy, paletteIndex);
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

    public PixelBuffer ToPixelBuffer(IReadOnlyList<Color> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0 || palette.Count > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(palette));
        var result = PixelBufferPool.Borrow(Size, Size);
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var color = palette[Math.Min(_indices[y * Size + x], palette.Count - 1)];
            result.SetPixel(x, y, color.R / 255f, color.G / 255f, color.B / 255f, 1f);
        }
        return result;
    }
}
