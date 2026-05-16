using System;
using System.Collections.Generic;
using System.Windows;

namespace PixelAssetGenerator.Core
{
    // Shared integer rasterization utilities to ensure ShapeDrawingWindow and
    // ShapeNode produce identical pixel masks for custom-drawn shapes.
    public static class PixelRaster
    {
        // Rasterize normalized sampled paths (points in [0,1]) into a boolean mask of size x size.
        public static bool[,] RasterizeFromNormalizedPaths(int size, List<(List<Point> sampled, bool closed, float width)> paths)
        {
            var strokes = new List<(IEnumerable<Point> points, int width)>();
            foreach (var (sampled, closed, width) in paths)
            {
                if (sampled == null || sampled.Count == 0) continue;
                var pts = new List<Point>();
                foreach (var p in sampled)
                {
                    // Map normalized coords back to integer logical pixel indices using rounding.
                    var px = p.X * size;
                    var py = p.Y * size;
                    var ix = Math.Clamp((int)MathF.Round((float)px), 0, size - 1);
                    var iy = Math.Clamp((int)MathF.Round((float)py), 0, size - 1);
                    pts.Add(new Point(ix, iy));
                }
                var iw = Math.Max(1, (int)Math.Round(width));
                strokes.Add((pts, iw));
                // If path is closed we also ensure the final-to-first edge will be rasterized
                // by the integer-stroke rasterizer when appropriate.
            }

            return RasterizeFromIntegerStrokes(size, strokes);
        }

        // Rasterize integer-aligned strokes (points containing integer X/Y) into mask
        public static bool[,] RasterizeFromIntegerStrokes(int size, IEnumerable<(IEnumerable<Point> points, int width)> strokes)
        {
            var mask = new bool[size, size];

            void MarkSquare(int cx, int cy, int w)
            {
                var startX = cx - (w - 1) / 2;
                var startY = cy - (w - 1) / 2;
                var endX = startX + w - 1;
                var endY = startY + w - 1;
                for (var yy = startY; yy <= endY; yy++)
                {
                    if (yy < 0 || yy >= size) continue;
                    for (var xx = startX; xx <= endX; xx++)
                    {
                        if (xx < 0 || xx >= size) continue;
                        mask[xx, yy] = true;
                    }
                }
            }

            static IEnumerable<(int x, int y)> BresenhamLinePoints(int x0, int y0, int x1, int y1)
            {
                var dx = Math.Abs(x1 - x0);
                var sx = x0 < x1 ? 1 : -1;
                var dy = -Math.Abs(y1 - y0);
                var sy = y0 < y1 ? 1 : -1;
                var err = dx + dy;
                var x = x0;
                var y = y0;
                while (true)
                {
                    yield return (x, y);
                    if (x == x1 && y == y1) break;
                    var e2 = 2 * err;
                    if (e2 >= dy)
                    {
                        err += dy;
                        x += sx;
                    }
                    if (e2 <= dx)
                    {
                        err += dx;
                        y += sy;
                    }
                }
            }

            foreach (var (points, width) in strokes)
            {
                var ptsList = new List<(int x, int y)>();
                foreach (var p in points)
                {
                    // Points are expected to be integer-aligned; clamp to valid range
                    var ix = Math.Clamp((int)Math.Round(p.X), 0, size - 1);
                    var iy = Math.Clamp((int)Math.Round(p.Y), 0, size - 1);
                    ptsList.Add((ix, iy));
                }
                if (ptsList.Count == 0) continue;

                var w = Math.Max(1, width);
                // Draw line segments
                for (var i = 0; i < ptsList.Count - 1; i++)
                {
                    var a = ptsList[i];
                    var b = ptsList[i + 1];
                    foreach (var p in BresenhamLinePoints(a.x, a.y, b.x, b.y))
                        MarkSquare(p.x, p.y, w);
                }
                // Stamp points
                foreach (var pt in ptsList)
                    MarkSquare(pt.x, pt.y, w);
            }

            return mask;
        }

        /// <summary>
        /// Rasterizes the filled interior of a closed polygon defined by normalized [0,1] points
        /// into a boolean mask of <paramref name="size"/> × <paramref name="size"/> pixels.
        /// Uses an even-odd scanline algorithm sampling at each pixel's vertical center.
        /// </summary>
        public static bool[,] RasterizeFillFromNormalizedPath(int size, IEnumerable<Point> normalizedPoints)
        {
            var mask = new bool[size, size];

            // Convert normalized coords to pixel-space floats.
            var poly = new List<(float x, float y)>();
            foreach (var p in normalizedPoints)
                poly.Add(((float)(p.X * size), (float)(p.Y * size)));

            if (poly.Count < 3) return mask;

            int n = poly.Count;
            for (int sy = 0; sy < size; sy++)
            {
                float scanY = sy + 0.5f; // sample at pixel centre
                var intersections = new List<float>();

                for (int i = 0; i < n; i++)
                {
                    var (x0, y0) = poly[i];
                    var (x1, y1) = poly[(i + 1) % n];
                    // Only count edges that cross scanY (exclusive on the upper boundary to handle shared vertices correctly).
                    if ((y0 <= scanY && scanY < y1) || (y1 <= scanY && scanY < y0))
                    {
                        float t = (scanY - y0) / (y1 - y0);
                        intersections.Add(x0 + t * (x1 - x0));
                    }
                }

                intersections.Sort();

                // Fill between each pair of intersections (even-odd rule).
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int startX = Math.Clamp((int)intersections[i], 0, size - 1);
                    int endX   = Math.Clamp((int)intersections[i + 1], 0, size - 1);
                    for (int sx = startX; sx <= endX; sx++)
                        mask[sx, sy] = true;
                }
            }

            return mask;
        }
    }
}
