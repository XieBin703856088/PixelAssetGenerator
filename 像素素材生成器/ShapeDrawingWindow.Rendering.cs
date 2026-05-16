using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private List<Stroke> GetAllStrokes()
        {
            var all = new List<Stroke>();
            foreach (var (kind, kindIdx) in _layerOrder)
            {
                if (kind == LayerKind.Pencil)
                {
                    if (kindIdx >= _pencilLayers.Count) continue;
                    var layer = _pencilLayers[kindIdx];
                    if (!layer.IsVisible) continue;
                    all.AddRange(layer.Strokes);
                }
                else
                {
                    if (kindIdx >= _vectorPaths.Count) continue;
                    var vp = _vectorPaths[kindIdx];
                    if (!vp.IsVisible) continue;
                    if (vp.Anchors.Count == 0) continue;
                    var pts = SampleVectorPath(vp);
                    if (pts.Count == 0) continue;
                    var s = new Stroke { Width = vp.Width, Color = vp.Color };
                    s.Points.AddRange(pts);
                    all.Add(s);
                }
            }
            return all;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Commit any in-progress path before exporting
            if (_currentPathAnchors.Count >= 2)
                CommitCurrentPath();

            var allStrokes = GetAllStrokes();
            if (allStrokes.Count == 0)
            {
                MessageBox.Show("Draw at least one stroke first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Export normalized points layer by layer so the layer draw order is preserved.
            // Pencil strokes: points + (NegativeInfinity, rgb) color marker + (NaN, width) separator.
            // Filled vector paths: polygon points + (PositiveInfinity, rgb) fill marker, then the
            // same points again as an outline stroke so the border is also rasterized.
            // This matches the z-order used when building ResultBitmap (fill under stroke).
            var outList = new List<Point>();
            foreach (var (kind, kindIdx) in _layerOrder)
            {
                if (kind == LayerKind.Pencil)
                {
                    if (kindIdx >= _pencilLayers.Count) continue;
                    var pencilLayer = _pencilLayers[kindIdx];
                    if (!pencilLayer.IsVisible) continue;
                    foreach (var stroke in pencilLayer.Strokes)
                    {
                        if (stroke.Points.Count == 0) continue;
                        foreach (var p in stroke.Points)
                            outList.Add(new Point(p.X / _canvasSize, p.Y / _canvasSize));
                        long rgb = stroke.Color.R | ((long)stroke.Color.G << 8) | ((long)stroke.Color.B << 16);
                        outList.Add(new Point(double.NegativeInfinity, (double)rgb));
                        outList.Add(new Point(double.NaN, stroke.Width));
                    }
                }
                else // LayerKind.Path
                {
                    if (kindIdx >= _vectorPaths.Count) continue;
                    var vp = _vectorPaths[kindIdx];
                    if (!vp.IsVisible || vp.Anchors.Count == 0) continue;
                    var sampledPts = SampleVectorPath(vp);
                    if (sampledPts.Count == 0) continue;
                    long rgb = vp.Color.R | ((long)vp.Color.G << 8) | ((long)vp.Color.B << 16);

                    // Export fill polygon before the outline so fill renders underneath.
                    if (vp.IsFilled && vp.IsClosed && vp.Anchors.Count >= 3)
                    {
                        foreach (var p in sampledPts)
                            outList.Add(new Point(p.X / _canvasSize, p.Y / _canvasSize));
                        // (PositiveInfinity, packedRgb) signals a filled polygon segment to ShapeNode.
                        outList.Add(new Point(double.PositiveInfinity, (double)rgb));
                    }

                    // Export outline stroke.
                    foreach (var p in sampledPts)
                        outList.Add(new Point(p.X / _canvasSize, p.Y / _canvasSize));
                    outList.Add(new Point(double.NegativeInfinity, (double)rgb));
                    outList.Add(new Point(double.NaN, vp.Width));
                }
            }

            // ── Editor state section ──────────────────────────────────────────────────
            // Appended after the raster data so ShapeNode (which stops at the marker)
            // ignores it, while ShapeDrawingWindow can fully restore layers on re-open.
            // Format: (NaN,-9999) marker, then (layerCount,0), then per-layer records.
            outList.Add(new Point(double.NaN, -9999.0));   // section separator
            outList.Add(new Point(_layerOrder.Count, 0));  // layer count

            foreach (var (layerKind, kindIdx) in _layerOrder)
            {
                if (layerKind == LayerKind.Pencil)
                {
                    if (kindIdx >= _pencilLayers.Count) continue;
                    var pl = _pencilLayers[kindIdx];
                    // (0, strokeCount) — kind=0 means pencil
                    outList.Add(new Point(0, pl.Strokes.Count));
                    // (isVisible, isShapeLayer)
                    outList.Add(new Point(pl.IsVisible ? 1.0 : 0.0, pl.IsShapeLayer ? 1.0 : 0.0));
                    foreach (var stroke in pl.Strokes)
                    {
                        long rgb = stroke.Color.R | ((long)stroke.Color.G << 8) | ((long)stroke.Color.B << 16);
                        outList.Add(new Point(rgb, stroke.Width));          // color + width
                        outList.Add(new Point(stroke.Points.Count, 0));     // point count
                        foreach (var p in stroke.Points)
                            outList.Add(new Point(p.X / _canvasSize, p.Y / _canvasSize));
                    }
                }
                else // Path
                {
                    if (kindIdx >= _vectorPaths.Count) continue;
                    var vp = _vectorPaths[kindIdx];
                    // (1, anchorCount) — kind=1 means path
                    outList.Add(new Point(1, vp.Anchors.Count));
                    long rgb = vp.Color.R | ((long)vp.Color.G << 8) | ((long)vp.Color.B << 16);
                    outList.Add(new Point(rgb, vp.Width));                  // color + width
                    int flags = (vp.IsClosed ? 1 : 0) | (vp.IsFilled ? 2 : 0);
                    outList.Add(new Point(flags, vp.IsVisible ? 1.0 : 0.0)); // flags + visibility
                    foreach (var anchor in vp.Anchors)
                    {
                        outList.Add(new Point(anchor.Position.X / _canvasSize, anchor.Position.Y / _canvasSize));
                        outList.Add(new Point(anchor.InHandle.X  / _canvasSize, anchor.InHandle.Y  / _canvasSize));
                        outList.Add(new Point(anchor.OutHandle.X / _canvasSize, anchor.OutHandle.Y / _canvasSize));
                        outList.Add(new Point((int)anchor.Type, 0));
                    }
                }
            }
            // ── End editor state section ──────────────────────────────────────────────

            ResultPoints = outList;

            // capture bitmap
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, _canvasSize, _canvasSize));

                void DrawExportMask(bool[,] mask, Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    for (var iy = 0; iy < (int)_canvasSize; iy++)
                        for (var ix = 0; ix < (int)_canvasSize; ix++)
                            if (mask[ix, iy])
                                dc.DrawRectangle(brush, null, new Rect(ix, iy, 1, 1));
                }

                // Render layers in _layerOrder so z-order is respected in the exported bitmap.
                foreach (var (kind, kindIdx) in _layerOrder)
                {
                    if (kind == LayerKind.Pencil)
                    {
                        if (kindIdx >= _pencilLayers.Count) continue;
                        var layer = _pencilLayers[kindIdx];
                        if (!layer.IsVisible || layer.Strokes.Count == 0) continue;
                        var layerStrokesByColor = new Dictionary<Color, List<(IEnumerable<Point> points, int width)>>();
                        foreach (var s in layer.Strokes)
                        {
                            if (s.Points.Count == 0) continue;
                            if (!layerStrokesByColor.TryGetValue(s.Color, out var lst))
                                layerStrokesByColor[s.Color] = lst = new();
                            lst.Add((s.Points, Math.Max(1, (int)Math.Round(s.Width))));
                        }
                        foreach (var (sc, css) in layerStrokesByColor)
                        {
                            var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, css);
                            DrawExportMask(mask, sc);
                        }
                    }
                    else // LayerKind.Path
                    {
                        if (kindIdx >= _vectorPaths.Count) continue;
                        var vp = _vectorPaths[kindIdx];
                        if (!vp.IsVisible) continue;
                        if (vp.IsFilled && vp.IsClosed && vp.Anchors.Count >= 3)
                        {
                            var fillMask = ComputeFillMaskForPath((int)_canvasSize, vp);
                            if (fillMask != null)
                                DrawExportMask(fillMask, vp.Color);
                        }
                        if (vp.Anchors.Count > 0)
                        {
                            var pts = SampleVectorPath(vp);
                            if (pts.Count > 0)
                            {
                                var strokeItems = new List<(IEnumerable<Point> points, int width)> { (pts, Math.Max(1, (int)Math.Round(vp.Width))) };
                                var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, strokeItems);
                                DrawExportMask(mask, vp.Color);
                            }
                        }
                    }
                }
            }

            var rtb = new RenderTargetBitmap((int)_canvasSize, (int)_canvasSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            ResultBitmap = rtb;

            DialogResult = true;
            Close();
        }

        private void RedrawCanvas()
        {
            DrawCanvas.Children.Clear();

            var allStrokes = GetAllStrokes();
            var totalAnchors = _vectorPaths.Sum(vp => vp.Anchors.Count) + _currentPathAnchors.Count;
            var totalPixelPts = allStrokes.Sum(s => s.Points.Count) + (_currentStroke?.Points.Count ?? 0);
            VertexCountText.Text = (totalAnchors + totalPixelPts).ToString();
            var pencilStrokeCount = _pencilLayers.Sum(l => l.Strokes.Count);
            PathCountText.Text = (_vectorPaths.Count + pencilStrokeCount + (_currentPathAnchors.Count > 0 ? 1 : 0)).ToString();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var physW = _canvasSize * _displayScale;
                var physH = _canvasSize * _displayScale;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), null, new Rect(0, 0, physW, physH));

                // per-pixel grid lines
                var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
                gridPen.Freeze();
                for (var x = 0; x <= _canvasSize; x++)
                {
                    var gx = x * _displayScale + 0.5;
                    dc.DrawLine(gridPen, new Point(gx, 0), new Point(gx, physH));
                }
                for (var y = 0; y <= _canvasSize; y++)
                {
                    var gy = y * _displayScale + 0.5;
                    dc.DrawLine(gridPen, new Point(0, gy), new Point(physW, gy));
                }

                // Combined mask used to exclude committed pixels from the preview-line overlay.
                bool[,]? combinedMask = null;

                void DrawMaskLayerToDc(bool[,] mask, Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    for (var iy = 0; iy < (int)_canvasSize; iy++)
                        for (var ix = 0; ix < (int)_canvasSize; ix++)
                            if (mask[ix, iy])
                                dc.DrawRectangle(brush, null, new Rect(ix * _displayScale, iy * _displayScale, _displayScale, _displayScale));
                    if (combinedMask == null)
                        combinedMask = mask;
                    else
                    {
                        for (int iy = 0; iy < (int)_canvasSize; iy++)
                            for (int ix = 0; ix < (int)_canvasSize; ix++)
                                if (mask[ix, iy]) combinedMask[ix, iy] = true;
                    }
                }

                // Render layers in _layerOrder so z-order is respected: each layer's fill and
                // stroke are drawn in sequence, so higher layers always paint over lower ones.
                foreach (var (kind, kindIdx) in _layerOrder)
                {
                    if (kind == LayerKind.Pencil)
                    {
                        if (kindIdx >= _pencilLayers.Count) continue;
                        var layer = _pencilLayers[kindIdx];
                        if (!layer.IsVisible || layer.Strokes.Count == 0) continue;
                        var layerStrokesByColor = new Dictionary<Color, List<(IEnumerable<Point> points, int width)>>();
                        foreach (var s in layer.Strokes)
                        {
                            if (s.Points.Count == 0) continue;
                            if (!layerStrokesByColor.TryGetValue(s.Color, out var lst))
                                layerStrokesByColor[s.Color] = lst = new();
                            lst.Add((s.Points, Math.Max(1, (int)Math.Round(s.Width))));
                        }
                        foreach (var (sc, css) in layerStrokesByColor)
                        {
                            var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, css);
                            DrawMaskLayerToDc(mask, sc);
                        }
                    }
                    else // LayerKind.Path
                    {
                        if (kindIdx >= _vectorPaths.Count) continue;
                        var vp = _vectorPaths[kindIdx];
                        if (!vp.IsVisible) continue;
                        // Fill before stroke so the stroke renders on top of the fill within this path.
                        if (vp.IsFilled && vp.IsClosed && vp.Anchors.Count >= 3)
                        {
                            var fillMask = ComputeFillMaskForPath((int)_canvasSize, vp);
                            if (fillMask != null)
                                DrawMaskLayerToDc(fillMask, vp.Color);
                        }
                        if (vp.Anchors.Count > 0)
                        {
                            var pts = SampleVectorPath(vp);
                            if (pts.Count > 0)
                            {
                                var strokeItems = new List<(IEnumerable<Point> points, int width)> { (pts, Math.Max(1, (int)Math.Round(vp.Width))) };
                                var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, strokeItems);
                                DrawMaskLayerToDc(mask, vp.Color);
                            }
                        }
                    }
                }

                // In-progress pencil stroke — only show when the active layer is visible
                if (_currentStroke != null && _currentStroke.Points.Count > 0 && (ActivePencilLayer?.IsVisible ?? true))
                {
                    var strokeItems = new List<(IEnumerable<Point> points, int width)>
                    {
                        (_currentStroke.Points, Math.Max(1, (int)Math.Round(_currentStroke.Width)))
                    };
                    var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, strokeItems);
                    DrawMaskLayerToDc(mask, _currentStroke.Color);
                }

                // Shape path preview during drag — rendered as a rasterized outline
                if (_isStamping && _mode == DrawingMode.ShapeStamp && _shapeStartPos.HasValue)
                {
                    var pStart = _shapeStartPos.Value;
                    var pEnd   = _shapePreviewEnd ?? pStart;
                    int px1 = (int)Math.Min(pStart.X, pEnd.X);
                    int py1 = (int)Math.Min(pStart.Y, pEnd.Y);
                    int px2 = (int)Math.Max(pStart.X, pEnd.X);
                    int py2 = (int)Math.Max(pStart.Y, pEnd.Y);
                    // Single-click fallback: use the size slider value
                    if (px1 == px2 || py1 == py2)
                    {
                        int s    = Math.Max(2, (int)Math.Round(_toolSize));
                        int half = (s - 1) / 2;
                        int cxP  = (int)pStart.X;
                        int cyP  = (int)pStart.Y;
                        px1 = cxP - half;
                        py1 = cyP - half;
                        px2 = px1 + s - 1;
                        py2 = py1 + s - 1;
                    }
                    px1 = Math.Clamp(px1, 0, (int)_canvasSize - 1);
                    py1 = Math.Clamp(py1, 0, (int)_canvasSize - 1);
                    px2 = Math.Clamp(px2, 0, (int)_canvasSize - 1);
                    py2 = Math.Clamp(py2, 0, (int)_canvasSize - 1);
                    if (px2 >= px1 && py2 >= py1)
                    {
                        var previewAnchors = BuildShapeAnchors(_shapeStampType, px1, py1, px2 - px1 + 1, py2 - py1 + 1);
                        if (previewAnchors.Count >= 2)
                        {
                            var previewPath = new VectorPath { Width = 1, Color = _drawColor, IsClosed = true, IsFilled = false };
                            previewPath.Anchors.AddRange(previewAnchors);
                            var previewPts = SampleVectorPath(previewPath);
                            if (previewPts.Count > 0)
                            {
                                var previewBrush = new SolidColorBrush(Color.FromArgb(0xCC, _drawColor.R, _drawColor.G, _drawColor.B));
                                previewBrush.Freeze();
                                foreach (var pt in previewPts)
                                {
                                    int ix = (int)pt.X, iy = (int)pt.Y;
                                    if (ix >= 0 && ix < (int)_canvasSize && iy >= 0 && iy < (int)_canvasSize)
                                        dc.DrawRectangle(previewBrush, null, new Rect(ix * _displayScale, iy * _displayScale, _displayScale, _displayScale));
                                }
                            }
                        }
                    }
                }

                // In-progress path: build a temporary stroke preview using the current draw color
                if (_currentPathAnchors.Count >= 2)
                {
                    var tempStroke = BuildPathStrokeFromAnchors(_currentPathAnchors, closePath: false);
                    if (tempStroke.Points.Count > 0)
                    {
                        var strokeItems = new List<(IEnumerable<Point> points, int width)>
                        {
                            (tempStroke.Points, Math.Max(1, (int)Math.Round(tempStroke.Width)))
                        };
                        var mask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, strokeItems);
                        DrawMaskLayerToDc(mask, _drawColor);
                    }
                }

                // Preview line from last anchor to cursor rendered semi-transparently
                // so users can distinguish uncommitted preview from committed content
                if (_currentPathAnchors.Count >= 1 && _lastMousePos.HasValue)
                {
                    var lastAnchor = _currentPathAnchors[^1];
                    var cursor = _lastMousePos.Value;
                    if (lastAnchor != cursor)
                    {
                        var previewPts = new List<Point>();
                        foreach (var pt in BresenhamLinePoints((int)lastAnchor.X, (int)lastAnchor.Y, (int)cursor.X, (int)cursor.Y))
                            previewPts.Add(new Point(pt.X, pt.Y));
                        var previewStrokes = new List<(IEnumerable<Point> points, int width)>
                        {
                            (previewPts, Math.Max(1, (int)Math.Round(_toolSize)))
                        };
                        var previewMask = PixelRaster.RasterizeFromIntegerStrokes((int)_canvasSize, previewStrokes);
                        var previewBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x5C, 0xC8, 0xFF));
                        previewBrush.Freeze();
                        for (var iy = 0; iy < (int)_canvasSize; iy++)
                        {
                            for (var ix = 0; ix < (int)_canvasSize; ix++)
                            {
                                if (!previewMask[ix, iy] || (combinedMask != null && combinedMask[ix, iy])) continue;
                                dc.DrawRectangle(previewBrush, null, new Rect(ix * _displayScale, iy * _displayScale, _displayScale, _displayScale));
                            }
                        }
                    }
                }

                // Draw anchor point indicators for in-progress path placement
                if (_currentPathAnchors.Count > 0 && _editingPathIndex < 0)
                {
                    var anchorBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x5C, 0xC8, 0xFF));
                    anchorBrush.Freeze();
                    var anchorPen = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 1.0);
                    anchorPen.Freeze();

                    // Check if cursor is near the first anchor to show close-path snap ring
                    bool snapToClose = false;
                    if (_currentPathAnchors.Count >= 2 && _lastMousePos.HasValue)
                    {
                        var first = _currentPathAnchors[0];
                        double snapRadius = Math.Max(5.0, _displayScale * 0.6);
                        double fcx = (first.X + 0.5) * _displayScale;
                        double fcy = (first.Y + 0.5) * _displayScale;
                        double fmx = (_lastMousePos.Value.X + 0.5) * _displayScale;
                        double fmy = (_lastMousePos.Value.Y + 0.5) * _displayScale;
                        double fdx = fmx - fcx, fdy = fmy - fcy;
                        snapToClose = Math.Sqrt(fdx * fdx + fdy * fdy) <= snapRadius;
                    }

                    for (int i = 0; i < _currentPathAnchors.Count; i++)
                    {
                        var anchor = _currentPathAnchors[i];
                        var acx = (anchor.X + 0.5) * _displayScale;
                        var acy = (anchor.Y + 0.5) * _displayScale;
                        var anchorRadius = Math.Max(3.0, _displayScale * 0.3);
                        if (i == 0 && snapToClose)
                        {
                            // Green snap ring around first anchor: signals close-path gesture
                            var snapPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x4C, 0xFF, 0x91)), 1.5);
                            snapPen.Freeze();
                            dc.DrawEllipse(null, snapPen, new Point(acx, acy), anchorRadius * 2.2, anchorRadius * 2.2);
                        }
                        dc.DrawEllipse(anchorBrush, anchorPen, new Point(acx, acy), anchorRadius, anchorRadius);
                    }
                }

                // Draw edit-mode anchor/handle overlay
                if (_editingPathIndex >= 0 && _editingPathIndex < _vectorPaths.Count)
                    DrawEditModeOverlay(dc);

                // In Move mode with no path selected, show passive anchor dots for every path
                // so the user can see where anchors are without needing to select first
                if (_mode == DrawingMode.Move && _editingPathIndex < 0 && _vectorPaths.Count > 0)
                    DrawMoveModePaths(dc);
            }

            var rtb = new RenderTargetBitmap((int)(_canvasSize * _displayScale), (int)(_canvasSize * _displayScale), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var brush = new ImageBrush(rtb) { Stretch = Stretch.None };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(DrawCanvas, BitmapScalingMode.NearestNeighbor);
            DrawCanvas.Background = brush;

            UpdateCursorOverlay();
            UpdateSelectionBoxOverlay();
        }

        private void UpdateCursorOverlay()
        {
            // Remove previous overlay if present
            if (_cursorOverlay != null)
            {
                if (DrawCanvas.Children.Contains(_cursorOverlay))
                    DrawCanvas.Children.Remove(_cursorOverlay);
                _cursorOverlay = null;
            }
            if (_cursorShapeOverlay != null)
            {
                if (DrawCanvas.Children.Contains(_cursorShapeOverlay))
                    DrawCanvas.Children.Remove(_cursorShapeOverlay);
                _cursorShapeOverlay = null;
            }

            // No brush-size cursor in Move/Select mode (rubber-band handles its own feedback)
            if (_mode == DrawingMode.Move && _editingPathIndex < 0) return;

            if (!_lastMousePos.HasValue) return;

            var lp = _lastMousePos.Value;
            var px = (int)lp.X;
            var py = (int)lp.Y;

            if (_mode == DrawingMode.ShapeStamp)
            {
                // During active drag, the preview is rendered by RedrawCanvas — no cursor overlay needed
                if (_isStamping) return;

                int size = Math.Max(1, (int)Math.Round(_toolSize));
                int halfOffset = (size - 1) / 2;
                double physSize = size * _displayScale;
                double left = (px - halfOffset) * _displayScale;
                double top  = (py - halfOffset) * _displayScale;

                UIElement? shapeEl = _shapeStampType switch
                {
                    ShapeStampType.Circle => new System.Windows.Shapes.Ellipse
                    {
                        Width = physSize, Height = physSize,
                        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, _drawColor.R, _drawColor.G, _drawColor.B)),
                        Fill   = new SolidColorBrush(Color.FromArgb(0x33, _drawColor.R, _drawColor.G, _drawColor.B)),
                        StrokeThickness = 1, IsHitTestVisible = false
                    },
                    ShapeStampType.Ring => new System.Windows.Shapes.Ellipse
                    {
                        Width = physSize, Height = physSize,
                        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, _drawColor.R, _drawColor.G, _drawColor.B)),
                        Fill   = Brushes.Transparent,
                        // Stroke thickness matches the actual ring: 28% of the radius (size-1)/2, not the diameter.
                        StrokeThickness = Math.Max(1, _displayScale * 0.28 * (size - 1) / 2.0),
                        IsHitTestVisible = false
                    },
                    ShapeStampType.Diamond => BuildDiamondCursor(physSize, _drawColor),
                    ShapeStampType.Triangle => BuildTriangleCursor(physSize, _drawColor),
                    ShapeStampType.Star => BuildStarCursor(physSize, _drawColor),
                    ShapeStampType.Cross => BuildCrossCursor(physSize, _drawColor),
                    ShapeStampType.Hexagon => BuildHexagonCursor(physSize, _drawColor),
                    _ /* Square */ => new Rectangle
                    {
                        Width = physSize, Height = physSize,
                        Stroke = new SolidColorBrush(Color.FromArgb(0xCC, _drawColor.R, _drawColor.G, _drawColor.B)),
                        Fill   = new SolidColorBrush(Color.FromArgb(0x33, _drawColor.R, _drawColor.G, _drawColor.B)),
                        StrokeThickness = 1, IsHitTestVisible = false
                    }
                };

                if (shapeEl != null)
                {
                    Canvas.SetLeft(shapeEl, left);
                    Canvas.SetTop(shapeEl, top);
                    _cursorOverlay = shapeEl as Rectangle ?? new Rectangle
                    {
                        Width = physSize, Height = physSize,
                        IsHitTestVisible = false, Opacity = 0
                    };
                    // Use the shape element directly as the overlay placeholder so the
                    // remove-by-reference logic above still works via DrawCanvas.Children.
                    DrawCanvas.Children.Add(shapeEl);
                    // Store a reference in _cursorOverlay so the removal path finds it.
                    // Re-implement by keeping shapeEl in a separate field.
                    _cursorShapeOverlay = shapeEl;
                    return;
                }
            }

            // In path mode, show a small indicator matching the path width
            var cursorSize = _mode == DrawingMode.Path
                ? Math.Max(1, (int)Math.Round(_toolSize))
                : Math.Max(1, (int)Math.Round(_toolSize));
            var halfOff = (cursorSize - 1) / 2;
            var startX = px - halfOff;
            var startY = py - halfOff;

            var rect = new Rectangle()
            {
                Width = cursorSize * _displayScale,
                Height = cursorSize * _displayScale,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            if (_mode == DrawingMode.Path)
            {
                rect.Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66));
                rect.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xD9, 0x66));
            }
            else if (EraserToggle?.IsChecked == true)
            {
                rect.Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x66, 0x66));
                rect.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x66, 0x66));
            }
            else
            {
                rect.Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x5C, 0xC8, 0xFF));
                rect.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            }

            Canvas.SetLeft(rect, startX * _displayScale);
            Canvas.SetTop(rect, startY * _displayScale);
            _cursorOverlay = rect;
            DrawCanvas.Children.Add(_cursorOverlay);
        }

        // Shape cursor overlay element (non-Rectangle shapes that can't use _cursorOverlay directly)
        private UIElement? _cursorShapeOverlay;

        private static System.Windows.Shapes.Path BuildDiamondCursor(double physSize, Color c)
        {
            double h = physSize / 2.0;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(h, 0), true, true);
                ctx.LineTo(new Point(physSize, h), false, false);
                ctx.LineTo(new Point(h, physSize), false, false);
                ctx.LineTo(new Point(0, h), false, false);
            }
            geo.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
                Fill   = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
        }

        private static System.Windows.Shapes.Path BuildTriangleCursor(double physSize, Color c)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(physSize / 2.0, 0), true, true);
                ctx.LineTo(new Point(physSize, physSize), false, false);
                ctx.LineTo(new Point(0, physSize), false, false);
            }
            geo.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
                Fill   = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
        }

        private static System.Windows.Shapes.Path BuildStarCursor(double physSize, Color c)
        {
            double cx = physSize / 2.0, cy = physSize / 2.0;
            double outerR = physSize / 2.0, innerR = outerR * 0.38;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool first = true;
                for (int k = 0; k < 10; k++)
                {
                    double angle = Math.PI * k / 5.0 - Math.PI / 2.0;
                    double rad   = k % 2 == 0 ? outerR : innerR;
                    var pt = new Point(cx + Math.Cos(angle) * rad, cy + Math.Sin(angle) * rad);
                    if (first) { ctx.BeginFigure(pt, true, true); first = false; }
                    else ctx.LineTo(pt, false, false);
                }
            }
            geo.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
                Fill   = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
        }

        private static System.Windows.Shapes.Path BuildCrossCursor(double physSize, Color c)
        {
            double arm = Math.Max(1.5, physSize * 0.15);
            double h   = physSize / 2.0;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                // Horizontal bar
                ctx.BeginFigure(new Point(0, h - arm), true, true);
                ctx.LineTo(new Point(physSize, h - arm), false, false);
                ctx.LineTo(new Point(physSize, h + arm), false, false);
                ctx.LineTo(new Point(0, h + arm), false, false);
                // Vertical bar
                ctx.BeginFigure(new Point(h - arm, 0), true, true);
                ctx.LineTo(new Point(h + arm, 0), false, false);
                ctx.LineTo(new Point(h + arm, physSize), false, false);
                ctx.LineTo(new Point(h - arm, physSize), false, false);
            }
            geo.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
                Fill   = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
        }

        private static System.Windows.Shapes.Path BuildHexagonCursor(double physSize, Color c)
        {
            double cx = physSize / 2.0, cy = physSize / 2.0;
            double r = physSize / 2.0;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool first = true;
                for (int k = 0; k < 6; k++)
                {
                    double angle = Math.PI * k / 3.0 - Math.PI / 2.0;
                    var pt = new Point(cx + Math.Cos(angle) * r, cy + Math.Sin(angle) * r);
                    if (first) { ctx.BeginFigure(pt, true, true); first = false; }
                    else ctx.LineTo(pt, false, false);
                }
            }
            geo.Freeze();
            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B)),
                Fill   = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B)),
                StrokeThickness = 1, IsHitTestVisible = false
            };
        }

    }
}
