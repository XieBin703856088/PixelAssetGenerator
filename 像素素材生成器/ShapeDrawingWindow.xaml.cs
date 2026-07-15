using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    // Drawing window supporting pencil (freehand) and path (click-to-place anchors) modes.
    // Keeps the same public surface as before so existing callers remain compatible.
    public partial class ShapeDrawingWindow : Window
    {

        private double _canvasSize;
        private double _toolSize = 1.0;   // shared size for pencil, path stroke width, and shape stamp
        private bool _syncingToolSize;
        private ShapeStampType _shapeStampType = ShapeStampType.Hexagon;
        private bool _isStamping;
        // Shape placement state — used in ShapeStamp mode to track drag extent for path creation
        private Point? _shapeStartPos;
        private Point? _shapePreviewEnd;
        private System.Windows.Threading.DispatcherTimer? _shapeLongPressTimer;
        private Color _drawColor = Color.FromArgb(255, 255, 255, 255);
        private Border? _colorSwatch;
        private DrawingMode _mode = DrawingMode.Pencil;
        private bool _isDrawing;
        private bool _isErasing;
        // last erase pixel used to interpolate erase path
        private Point? _lastErasePixel;
        // last mouse position in logical pixel coordinates for cursor preview
        private Point? _lastMousePos;
        // lightweight cursor overlay rectangle cached for fast updates
        private Rectangle? _cursorOverlay;
        private Stroke? _currentStroke;
        // Pencil layers: each named layer owns its own stroke list
        private readonly List<PencilLayer> _pencilLayers = [new PencilLayer { Name = "Layer 1" }];
        private int _activePencilLayerIndex = 0;
        private PencilLayer? ActivePencilLayer =>
            _activePencilLayerIndex >= 0 && _activePencilLayerIndex < _pencilLayers.Count
            ? _pencilLayers[_activePencilLayerIndex] : null;

        // --- Path mode state ---
        // Anchor points currently being placed (not yet finished into a VectorPath)
        private readonly List<Point> _currentPathAnchors = new();
        // Finished vector paths (supports Bézier editing)
        private readonly List<VectorPath> _vectorPaths = new();
        // Unified display/render order: each entry identifies a layer by (kind, kind-specific index).
        // This allows free mixing of pencil and path layers in any order.
        private readonly List<(LayerKind kind, int index)> _layerOrder = [];

        // --- Path edit mode state ---
        // Index of the VectorPath currently open for editing (-1 = none)
        private int _editingPathIndex = -1;
        // Index of the selected anchor within the editing path (-1 = none)
        private int _selectedAnchorIndex = -1;
        // Which part of the anchor is being dragged
        private DragTarget _dragTarget = DragTarget.None;
        private bool _isDraggingInEdit;

        // --- Undo / Redo ---
        private const int MaxUndoHistory = 50;

        private readonly List<UndoState> _undoStack = new();
        private readonly List<UndoState> _redoStack = new();

        // how many screen pixels represent one logical canvas pixel
        private int _displayScale;
        // track if a full redraw has been scheduled to avoid flooding the dispatcher
        private bool _redrawScheduled;

        // --- Move/Select mode multi-selection state ---
        // Selected anchors keyed by (pathIndex, anchorIndex) — may span multiple paths
        private readonly HashSet<(int pathIdx, int anchorIdx)> _multiSelectedAnchors = new();
        // True while a rubber-band (box) selection drag is in progress
        private bool _isBoxSelecting;
        private Point _boxSelectStartScreen;    // screen-space corner where drag began
        private Point _boxSelectCurrentScreen;  // screen-space current corner
        // True while dragging a group of selected anchors
        private bool _isDraggingMulti;
        private Point _multiDragStartLogical;   // logical-pixel position where drag started
        // Snapshot of anchor positions captured when the multi-drag begins
        private readonly List<(int pathIdx, int anchorIdx, Point pos, Point inHandle, Point outHandle)> _multiDragOrigins = new();
        // Rubber-band rectangle element overlaid on the canvas
        private Rectangle? _selectionBoxOverlay;

        // Edit-mode multi-selection: indices of selected anchors within the editing path
        private readonly HashSet<int> _editMultiSelectedAnchors = new();

        // Reentrancy guard: prevents RefreshLayerList from triggering itself via SelectionChanged
        private bool _isRefreshingLayerList;

        // --- Layer drag-reorder state ---
        private bool _isDraggingLayer;
        private int _dragLayerUiIndex = -1;
        private Point _dragLayerStartPoint;
        private int _dragInsertBeforeUiIndex = -1;
        private Adorner? _dragInsertAdorner;

        // --- Layer inline-rename state ---
        private bool _isRenamingLayer;
        private LayerItem? _renamingItem;

        public List<Point>? ResultPoints { get; private set; }
        public BitmapSource? ResultBitmap { get; private set; }

        /// <summary>The color used for pencil strokes, path strokes, and fill. Defaults to white.</summary>
        public Color DrawColor
        {
            get => _drawColor;
            set
            {
                _drawColor = value;
                UpdateColorSwatchButton();
                RedrawCanvas();
            }
        }

        public ShapeDrawingWindow() : this(32.0)
        {
        }

        // canvasSize: pixel resolution to render and normalize against (matches node tile size)
        public ShapeDrawingWindow(double canvasSize)
        {
            _canvasSize = Math.Max(1.0, canvasSize);
            // choose an integer display scale so the visible canvas is comfortable to draw on
            // aim for roughly 512px max size: displayScale = floor(512 / canvasSize)
            _displayScale = Math.Max(1, (int)Math.Floor(512.0 / _canvasSize));

            InitializeComponent();
            var drawCanvas = DrawCanvas ?? throw new InvalidOperationException("Drawing canvas failed to initialize.");
            _colorSwatch = ColorPickerSwatch;
            UpdateColorSwatchButton();

            // Ensure pixel-perfect display: enable layout rounding and snapping
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            drawCanvas.SnapsToDevicePixels = true;
            RenderOptions.SetEdgeMode(drawCanvas, EdgeMode.Aliased);

            drawCanvas.Cursor = Cursors.Cross;
            // ensure canvas element matches requested physical size (logical size * scale)

            // Apply canvas dimensions
            drawCanvas.Width = _canvasSize * _displayScale;
            drawCanvas.Height = _canvasSize * _displayScale;

            // Configure pencil size slider limits and default.
            // Pencil size is expressed in logical pixels (1..canvasSize).
            if (PencilSizeSlider != null)
            {
                PencilSizeSlider.Minimum = 1;
                PencilSizeSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                PencilSizeSlider.Value = _toolSize;
                PencilSizeSlider.ValueChanged += (_, __) => SetToolSize(PencilSizeSlider.Value);
            }

            if (PencilSizeText != null)
            {
                PencilSizeText.Text = Math.Max(1, (int)Math.Round(_toolSize)).ToString();
            }

            // Configure path width slider
            if (PathWidthSlider != null)
            {
                PathWidthSlider.Minimum = 1;
                PathWidthSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                PathWidthSlider.Value = _toolSize;
                PathWidthSlider.ValueChanged += (_, __) => SetToolSize(PathWidthSlider.Value);
            }

            if (PathWidthText != null)
            {
                PathWidthText.Text = Math.Max(1, (int)Math.Round(_toolSize)).ToString();
            }

            // Configure shape size slider
            if (ShapeSizeSlider != null)
            {
                ShapeSizeSlider.Minimum = 1;
                ShapeSizeSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                ShapeSizeSlider.Value = _toolSize;
                ShapeSizeSlider.ValueChanged += (_, __) => SetToolSize(ShapeSizeSlider.Value);
            }

            if (ShapeSizeText != null)
                ShapeSizeText.Text = Math.Max(1, (int)Math.Round(_toolSize)).ToString();

            // Long-press timer for shape picker (fires after 450 ms)
            _shapeLongPressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _shapeLongPressTimer.Tick += (_, __) =>
            {
                _shapeLongPressTimer.Stop();
                OpenShapePicker();
            };

            // Seed the unified order with the single default pencil layer created at field initialisation.
            _layerOrder.Add((LayerKind.Pencil, 0));

            RefreshLayerList();
            RedrawCanvas();

            PathListBox.PreviewMouseLeftButtonDown  += PathListBox_DragMouseDown;
            PathListBox.PreviewMouseMove            += PathListBox_DragMouseMove;
            PathListBox.PreviewMouseLeftButtonUp    += PathListBox_DragMouseUp;
            PathListBox.MouseDoubleClick            += PathListBox_MouseDoubleClick;
            PathListBox.PreviewMouseRightButtonDown += PathListBox_LayerContextMenu;
        }

        // Public API to update the logical canvas size after the window has been created.
        // This will resize the visible canvas, adjust the pencil slider range and redraw.
        public void UpdateCanvasSize(double newCanvasSize)
        {
            var cs = Math.Max(1.0, newCanvasSize);
            if (Math.Abs(cs - _canvasSize) < 0.001) return;
            _canvasSize = cs;
            _displayScale = Math.Max(1, (int)Math.Floor(512.0 / _canvasSize));
            DrawCanvas.Width = _canvasSize * _displayScale;
            DrawCanvas.Height = _canvasSize * _displayScale;

            if (PencilSizeSlider != null)
            {
                PencilSizeSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                if (PencilSizeText != null)
                    PencilSizeText.Text = Math.Max(1, (int)Math.Round(PencilSizeSlider.Value)).ToString();
            }

            if (PathWidthSlider != null)
            {
                PathWidthSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                if (PathWidthText != null)
                    PathWidthText.Text = Math.Max(1, (int)Math.Round(PathWidthSlider.Value)).ToString();
            }

            if (ShapeSizeSlider != null)
            {
                ShapeSizeSlider.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
                if (ShapeSizeText != null)
                    ShapeSizeText.Text = Math.Max(1, (int)Math.Round(ShapeSizeSlider.Value)).ToString();
            }

            // Clamp the shared tool size to the updated maximum
            var newMax = Math.Max(1, (int)Math.Ceiling(Math.Min(32.0, _canvasSize)));
            if (_toolSize > newMax)
                SetToolSize(newMax);

            RedrawCanvas();
        }

        // Accept normalized point list where subpaths are separated by NaN sentinel
        public ShapeDrawingWindow(IEnumerable<Point>? initialPoints) : this(32.0)
        {
            InitializeFromNormalizedPoints(initialPoints);
        }

        // Accept normalized points and explicit canvas size
        public ShapeDrawingWindow(IEnumerable<Point>? initialPoints, double canvasSize) : this(canvasSize)
        {
            InitializeFromNormalizedPoints(initialPoints);
        }

        private void InitializeFromNormalizedPoints(IEnumerable<Point>? initialPoints)
        {
            if (initialPoints == null) return;
            var points = initialPoints.ToList();

            // If an editor state section is present, use it for a lossless round-trip.
            var markerIdx = points.FindIndex(p => double.IsNaN(p.X) && !double.IsNaN(p.Y) && p.Y <= -9000.0);
            if (markerIdx >= 0)
            {
                LoadEditorState(points, markerIdx + 1);
                return;
            }

            // Legacy path: load all points as pencil strokes in the default layer.
            var layer = _pencilLayers[0];
            var segment = new List<Point>();
            foreach (var p in points)
            {
                // Separator encoding:
                // - legacy: (NaN, NaN)
                // - extended: (NaN, width) where width is a finite value representing stroke width in logical pixels
                if (double.IsNaN(p.X) || double.IsNaN(p.Y))
                {
                    // If X is NaN and Y is finite, use Y as the stroke width for the segment we just collected.
                    var segWidth = (double.IsNaN(p.X) && !double.IsNaN(p.Y)) ? p.Y : _toolSize;
                    if (segment.Count > 0)
                    {
                        var st = new Stroke { Width = segWidth };
                        foreach (var sp in segment)
                        {
                            // snap initialized points to integer logical pixels
                            var sx = Math.Clamp((int)Math.Round(sp.X), 0, (int)_canvasSize - 1);
                            var sy = Math.Clamp((int)Math.Round(sp.Y), 0, (int)_canvasSize - 1);
                            st.Points.Add(new Point(sx, sy));
                        }
                        layer.Strokes.Add(st);
                        segment.Clear();
                    }
                    // If segment was empty and a width marker was present, ignore — no stroke to assign to.
                }
                else
                {
                    // map normalized to logical pixel coords and snap
                    var lx = p.X * _canvasSize;
                    var ly = p.Y * _canvasSize;
                    lx = Math.Clamp(lx, 0, _canvasSize - 1);
                    ly = Math.Clamp(ly, 0, _canvasSize - 1);
                    segment.Add(new Point(Math.Round(lx), Math.Round(ly)));
                }
            }
            if (segment.Count > 0)
            {
                var st = new Stroke { Width = _toolSize };
                foreach (var sp in segment)
                {
                    var sx = Math.Clamp((int)Math.Round(sp.X), 0, (int)_canvasSize - 1);
                    var sy = Math.Clamp((int)Math.Round(sp.Y), 0, (int)_canvasSize - 1);
                    st.Points.Add(new Point(sx, sy));
                }
                layer.Strokes.Add(st);
            }
            RedrawCanvas();
        }

        /// <summary>
        /// Restores the full editor state (layers, paths, colors, bezier anchors) from the
        /// editor state section that begins at <paramref name="start"/> in <paramref name="points"/>.
        /// </summary>
        private void LoadEditorState(List<Point> points, int start)
        {
            // Clear the default state seeded by the constructor before rebuilding.
            _pencilLayers.Clear();
            _vectorPaths.Clear();
            _layerOrder.Clear();

            int i = start;
            if (i >= points.Count) goto EnsureDefault;

            int layerCount = (int)points[i].X;
            i++;

            for (int l = 0; l < layerCount && i < points.Count; l++)
            {
                var header = points[i++];
                int kind = (int)header.X;

                if (kind == 0) // Pencil layer
                {
                    int strokeCount = (int)header.Y;
                    bool isVisible = i < points.Count && points[i].X >= 0.5;
                    bool isShapeLayer = i < points.Count && points[i].Y >= 0.5;
                    if (i < points.Count) i++; // consume visibility point

                    int shapeCount = _pencilLayers.Count(l => l.IsShapeLayer);
                    int pencilCount = _pencilLayers.Count(l => !l.IsShapeLayer);
                    var layerName = isShapeLayer
                        ? $"Shape Layer {shapeCount + 1}"
                        : $"Layer {pencilCount + 1}";
                    var layer = new PencilLayer { Name = layerName, IsVisible = isVisible, IsShapeLayer = isShapeLayer };
                    _layerOrder.Add((LayerKind.Pencil, _pencilLayers.Count));
                    _pencilLayers.Add(layer);

                    for (int s = 0; s < strokeCount && i < points.Count; s++)
                    {
                        // (packed_rgb, width)
                        var cw = points[i++];
                        var rgb = (long)cw.X;
                        var color = Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
                        var strokeWidth = cw.Y;

                        // (pointCount, 0)
                        if (i >= points.Count) break;
                        int ptCount = (int)points[i++].X;

                        var stroke = new Stroke { Width = strokeWidth, Color = color };
                        for (int p = 0; p < ptCount && i < points.Count; p++)
                        {
                            var np = points[i++];
                            stroke.Points.Add(new Point(
                                Math.Clamp((int)Math.Round(np.X * _canvasSize), 0, (int)_canvasSize - 1),
                                Math.Clamp((int)Math.Round(np.Y * _canvasSize), 0, (int)_canvasSize - 1)));
                        }
                        layer.Strokes.Add(stroke);
                    }
                }
                else // Path layer (kind == 1)
                {
                    int anchorCount = (int)header.Y;

                    // (packed_rgb, width)
                    if (i >= points.Count) break;
                    var cw = points[i++];
                    var rgb = (long)cw.X;
                    var color = Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
                    var pathWidth = cw.Y;

                    // (flags, isVisible)
                    if (i >= points.Count) break;
                    var flagsPt = points[i++];
                    int flags = (int)flagsPt.X;
                    bool isClosed  = (flags & 1) != 0;
                    bool isFilled  = (flags & 2) != 0;
                    bool isVisible = flagsPt.Y >= 0.5;

                    var vp = new VectorPath
                    {
                        Name      = $"Path {_vectorPaths.Count + 1}",
                        Color     = color,
                        Width     = pathWidth,
                        IsClosed  = isClosed,
                        IsFilled  = isFilled,
                        IsVisible = isVisible
                    };
                    _layerOrder.Add((LayerKind.Path, _vectorPaths.Count));
                    _vectorPaths.Add(vp);

                    for (int a = 0; a < anchorCount && i < points.Count; a++)
                    {
                        // position
                        var posPt = points[i++];
                        var pos = new Point(
                            Math.Clamp(posPt.X * _canvasSize, 0, _canvasSize - 1),
                            Math.Clamp(posPt.Y * _canvasSize, 0, _canvasSize - 1));

                        // in-handle
                        if (i >= points.Count) break;
                        var inPt = points[i++];
                        var inHandle = new Point(inPt.X * _canvasSize, inPt.Y * _canvasSize);

                        // out-handle
                        if (i >= points.Count) break;
                        var outPt = points[i++];
                        var outHandle = new Point(outPt.X * _canvasSize, outPt.Y * _canvasSize);

                        // anchor type
                        if (i >= points.Count) break;
                        var anchorType = (AnchorPointType)(int)points[i++].X;

                        vp.Anchors.Add(new PathAnchor(pos)
                        {
                            Type      = anchorType,
                            InHandle  = inHandle,
                            OutHandle = outHandle
                        });
                    }
                }
            }

            EnsureDefault:
            // Always keep at least one pencil layer so the editor is usable.
            if (_pencilLayers.Count == 0)
            {
                _pencilLayers.Add(new PencilLayer { Name = "Layer 1" });
                _layerOrder.Insert(0, (LayerKind.Pencil, 0));
            }
            _activePencilLayerIndex = 0;
            RefreshLayerList();
            RedrawCanvas();
        }




        // Schedule a deferred full redraw to avoid blocking mouse input with expensive bitmap rendering.
        // Ensures we only queue one redraw at a time.
        private void ScheduleRedraw()
        {
            if (_redrawScheduled) return;
            _redrawScheduled = true;
            // use Background priority so input/overlay updates remain smooth
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { RedrawCanvas(); }
                finally { _redrawScheduled = false; }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }



        // Erase pixels/stroke points within eraser area centered on logical pixel 'pos'.
        // Improved behavior: remove any stroke points whose stamped square (according to
        // the stroke's width) intersects the eraser rectangle. This matches the rasterizer
        // semantics and prevents leftover pixels (or accidental removal elsewhere) when
        // strokes use widths > 1.



        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var savedMode = _mode;
            SaveUndoState();
            _pencilLayers.Clear();
            _pencilLayers.Add(new PencilLayer { Name = "Layer 1" });
            _activePencilLayerIndex = 0;
            _currentStroke = null;
            _vectorPaths.Clear();
            _currentPathAnchors.Clear();
            _layerOrder.Clear();
            _layerOrder.Add((LayerKind.Pencil, 0));
            ExitEditMode();
            RefreshLayerList();
            if (_mode != savedMode)
                SetMode(savedMode);
            RedrawCanvas();
        }

        // --- Undo / Redo helpers ---

        private UndoState CaptureState()
        {
            return new UndoState
            {
                PencilLayers = _pencilLayers.Select(l => l.Clone()).ToList(),
                ActivePencilLayerIndex = _activePencilLayerIndex,
                VectorPaths = _vectorPaths.Select(vp => vp.Clone()).ToList(),
                PathAnchors = new List<Point>(_currentPathAnchors),
                EditingPathIndex = _editingPathIndex,
                SelectedAnchorIndex = _selectedAnchorIndex,
                LayerOrder = new List<(LayerKind kind, int index)>(_layerOrder)
            };
        }

        private void RestoreState(UndoState state)
        {
            _pencilLayers.Clear();
            _pencilLayers.AddRange(state.PencilLayers.Select(l => l.Clone()));
            _activePencilLayerIndex = Math.Clamp(state.ActivePencilLayerIndex, 0, Math.Max(0, _pencilLayers.Count - 1));
            _vectorPaths.Clear();
            _vectorPaths.AddRange(state.VectorPaths.Select(vp => vp.Clone()));
            _layerOrder.Clear();
            _layerOrder.AddRange(state.LayerOrder);
            _currentPathAnchors.Clear();
            _currentPathAnchors.AddRange(state.PathAnchors);
            _currentStroke = null;
            _isStamping    = false;
            _shapeStartPos   = null;
            _shapePreviewEnd = null;
            _editingPathIndex = state.EditingPathIndex;
            _selectedAnchorIndex = state.SelectedAnchorIndex;
            _dragTarget = DragTarget.None;
            _isDraggingInEdit = false;
            // Clear transient multi-select state — anchor indices may have shifted after undo
            _multiSelectedAnchors.Clear();
            _editMultiSelectedAnchors.Clear();
            _isDraggingMulti = false;
            _multiDragOrigins.Clear();
            _isBoxSelecting = false;
            RemoveSelectionBoxOverlay();
            RefreshLayerList();
            RedrawCanvas();
        }

        /// <summary>
        /// Saves the current drawing state onto the undo stack and clears the redo stack.
        /// </summary>
        private void SaveUndoState()
        {
            if (_undoStack.Count >= MaxUndoHistory)
                _undoStack.RemoveAt(0);
            _undoStack.Add(CaptureState());
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(CaptureState());
            var state = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            RestoreState(state);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(CaptureState());
            var state = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            RestoreState(state);
        }

        // --- Mode switching ---
        private void PencilModeButton_Click(object sender, RoutedEventArgs e)
        {
            SetMode(DrawingMode.Pencil);
        }

        private void PathModeButton_Click(object sender, RoutedEventArgs e)
        {
            SetMode(DrawingMode.Path);
        }

        private void MoveModeButton_Click(object sender, RoutedEventArgs e)
        {
            SetMode(DrawingMode.Move);
        }

        private void ShapeModeButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any in-flight long-press so a normal click is not eaten
            _shapeLongPressTimer?.Stop();
            SetMode(DrawingMode.ShapeStamp);
        }

        private void ShapeModeButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _shapeLongPressTimer?.Start();
        }

        private void ShapeModeButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            // If the timer has not fired yet, cancel it — this was a short click, not a long press
            if (_shapeLongPressTimer?.IsEnabled == true)
                _shapeLongPressTimer.Stop();
        }

        /// <summary>Opens the shape picker popup anchored below <see cref="ShapeModeButton"/>.</summary>
        private void OpenShapePicker()
        {
            if (ShapePickerPopup == null) return;
            ShapePickerPopup.PlacementTarget = ShapeModeButton;
            ShapePickerPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ShapePickerPopup.IsOpen = true;
        }

        /// <summary>Handles a shape selection from <see cref="ShapePickerPopup"/>.</summary>
        private void ShapePicker_Click(object sender, RoutedEventArgs e)
        {
            if (ShapePickerPopup != null)
                ShapePickerPopup.IsOpen = false;

            if ((sender as System.Windows.Controls.Button)?.Tag is not string tag) return;
            if (!Enum.TryParse<ShapeStampType>(tag, out var picked)) return;

            _shapeStampType = picked;

            // Update toolbar label and button icon
            var names = new Dictionary<ShapeStampType, (string label, string icon)>
            {
                [ShapeStampType.Circle]   = ("Circle",  "●"),
                [ShapeStampType.Square]   = ("Square", "■"),
                [ShapeStampType.Diamond]  = ("Diamond",  "◆"),
                [ShapeStampType.Triangle] = ("Triangle", "▲"),
                [ShapeStampType.Ring]     = ("Ring",  "○"),
                [ShapeStampType.Star]     = ("Star",  "★"),
                [ShapeStampType.Cross]    = ("Cross",  "✚"),
                [ShapeStampType.Hexagon]  = ("Hexagon", "⬢"),
            };

            if (names.TryGetValue(picked, out var info))
            {
                if (ShapeTypeLabel != null)  ShapeTypeLabel.Text  = info.label;
                if (ShapeModeIcon  != null)  ShapeModeIcon.Text   = info.icon;
            }

            SetMode(DrawingMode.ShapeStamp);
        }

        private void SetMode(DrawingMode mode)
        {
            // Exit edit mode whenever switching drawing modes
            ExitEditMode();

            // Finish any in-progress path before switching
            if (_mode == DrawingMode.Path && _currentPathAnchors.Count >= 2)
                CommitCurrentPath();
            else
                _currentPathAnchors.Clear();

            // Clear multi-select state when changing modes
            _multiSelectedAnchors.Clear();
            _isBoxSelecting = false;
            _isDraggingMulti = false;
            _multiDragOrigins.Clear();
            RemoveSelectionBoxOverlay();

            _mode = mode;
            PencilModeButton.IsChecked  = mode == DrawingMode.Pencil;
            PathModeButton.IsChecked    = mode == DrawingMode.Path;
            MoveModeButton.IsChecked    = mode == DrawingMode.Move;
            ShapeModeButton.IsChecked   = mode == DrawingMode.ShapeStamp;
            PencilToolbar.Visibility    = mode == DrawingMode.Pencil      ? Visibility.Visible : Visibility.Collapsed;
            PathToolbar.Visibility      = mode == DrawingMode.Path        ? Visibility.Visible : Visibility.Collapsed;
            MoveToolbar.Visibility      = mode == DrawingMode.Move        ? Visibility.Visible : Visibility.Collapsed;
            ShapeToolbar.Visibility     = mode == DrawingMode.ShapeStamp  ? Visibility.Visible : Visibility.Collapsed;

            InstructionText.Text = mode switch
            {
                DrawingMode.Pencil     => SettingsService.Hints.PencilModeInstructions,
                DrawingMode.Path       => SettingsService.Hints.PathModeInstructions,
                DrawingMode.Move       => SettingsService.Hints.MoveModeInstructions,
                DrawingMode.ShapeStamp => SettingsService.Hints.ShapeStampModeInstructions,
                _ => ""
            };

            RedrawCanvas();
        }


        // --- Layer list sidebar handlers ---


        /// <summary>Toggles the visibility of the layer whose eye-icon button was clicked.</summary>

        // --- Layer drag-to-reorder mouse handlers ---


        /// <summary>Add a new empty pencil layer and select it.</summary>


        /// <summary>Removes the pencil layer at <paramref name="kindIdx"/> and fixes up <see cref="_layerOrder"/>.</summary>

        // -----------------------------------------------------------------------
        // Layer drag-reorder helpers
        // -----------------------------------------------------------------------

        /// <summary>Returns the UI-list index (0 = top) of the layer item under <paramref name="pt"/> in PathListBox space, or -1.</summary>

        /// <summary>Returns the UI-list index before which the dragged item should be inserted, based on vertical mouse position.</summary>

        /// <summary>Returns the Y coordinate (in PathListBox space) for the drag insertion indicator line.</summary>

        /// <summary>Shows or repositions the adorner insertion-line indicator on PathListBox.</summary>

        /// <summary>
        /// Moves the layer at UI position <paramref name="fromUiIdx"/> so it appears just before
        /// UI position <paramref name="insertBeforeUiIdx"/> in the layer panel.
        /// UI index 0 is the topmost (highest-priority) layer, matching panel display order.
        /// </summary>




        /// <summary>
        /// Converts current path anchors into a finished stroke and adds it to the path strokes list.
        /// </summary>


        private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(_drawColor) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _drawColor = dlg.SelectedColor;
                UpdateColorSwatchButton();
                RedrawCanvas();
            }
        }

        private void UpdateColorSwatchButton()
        {
            if (_colorSwatch != null)
                _colorSwatch.Background = new SolidColorBrush(_drawColor);
        }

        /// <summary>Sets the shared tool size and synchronises all three sliders.</summary>
        private void SetToolSize(double size)
        {
            if (_syncingToolSize) return;
            _syncingToolSize = true;
            try
            {
                var max = ShapeSizeSlider?.Maximum ?? Math.Min(32.0, _canvasSize);
                _toolSize = Math.Clamp(size, 1, max);
                if (PencilSizeSlider != null) PencilSizeSlider.Value = _toolSize;
                if (PathWidthSlider  != null) PathWidthSlider.Value  = _toolSize;
                if (ShapeSizeSlider  != null) ShapeSizeSlider.Value  = _toolSize;
                if (PencilSizeText   != null) PencilSizeText.Text    = Math.Max(1, (int)Math.Round(_toolSize)).ToString();
                if (PathWidthText    != null) PathWidthText.Text     = Math.Max(1, (int)Math.Round(_toolSize)).ToString();
                if (ShapeSizeText    != null) ShapeSizeText.Text     = Math.Max(1, (int)Math.Round(_toolSize)).ToString();
                RedrawCanvas();
            }
            finally { _syncingToolSize = false; }
        }


        /// <summary>
        /// Returns all finished strokes from all pencil layers combined with sampled vector paths.
        /// </summary>



        /// <summary>
        /// Renders anchor squares, Bézier handle circles and connecting lines for the editing path.
        /// All coordinates are in the DrawingContext's space (physical pixels = logical * _displayScale).
        /// </summary>

        /// <summary>
        /// Builds a temporary Stroke from path anchor points by Bresenham-interpolating between consecutive anchors.
        /// </summary>

        // Update or create a lightweight cursor overlay rectangle. This avoids
        // regenerating the full RenderTargetBitmap on simple mouse moves and
        // makes cursor tracking much smoother.


        // -----------------------------------------------------------------------
        // Vector path sampling helpers
        // -----------------------------------------------------------------------

        /// <summary>Evaluates a cubic Bézier polynomial component at parameter t.</summary>

        // -----------------------------------------------------------------------
        // Path fill rasterization helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Builds a WPF <see cref="StreamGeometry"/> from the Bézier anchors of a <see cref="VectorPath"/>,
        /// suitable for fill rasterization. Coordinates are in logical pixel space (integer anchor
        /// positions + 0.5 offset to hit pixel centres; handles already in fractional pixel space).
        /// </summary>

        // -----------------------------------------------------------------------
        // Path list helpers
        // -----------------------------------------------------------------------

        /// <summary>Returns the first visual child of type <typeparamref name="T"/> in the subtree.</summary>

        /// <summary>Returns the <see cref="LayerItem"/> under <paramref name="pt"/> in PathListBox coordinates, or null.</summary>

        /// <summary>Double-click on a layer item starts an inline rename.</summary>


        // -----------------------------------------------------------------------
        // Layer inline rename helpers
        // -----------------------------------------------------------------------

        /// <summary>Begins an inline rename for <paramref name="item"/>, showing the TextBox and focusing it.</summary>


        /// <summary>
        /// Renders passive anchor-position indicators for all vector paths while in Move mode
        /// and no specific path is selected. Uses dimmed colours so anchors are visible but
        /// unobtrusive. No handles are drawn — the full overlay appears once a path is entered.
        /// </summary>

        /// <summary>
        /// Hit-tests screen position against anchor points and shows a context menu for changing
        /// the anchor type or deleting it. Works in Move mode (all paths) and edit mode (active path).
        /// Returns true if a menu was shown.
        /// </summary>

        // -----------------------------------------------------------------------
        // Move/Select mode helpers
        // -----------------------------------------------------------------------

        /// <summary>Hit-tests screen position against every anchor across all paths.</summary>

        /// <summary>Completes the rubber-band selection. In edit mode adds enclosed anchors to <see cref="_editMultiSelectedAnchors"/>; in Move mode adds to <see cref="_multiSelectedAnchors"/>.</summary>

        // -----------------------------------------------------------------------
        // Shape stamp helpers
        // -----------------------------------------------------------------------

        private static readonly IReadOnlyDictionary<ShapeStampType, string> ShapeIcons =
            new Dictionary<ShapeStampType, string>
            {
                [ShapeStampType.Circle]   = "●",
                [ShapeStampType.Square]   = "■",
                [ShapeStampType.Diamond]  = "◆",
                [ShapeStampType.Triangle] = "▲",
                [ShapeStampType.Ring]     = "○",
                [ShapeStampType.Star]     = "★",
                [ShapeStampType.Cross]    = "✛",
                [ShapeStampType.Hexagon]  = "⬢",
            };

        private static readonly IReadOnlyDictionary<ShapeStampType, string> ShapeNames =
            new Dictionary<ShapeStampType, string>
            {
                [ShapeStampType.Circle]   = "Circle",
                [ShapeStampType.Square]   = "Square",
                [ShapeStampType.Diamond]  = "Diamond",
                [ShapeStampType.Triangle] = "Triangle",
                [ShapeStampType.Ring]     = "Ring",
                [ShapeStampType.Star]     = "Star",
                [ShapeStampType.Cross]    = "Cross",
                [ShapeStampType.Hexagon]  = "Hexagon",
            };

    }
}
