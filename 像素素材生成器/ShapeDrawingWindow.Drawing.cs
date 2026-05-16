using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private static IEnumerable<(int X, int Y)> BresenhamLinePoints(int x0, int y0, int x1, int y1)
        {
            var dx = Math.Abs(x1 - x0);
            var sx = x0 < x1 ? 1 : -1;
            var dy = -Math.Abs(y1 - y0);
            var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy; // error value e_xy

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

        private void EraseAt(Point pos)
        {
            var px = (int)pos.X;
            var py = (int)pos.Y;
            // use current tool size as eraser size (logical pixels)
            var r = Math.Max(1, (int)Math.Round(_toolSize));
            // compute integer-aligned inclusive bounds so eraser always removes exactly 'r' pixels
            var halfOffset = (r - 1) / 2; // integer division matches rendering/export logic
            var startX = px - halfOffset;
            var startY = py - halfOffset;
            var endX = startX + r - 1;
            var endY = startY + r - 1;

            // Helper to test intersection between two inclusive integer rectangles
            static bool RectsIntersect(int aStartX, int aStartY, int aEndX, int aEndY, int bStartX, int bStartY, int bEndX, int bEndY)
            {
                return !(aEndX < bStartX || aStartX > bEndX || aEndY < bStartY || aStartY > bEndY);
            }

            // For each stroke, split into contiguous segments of points that are not removed.
            // This prevents the rasterizer from reconnecting points across erased gaps
            // (which would otherwise draw new pixels outside the eraser area).
            // Only strokes in the active pencil layer are erased.
            var activeLayer = ActivePencilLayer;
            if (activeLayer != null)
            {
                var newStrokes = new List<Stroke>();
                foreach (var st in activeLayer.Strokes)
                {
                    var stWidth = Math.Max(1, (int)Math.Round(st.Width));
                    var currentSeg = new List<Point>();
                    foreach (var pt in st.Points)
                    {
                        var cx = (int)Math.Round(pt.X);
                        var cy = (int)Math.Round(pt.Y);
                        var markStartX = cx - (stWidth - 1) / 2;
                        var markStartY = cy - (stWidth - 1) / 2;
                        var markEndX = markStartX + stWidth - 1;
                        var markEndY = markStartY + stWidth - 1;
                        var intersects = RectsIntersect(startX, startY, endX, endY, markStartX, markStartY, markEndX, markEndY);
                        if (!intersects)
                        {
                            currentSeg.Add(pt);
                        }
                        else
                        {
                            if (currentSeg.Count > 0)
                            {
                                var ns = new Stroke { Width = st.Width, Color = st.Color };
                                ns.Points.AddRange(currentSeg);
                                newStrokes.Add(ns);
                                currentSeg.Clear();
                            }
                            // skip this point (erased)
                        }
                    }
                    if (currentSeg.Count > 0)
                    {
                        var ns = new Stroke { Width = st.Width, Color = st.Color };
                        ns.Points.AddRange(currentSeg);
                        newStrokes.Add(ns);
                    }
                }
                // Replace strokes with the rebuilt list
                activeLayer.Strokes.Clear();
                activeLayer.Strokes.AddRange(newStrokes);
            }
            // Vector paths are not erased by the pencil eraser — use the layer list to delete them.

            if (_currentStroke != null)
            {
                var st = _currentStroke;
                var stWidth = Math.Max(1, (int)Math.Round(st.Width));
                var segments = new List<List<Point>>();
                var currentSeg = new List<Point>();
                foreach (var pt in st.Points)
                {
                    var cx = (int)Math.Round(pt.X);
                    var cy = (int)Math.Round(pt.Y);
                    var markStartX = cx - (stWidth - 1) / 2;
                    var markStartY = cy - (stWidth - 1) / 2;
                    var markEndX = markStartX + stWidth - 1;
                    var markEndY = markStartY + stWidth - 1;
                    var intersects = RectsIntersect(startX, startY, endX, endY, markStartX, markStartY, markEndX, markEndY);
                    if (!intersects)
                    {
                        currentSeg.Add(pt);
                    }
                    else
                    {
                        if (currentSeg.Count > 0)
                        {
                            segments.Add(new List<Point>(currentSeg));
                            currentSeg.Clear();
                        }
                        // skip erased point
                    }
                }
                if (currentSeg.Count > 0)
                    segments.Add(currentSeg);

                if (segments.Count == 0)
                {
                    _currentStroke = null;
                }
                else
                {
                    // keep the last segment as the current in-progress stroke and push earlier
                    // segments into the finished strokes list so they persist.
                    for (int i = 0; i < segments.Count - 1; i++)
                    {
                        var ns = new Stroke { Width = st.Width, Color = st.Color };
                        ns.Points.AddRange(segments[i]);
                        ActivePencilLayer?.Strokes.Add(ns);
                    }
                    var lastSeg = new Stroke { Width = st.Width, Color = st.Color };
                    lastSeg.Points.AddRange(segments[^1]);
                    _currentStroke = lastSeg;
                }
            }

            RedrawCanvas();
        }

        private void FinishCurrentPath_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPathAnchors.Count >= 2)
            {
                CommitCurrentPath();
                RedrawCanvas();
            }
        }

        private void FinishEdit_Click(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
            if (PathListBox != null) PathListBox.SelectedIndex = -1;
            InstructionText.Text = _mode == DrawingMode.Move
                ? SettingsService.Hints.FinishEditMoveModeInstructions
                : SettingsService.Hints.FinishEditPathModeInstructions;
            RedrawCanvas();
        }

        private void CommitCurrentPath(bool closePath = false)
        {
            if (_currentPathAnchors.Count < 2) return;

            SaveUndoState();
            var vp = new VectorPath
            {
                IsClosed = closePath && _currentPathAnchors.Count >= 3,
                Width = _toolSize,
                Name = $"Path {_vectorPaths.Count + 1}",
                IsFilled = (PathFillToggle?.IsChecked == true) && closePath && _currentPathAnchors.Count >= 3,
                Color = _drawColor
            };
            foreach (var pt in _currentPathAnchors)
                vp.Anchors.Add(new PathAnchor(pt));

            var newPathIndex = _vectorPaths.Count;
            _vectorPaths.Add(vp);
            _layerOrder.Add((LayerKind.Path, newPathIndex));
            _currentPathAnchors.Clear();
            RefreshLayerList();
            // Auto-select the newly committed path in the layer list.
            if (PathListBox?.ItemsSource is IEnumerable<LayerItem> layerItems)
            {
                var newItem = layerItems.FirstOrDefault(it => it.Kind == LayerKind.Path && it.Index == newPathIndex);
                if (newItem != null)
                    PathListBox.SelectedItem = newItem;
            }
        }

        /// <summary>
        /// Commits the current shape drag (or single-click) as a new <see cref="VectorPath"/> added to the layer list.
        /// When start == end (single-click), the shape is placed at the click position using <see cref="_toolSize"/>.
        /// When dragging, the bounding box of the drag defines the shape dimensions.
        /// </summary>
        private void CommitShapePath()
        {
            if (!_shapeStartPos.HasValue) return;

            var pStart = _shapeStartPos.Value;
            var pEnd   = _shapePreviewEnd ?? pStart;

            int x1 = (int)Math.Min(pStart.X, pEnd.X);
            int y1 = (int)Math.Min(pStart.Y, pEnd.Y);
            int x2 = (int)Math.Max(pStart.X, pEnd.X);
            int y2 = (int)Math.Max(pStart.Y, pEnd.Y);

            // Single-click fallback: center shape on click using the size slider
            if (x1 == x2 || y1 == y2)
            {
                int s    = Math.Max(2, (int)Math.Round(_toolSize));
                int half = (s - 1) / 2;
                int cxP  = (int)pStart.X;
                int cyP  = (int)pStart.Y;
                x1 = cxP - half;
                y1 = cyP - half;
                x2 = x1 + s - 1;
                y2 = y1 + s - 1;
            }

            x1 = Math.Clamp(x1, 0, (int)_canvasSize - 1);
            y1 = Math.Clamp(y1, 0, (int)_canvasSize - 1);
            x2 = Math.Clamp(x2, 0, (int)_canvasSize - 1);
            y2 = Math.Clamp(y2, 0, (int)_canvasSize - 1);

            _shapeStartPos   = null;
            _shapePreviewEnd = null;

            if (x2 < x1 || y2 < y1) return;

            int w = x2 - x1 + 1;
            int h = y2 - y1 + 1;

            var anchors = BuildShapeAnchors(_shapeStampType, x1, y1, w, h);
            if (anchors.Count < 2) return;

            bool isFilled = false;
            double strokeWidth = _shapeStampType == ShapeStampType.Ring
                ? Math.Max(1, (int)Math.Round(Math.Min((w - 1) / 2.0, (h - 1) / 2.0) * 0.28))
                : _toolSize;

            var shapeName = ShapeNames.TryGetValue(_shapeStampType, out var sn) ? sn : "Shape";
            var vp = new VectorPath
            {
                Name     = $"{shapeName} {_vectorPaths.Count + 1}",
                Color    = _drawColor,
                Width    = strokeWidth,
                IsClosed = true,
                IsFilled = isFilled
            };
            vp.Anchors.AddRange(anchors);

            var newPathIndex = _vectorPaths.Count;
            _vectorPaths.Add(vp);
            _layerOrder.Add((LayerKind.Path, newPathIndex));
            RefreshLayerList();

            // Auto-select the new shape path in the layer list
            if (PathListBox?.ItemsSource is IEnumerable<LayerItem> layerItems)
            {
                var newItem = layerItems.FirstOrDefault(it => it.Kind == LayerKind.Path && it.Index == newPathIndex);
                if (newItem != null)
                    PathListBox.SelectedItem = newItem;
            }
        }

        /// <summary>
        /// Returns anchor points for the requested <paramref name="shape"/> fitted into a bounding box
        /// starting at (<paramref name="x1"/>, <paramref name="y1"/>) with dimensions
        /// <paramref name="w"/> × <paramref name="h"/> logical pixels.
        /// Circle / Ring use 4 Bézier anchors; all other shapes use Corner anchors.
        /// </summary>
        private static List<PathAnchor> BuildShapeAnchors(ShapeStampType shape, int x1, int y1, int w, int h)
        {
            // Shapes whose diagonal edges depend on a pixel-grid centre vertex need an odd
            // dimension so an exact centre pixel exists.  An even dimension has no centre pixel,
            // which forces cx_i to be asymmetric (e.g. w=6 → cx_f=2.5 → cx_i=2, leaving 2 px
            // on one side and 3 on the other).  Trim 1 px from the far edge to restore symmetry.
            switch (shape)
            {
                case ShapeStampType.Diamond:
                    // Diamond needs a centre pixel in both axes for symmetric diagonal edges.
                    if (w % 2 == 0) w = Math.Max(1, w - 1);
                    if (h % 2 == 0) h = Math.Max(1, h - 1);
                    break;
                case ShapeStampType.Triangle:
                case ShapeStampType.Hexagon:
                case ShapeStampType.Star:
                    // These shapes only mirror left-right, so only width parity matters.
                    if (w % 2 == 0) w = Math.Max(1, w - 1);
                    break;
            }

            int x2 = x1 + w - 1;
            int y2 = y1 + h - 1;
            double cx_f = x1 + (w - 1) * 0.5;   // float center X (exact integer when w is odd)
            double cy_f = y1 + (h - 1) * 0.5;   // float center Y (exact integer when h is odd)
            int    cx_i = (int)Math.Round(cx_f); // integer center X
            int    cy_i = (int)Math.Round(cy_f); // integer center Y
            double rw   = (w - 1) * 0.5;         // half-width
            double rh   = (h - 1) * 0.5;         // half-height

            switch (shape)
            {
                case ShapeStampType.Circle:
                {
                    // Approximate ellipse with 4 cubic Bézier anchors (kappa = 0.5522847498)
                    // Use cx_f/cy_f (float center) for anchor positions to keep the curve
                    // symmetric for even-dimension bounding boxes.
                    const double kappa = 0.5522847498;
                    double kx = kappa * rw;
                    double ky = kappa * rh;

                    var top    = new PathAnchor(new Point(cx_f, y1))   { Type = AnchorPointType.Bezier };
                    var right  = new PathAnchor(new Point(x2,   cy_f)) { Type = AnchorPointType.Bezier };
                    var bottom = new PathAnchor(new Point(cx_f, y2))   { Type = AnchorPointType.Bezier };
                    var left   = new PathAnchor(new Point(x1,   cy_f)) { Type = AnchorPointType.Bezier };

                    top.InHandle     = new Point(cx_f - kx, y1);
                    top.OutHandle    = new Point(cx_f + kx, y1);
                    right.InHandle   = new Point(x2, cy_f - ky);
                    right.OutHandle  = new Point(x2, cy_f + ky);
                    bottom.InHandle  = new Point(cx_f + kx, y2);
                    bottom.OutHandle = new Point(cx_f - kx, y2);
                    left.InHandle    = new Point(x1, cy_f + ky);
                    left.OutHandle   = new Point(x1, cy_f - ky);

                    return [top, right, bottom, left];
                }

                case ShapeStampType.Ring:
                {
                    // Inset the ring path radius by half the stroke width so the stroke
                    // stays within the bounding box and doesn't get clipped at canvas edges.
                    double strokeHalf = Math.Max(0.5, Math.Round(Math.Min(rw, rh) * 0.14));
                    double effRw = Math.Max(1.0, rw - strokeHalf);
                    double effRh = Math.Max(1.0, rh - strokeHalf);
                    const double kappa = 0.5522847498;
                    double kx = kappa * effRw;
                    double ky = kappa * effRh;

                    var top    = new PathAnchor(new Point(cx_f,         cy_f - effRh)) { Type = AnchorPointType.Bezier };
                    var right  = new PathAnchor(new Point(cx_f + effRw, cy_f))         { Type = AnchorPointType.Bezier };
                    var bottom = new PathAnchor(new Point(cx_f,         cy_f + effRh)) { Type = AnchorPointType.Bezier };
                    var left   = new PathAnchor(new Point(cx_f - effRw, cy_f))         { Type = AnchorPointType.Bezier };

                    top.InHandle     = new Point(cx_f - kx, cy_f - effRh);
                    top.OutHandle    = new Point(cx_f + kx, cy_f - effRh);
                    right.InHandle   = new Point(cx_f + effRw, cy_f - ky);
                    right.OutHandle  = new Point(cx_f + effRw, cy_f + ky);
                    bottom.InHandle  = new Point(cx_f + kx, cy_f + effRh);
                    bottom.OutHandle = new Point(cx_f - kx, cy_f + effRh);
                    left.InHandle    = new Point(cx_f - effRw, cy_f + ky);
                    left.OutHandle   = new Point(cx_f - effRw, cy_f - ky);

                    return [top, right, bottom, left];
                }

                case ShapeStampType.Square:
                    return
                    [
                        new PathAnchor(new Point(x1, y1)),
                        new PathAnchor(new Point(x2, y1)),
                        new PathAnchor(new Point(x2, y2)),
                        new PathAnchor(new Point(x1, y2)),
                    ];

                case ShapeStampType.Diamond:
                    return
                    [
                        new PathAnchor(new Point(cx_i, y1)),
                        new PathAnchor(new Point(x2,   cy_i)),
                        new PathAnchor(new Point(cx_i, y2)),
                        new PathAnchor(new Point(x1,   cy_i)),
                    ];

                case ShapeStampType.Triangle:
                    return
                    [
                        new PathAnchor(new Point(cx_i, y1)),
                        new PathAnchor(new Point(x2,   y2)),
                        new PathAnchor(new Point(x1,   y2)),
                    ];

                case ShapeStampType.Star:
                {
                    double outerR = Math.Min(rw, rh);
                    double innerR = Math.Max(1.5, outerR * 0.38);
                    var anchors   = new List<PathAnchor>();
                    for (int k = 0; k < 10; k++)
                    {
                        double angle = Math.PI * k / 5.0 - Math.PI / 2.0;
                        double r     = k % 2 == 0 ? outerR : innerR;
                        int    px    = (int)Math.Round(cx_f + Math.Cos(angle) * r);
                        int    py    = (int)Math.Round(cy_f + Math.Sin(angle) * r);
                        anchors.Add(new PathAnchor(new Point(px, py)));
                    }
                    return anchors;
                }

                case ShapeStampType.Hexagon:
                {
                    // Regular hexagon with pointy top, fitted to the bounding box
                    var anchors = new List<PathAnchor>();
                    for (int k = 0; k < 6; k++)
                    {
                        double angle = Math.PI * k / 3.0 - Math.PI / 2.0;
                        int px = (int)Math.Round(cx_f + Math.Cos(angle) * rw);
                        int py = (int)Math.Round(cy_f + Math.Sin(angle) * rh);
                        anchors.Add(new PathAnchor(new Point(px, py)));
                    }
                    return anchors;
                }

                case ShapeStampType.Cross:
                {
                    int arm = Math.Max(1, (int)Math.Round(Math.Min(rw, rh) * 0.3));
                    return
                    [
                        new PathAnchor(new Point(cx_i - arm, y1)),
                        new PathAnchor(new Point(cx_i + arm, y1)),
                        new PathAnchor(new Point(cx_i + arm, cy_i - arm)),
                        new PathAnchor(new Point(x2,         cy_i - arm)),
                        new PathAnchor(new Point(x2,         cy_i + arm)),
                        new PathAnchor(new Point(cx_i + arm, cy_i + arm)),
                        new PathAnchor(new Point(cx_i + arm, y2)),
                        new PathAnchor(new Point(cx_i - arm, y2)),
                        new PathAnchor(new Point(cx_i - arm, cy_i + arm)),
                        new PathAnchor(new Point(x1,         cy_i + arm)),
                        new PathAnchor(new Point(x1,         cy_i - arm)),
                        new PathAnchor(new Point(cx_i - arm, cy_i - arm)),
                    ];
                }

                default:
                    return [];
            }
        }

        private Stroke BuildPathStrokeFromAnchors(List<Point> anchors, bool closePath)
        {
            var stroke = new Stroke { Width = _toolSize };
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                var a = anchors[i];
                var b = anchors[i + 1];
                foreach (var pt in BresenhamLinePoints((int)a.X, (int)a.Y, (int)b.X, (int)b.Y))
                {
                    var p = new Point(pt.X, pt.Y);
                    if (stroke.Points.Count == 0 || stroke.Points[^1] != p)
                        stroke.Points.Add(p);
                }
            }
            if (closePath && anchors.Count >= 3)
            {
                var last = anchors[^1];
                var first = anchors[0];
                foreach (var pt in BresenhamLinePoints((int)last.X, (int)last.Y, (int)first.X, (int)first.Y))
                {
                    var p = new Point(pt.X, pt.Y);
                    if (stroke.Points.Count == 0 || stroke.Points[^1] != p)
                        stroke.Points.Add(p);
                }
            }
            return stroke;
        }

        private static double CubicBezierPoint(double p0, double c1, double c2, double p3, double t)
        {
            double mt = 1.0 - t;
            return mt * mt * mt * p0 + 3.0 * mt * mt * t * c1 + 3.0 * mt * t * t * c2 + t * t * t * p3;
        }

        /// <summary>
        /// Samples a VectorPath into integer logical-pixel coordinates using Bézier
        /// interpolation for curved segments and Bresenham for straight ones.
        /// </summary>
        private List<Point> SampleVectorPath(VectorPath path)
        {
            var pts = new List<Point>();
            if (path.Anchors.Count == 0) return pts;
            if (path.Anchors.Count == 1)
            {
                pts.Add(path.Anchors[0].Position);
                return pts;
            }

            int segCount = path.IsClosed ? path.Anchors.Count : path.Anchors.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = path.Anchors[i];
                var b = path.Anchors[(i + 1) % path.Anchors.Count];
                bool aBezier = a.Type != AnchorPointType.Corner;
                bool bBezier = b.Type != AnchorPointType.Corner;

                if (!aBezier && !bBezier)
                {
                    int ax = (int)a.Position.X, ay = (int)a.Position.Y;
                    int bx = (int)b.Position.X, by = (int)b.Position.Y;
                    // Normalise the rasterisation direction so that mirror-symmetric segment pairs
                    // (e.g. the matching arms of a star) always produce mirror-image pixel sets.
                    // Bresenham is direction-sensitive; swapping endpoints and reversing the output
                    // yields the same pixels but driven from a canonical (smaller-y-first) direction.
                    bool swapped = ay > by || (ay == by && ax > bx);
                    var segPts = swapped
                        ? BresenhamLinePoints(bx, by, ax, ay).Reverse()
                        : BresenhamLinePoints(ax, ay, bx, by);
                    foreach (var lp in segPts)
                    {
                        var np = new Point(lp.X, lp.Y);
                        if (pts.Count == 0 || pts[^1] != np) pts.Add(np);
                    }
                }
                else
                {
                    var p0 = a.Position;
                    var c1 = aBezier ? a.OutHandle : a.Position;
                    var c2 = bBezier ? b.InHandle : b.Position;
                    var p3 = b.Position;

                    // Approximate arc length for adaptive step count
                    double chord = Math.Sqrt((p3.X - p0.X) * (p3.X - p0.X) + (p3.Y - p0.Y) * (p3.Y - p0.Y));
                    double polyLen = Math.Sqrt((c1.X - p0.X) * (c1.X - p0.X) + (c1.Y - p0.Y) * (c1.Y - p0.Y))
                                   + Math.Sqrt((c2.X - c1.X) * (c2.X - c1.X) + (c2.Y - c1.Y) * (c2.Y - c1.Y))
                                   + Math.Sqrt((p3.X - c2.X) * (p3.X - c2.X) + (p3.Y - c2.Y) * (p3.Y - c2.Y));
                    int steps = Math.Max(4, (int)(Math.Max(chord, polyLen) * 4));

                    Point? prevSample = null;
                    for (int t = 0; t <= steps; t++)
                    {
                        double tt = (double)t / steps;
                        double x = CubicBezierPoint(p0.X, c1.X, c2.X, p3.X, tt);
                        double y = CubicBezierPoint(p0.Y, c1.Y, c2.Y, p3.Y, tt);
                        var np = new Point(
                            Math.Clamp((int)Math.Round(x), 0, (int)_canvasSize - 1),
                            Math.Clamp((int)Math.Round(y), 0, (int)_canvasSize - 1));
                        if (prevSample.HasValue && prevSample.Value != np)
                        {
                            foreach (var lp in BresenhamLinePoints(
                                (int)prevSample.Value.X, (int)prevSample.Value.Y,
                                (int)np.X, (int)np.Y))
                            {
                                var bp = new Point(lp.X, lp.Y);
                                if (pts.Count == 0 || pts[^1] != bp) pts.Add(bp);
                            }
                        }
                        else if (pts.Count == 0 || pts[^1] != np)
                        {
                            pts.Add(np);
                        }
                        prevSample = np;
                    }
                }
            }
            return pts;
        }

        /// <summary>
        /// Converts raw canvas position to float logical canvas coordinates (no integer snap).
        /// Used for handle dragging where sub-pixel precision is needed.
        /// </summary>
        private Point ToLogicalCanvas(Point rawPos)
        {
            var lx = Math.Clamp(rawPos.X / _displayScale, 0.0, _canvasSize - 0.001);
            var ly = Math.Clamp(rawPos.Y / _displayScale, 0.0, _canvasSize - 0.001);
            return new Point(lx, ly);
        }

        /// <summary>
        /// Computes good initial Bézier handle positions for the anchor at <paramref name="anchorIndex"/>,
        /// based on the tangent direction through adjacent anchors.
        /// </summary>
        private void InitializeHandlesForAnchor(VectorPath path, int anchorIndex)
        {
            var anchor = path.Anchors[anchorIndex];
            var pos = anchor.Position;

            Point prevPos = pos, nextPos = pos;
            bool hasPrev = false, hasNext = false;

            if (anchorIndex > 0)
            { prevPos = path.Anchors[anchorIndex - 1].Position; hasPrev = true; }
            else if (path.IsClosed && path.Anchors.Count > 1)
            { prevPos = path.Anchors[^1].Position; hasPrev = true; }

            if (anchorIndex < path.Anchors.Count - 1)
            { nextPos = path.Anchors[anchorIndex + 1].Position; hasNext = true; }
            else if (path.IsClosed && path.Anchors.Count > 1)
            { nextPos = path.Anchors[0].Position; hasNext = true; }

            double dx, dy;
            if (hasPrev && hasNext) { dx = nextPos.X - prevPos.X; dy = nextPos.Y - prevPos.Y; }
            else if (hasNext)       { dx = nextPos.X - pos.X;     dy = nextPos.Y - pos.Y; }
            else if (hasPrev)       { dx = pos.X - prevPos.X;     dy = pos.Y - prevPos.Y; }
            else                    { dx = 2; dy = 0; }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) len = 1;

            double scale;
            if (hasPrev && hasNext)
            {
                double dPrev = Math.Sqrt((pos.X - prevPos.X) * (pos.X - prevPos.X) + (pos.Y - prevPos.Y) * (pos.Y - prevPos.Y));
                double dNext = Math.Sqrt((nextPos.X - pos.X) * (nextPos.X - pos.X) + (nextPos.Y - pos.Y) * (nextPos.Y - pos.Y));
                scale = Math.Max(1.0, Math.Min(dPrev, dNext) / 3.0);
            }
            else if (hasPrev)
                scale = Math.Max(1.0, Math.Sqrt((pos.X - prevPos.X) * (pos.X - prevPos.X) + (pos.Y - prevPos.Y) * (pos.Y - prevPos.Y)) / 3.0);
            else if (hasNext)
                scale = Math.Max(1.0, Math.Sqrt((nextPos.X - pos.X) * (nextPos.X - pos.X) + (nextPos.Y - pos.Y) * (nextPos.Y - pos.Y)) / 3.0);
            else
                scale = 2.0;

            dx /= len; dy /= len;
            double cx = pos.X + 0.5, cy2 = pos.Y + 0.5;  // pixel center in handle coords
            anchor.OutHandle = new Point(cx + dx * scale, cy2 + dy * scale);
            anchor.InHandle  = new Point(cx - dx * scale, cy2 - dy * scale);
        }

        private StreamGeometry BuildPathGeometry(VectorPath path)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();
            var anchors = path.Anchors;
            var startPt = new Point(anchors[0].Position.X + 0.5, anchors[0].Position.Y + 0.5);
            // isClosed=false: we explicitly include the closing segment for Bézier accuracy
            ctx.BeginFigure(startPt, isFilled: true, isClosed: false);
            int segCount = path.IsClosed ? anchors.Count : anchors.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = anchors[i];
                var b = anchors[(i + 1) % anchors.Count];
                bool aBez = a.Type != AnchorPointType.Corner;
                bool bBez = b.Type != AnchorPointType.Corner;
                var bPt = new Point(b.Position.X + 0.5, b.Position.Y + 0.5);
                if (!aBez && !bBez)
                {
                    ctx.LineTo(bPt, isStroked: false, isSmoothJoin: false);
                }
                else
                {
                    var c1 = aBez ? a.OutHandle : new Point(a.Position.X + 0.5, a.Position.Y + 0.5);
                    var c2 = bBez ? b.InHandle  : bPt;
                    ctx.BezierTo(c1, c2, bPt, isStroked: false, isSmoothJoin: false);
                }
            }
            geo.Freeze();
            return geo;
        }

        /// <summary>
        /// Rasterizes the fill of all visible, filled, closed vector paths that match
        /// <paramref name="colorFilter"/> into a boolean pixel mask.
        /// Returns <see langword="null"/> when no such paths exist (avoids unnecessary allocation).
        /// </summary>
        private bool[,]? ComputeFillMaskForPath(int canvasSize, VectorPath vp)
        {
            if (!vp.IsVisible || !vp.IsFilled || !vp.IsClosed || vp.Anchors.Count < 3) return null;
            var sampledPts = SampleVectorPath(vp);
            if (sampledPts.Count < 3) return null;
            // Normalize to [0,1] so PixelRaster uses the same scanline algorithm as ShapeNode,
            // ensuring the canvas preview matches the node output pixel-for-pixel.
            var normalizedPts = sampledPts.Select(p => new Point(p.X / canvasSize, p.Y / canvasSize));
            return PixelRaster.RasterizeFillFromNormalizedPath(canvasSize, normalizedPts);
        }

        private bool[,]? ComputeFillMask(int canvasSize, Color colorFilter)
        {
            bool any = _vectorPaths.Any(vp => vp.IsVisible && vp.IsFilled && vp.IsClosed && vp.Anchors.Count >= 3 && vp.Color == colorFilter);
            if (!any) return null;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var fillBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                fillBrush.Freeze();
                foreach (var vp in _vectorPaths)
                {
                    if (!vp.IsVisible || !vp.IsFilled || !vp.IsClosed || vp.Anchors.Count < 3) continue;
                    if (vp.Color != colorFilter) continue;
                    dc.DrawGeometry(fillBrush, null, BuildPathGeometry(vp));
                }
            }

            var rtb = new RenderTargetBitmap(canvasSize, canvasSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = canvasSize * 4;
            var pixels = new byte[canvasSize * stride];
            rtb.CopyPixels(pixels, stride, 0);

            var mask = new bool[canvasSize, canvasSize];
            for (int y = 0; y < canvasSize; y++)
                for (int x = 0; x < canvasSize; x++)
                    if (pixels[y * stride + x * 4 + 3] > 0)
                        mask[x, y] = true;
            return mask;
        }
    }
}
