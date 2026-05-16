using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private enum DrawingMode { Pencil, Path, Move, ShapeStamp }

        private enum ShapeStampType { Circle, Square, Diamond, Triangle, Ring, Star, Cross, Hexagon }

        // --- Path-editing data model ---

        /// <summary>Type of control for a path anchor point.</summary>
        private enum AnchorPointType
        {
            /// <summary>Sharp corner – no Bézier handles used.</summary>
            Corner,
            /// <summary>Smooth Bézier – InHandle is the mirror of OutHandle.</summary>
            Bezier,
            /// <summary>Bézier corner – InHandle and OutHandle are independent.</summary>
            BezierCorner
        }

        private sealed class PathAnchor
        {
            public Point Position { get; set; }
            public AnchorPointType Type { get; set; } = AnchorPointType.Corner;
            /// <summary>Handle on the incoming side (used for Bezier / BezierCorner).</summary>
            public Point InHandle { get; set; }
            /// <summary>Handle on the outgoing side (used for Bezier / BezierCorner).</summary>
            public Point OutHandle { get; set; }

            /// <summary>The pixel center of this anchor in handle coordinate space.</summary>
            public Point Center => new(Position.X + 0.5, Position.Y + 0.5);

            public PathAnchor(Point pos)
            {
                Position = pos;
                // Handles live in a coordinate space where integer n is the left edge of pixel n.
                // Adding 0.5 places them at the pixel center, consistent with anchor rendering.
                InHandle  = new Point(pos.X + 0.5, pos.Y + 0.5);
                OutHandle = new Point(pos.X + 0.5, pos.Y + 0.5);
            }

            public PathAnchor Clone() => new(Position) { Type = Type, InHandle = InHandle, OutHandle = OutHandle };
        }

        private sealed class VectorPath
        {
            public List<PathAnchor> Anchors { get; } = new();
            public bool IsClosed { get; set; }
            public double Width { get; set; } = 1.0;
            public string Name { get; set; } = string.Empty;
            public bool IsVisible { get; set; } = true;
            public bool IsFilled { get; set; } = false;
            public Color Color { get; set; } = Color.FromRgb(255, 255, 255);

            public VectorPath Clone()
            {
                var c = new VectorPath { IsClosed = IsClosed, Width = Width, Name = Name, IsVisible = IsVisible, IsFilled = IsFilled, Color = Color };
                c.Anchors.AddRange(Anchors.Select(a => a.Clone()));
                return c;
            }
        }

        private enum LayerKind { Pencil, Path }

        /// <summary>View-model item bound to the layer ListBox.</summary>
        private sealed class LayerItem : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            private void RaisePropertyChanged(string name) =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

            public string Icon { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string Info { get; init; } = string.Empty;
            public LayerKind Kind { get; init; }
            public int Index { get; init; }
            public bool IsVisible { get; init; } = true;
            /// <summary>Eye icon: filled when visible, hollow when hidden.</summary>
            public string VisibilityIcon => IsVisible ? "👁" : "○";
            /// <summary>Dims the layer name/info text when the layer is hidden.</summary>
            public double NameOpacity => IsVisible ? 1.0 : 0.4;

            private bool _isRenaming;
            /// <summary>True while the layer name is being edited inline.</summary>
            public bool IsRenaming
            {
                get => _isRenaming;
                set
                {
                    if (_isRenaming == value) return;
                    _isRenaming = value;
                    RaisePropertyChanged(nameof(IsRenaming));
                    RaisePropertyChanged(nameof(IsNotRenaming));
                }
            }
            /// <summary>Inverse of <see cref="IsRenaming"/> for visibility bindings.</summary>
            public bool IsNotRenaming => !_isRenaming;

            private string _editName = string.Empty;
            /// <summary>Editable name bound to the rename TextBox.</summary>
            public string EditName
            {
                get => _editName;
                set { _editName = value; RaisePropertyChanged(nameof(EditName)); }
            }
        }

        /// <summary>A named pencil-drawing layer holding a collection of strokes.</summary>
        private sealed class PencilLayer
        {
            public string Name { get; set; } = string.Empty;
            public bool IsVisible { get; set; } = true;
            /// <summary>True for layers created by the shape-stamp tool; displayed with a shape icon.</summary>
            public bool IsShapeLayer { get; set; } = false;
            public List<Stroke> Strokes { get; } = new();

            public PencilLayer Clone()
            {
                var c = new PencilLayer { Name = Name, IsVisible = IsVisible, IsShapeLayer = IsShapeLayer };
                c.Strokes.AddRange(Strokes.Select(CloneStroke));
                return c;
            }

            // Delegate to the outer class helper so Clone() compiles inside the nested class.
            private static Stroke CloneStroke(Stroke s)
            {
                var clone = new Stroke { Width = s.Width, Color = s.Color };
                clone.Points.AddRange(s.Points);
                return clone;
            }
        }

        private enum DragTarget { None, Anchor, InHandle, OutHandle }

        // store strokes with their individual widths and colors so changing either doesn't affect already-drawn strokes
        private sealed class Stroke
        {
            public List<Point> Points { get; } = new();
            public double Width { get; set; }
            public Color Color { get; set; } = Color.FromRgb(255, 255, 255);
        }

        // --- Undo / Redo ---

        private sealed class UndoState
        {
            public required List<PencilLayer> PencilLayers { get; init; }
            public int ActivePencilLayerIndex { get; init; }
            public required List<VectorPath> VectorPaths { get; init; }
            public required List<Point> PathAnchors { get; init; }
            public int EditingPathIndex { get; init; } = -1;
            public int SelectedAnchorIndex { get; init; } = -1;
            public required List<(LayerKind kind, int index)> LayerOrder { get; init; }
        }

        // -----------------------------------------------------------------------
        // Adorner for the layer drag-insertion indicator
        // -----------------------------------------------------------------------

        /// <summary>
        /// Draws a horizontal line with end-caps over a ListBox to indicate
        /// where a dragged layer will be inserted.
        /// </summary>
        private sealed class DragInsertAdorner : Adorner
        {
            private double _y;
            private readonly Brush _brush;

            public DragInsertAdorner(UIElement adornedElement, Brush brush) : base(adornedElement)
            {
                IsHitTestVisible = false;
                _brush = brush;
            }

            /// <summary>Repositions the indicator line and schedules a redraw.</summary>
            public void SetY(double y)
            {
                _y = y;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var pen = new Pen(_brush, 2);
                pen.Freeze();
                double w = ((FrameworkElement)AdornedElement).ActualWidth;
                drawingContext.DrawLine(pen, new Point(4, _y), new Point(w - 4, _y));
                drawingContext.DrawEllipse(_brush, null, new Point(4, _y), 3, 3);
                drawingContext.DrawEllipse(_brush, null, new Point(w - 4, _y), 3, 3);
            }
        }
    }
}
