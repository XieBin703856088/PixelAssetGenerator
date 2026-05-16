using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private void SetAnchorTypeCorner_Click(object sender, RoutedEventArgs e)
            => ChangeSelectedAnchorType(AnchorPointType.Corner);

        private void SetAnchorTypeBezier_Click(object sender, RoutedEventArgs e)
            => ChangeSelectedAnchorType(AnchorPointType.Bezier);

        private void SetAnchorTypeBezierCorner_Click(object sender, RoutedEventArgs e)
            => ChangeSelectedAnchorType(AnchorPointType.BezierCorner);

        private void ChangeSelectedAnchorType(AnchorPointType type)
        {
            if (_editingPathIndex < 0) return;
            var path = _vectorPaths[_editingPathIndex];

            // Apply to all multi-selected anchors when more than one is selected
            IEnumerable<int> targets = _editMultiSelectedAnchors.Count > 1
                ? (IEnumerable<int>)_editMultiSelectedAnchors
                : (_selectedAnchorIndex >= 0 ? new[] { _selectedAnchorIndex } : []);

            bool changed = false;
            foreach (var idx in targets)
            {
                if (idx < 0 || idx >= path.Anchors.Count) continue;
                var anchor = path.Anchors[idx];
                if (anchor.Type == type) continue;
                if (!changed) { SaveUndoState(); changed = true; }
                anchor.Type = type;
                if (type != AnchorPointType.Corner)
                {
                    var c = anchor.Center;
                    bool handlesAtCenter = Math.Abs(anchor.OutHandle.X - c.X) < 0.01 && Math.Abs(anchor.OutHandle.Y - c.Y) < 0.01;
                    if (handlesAtCenter)
                        InitializeHandlesForAnchor(path, idx);
                    if (type == AnchorPointType.Bezier)
                        anchor.InHandle = new Point(2 * c.X - anchor.OutHandle.X, 2 * c.Y - anchor.OutHandle.Y);
                }
            }
            if (changed)
            {
                UpdateAnchorEditPanelVisibility();
                RedrawCanvas();
            }
        }

        private void DeleteSelectedAnchor_Click(object sender, RoutedEventArgs e)
        {
            if (_editingPathIndex < 0 || _selectedAnchorIndex < 0) return;
            var path = _vectorPaths[_editingPathIndex];
            if (_selectedAnchorIndex >= path.Anchors.Count) return;
            SaveUndoState();
            path.Anchors.RemoveAt(_selectedAnchorIndex);
            _selectedAnchorIndex = -1;
            if (path.Anchors.Count == 0)
            {
                RemoveVectorPathAt(_editingPathIndex);
                ExitEditMode();
                RefreshLayerList();
            }
            else
            {
                UpdateAnchorEditPanelVisibility();
                RefreshLayerList();
            }
            RedrawCanvas();
        }

        private void DrawEditModeOverlay(DrawingContext dc)
        {
            var path = _vectorPaths[_editingPathIndex];
            double ds = _displayScale;
            double anchorHalf = Math.Max(3.5, ds * 0.35);   // half-size of anchor square
            double handleR    = Math.Max(2.5, ds * 0.25);   // radius of handle circle

            // Pens / brushes (frozen for performance)
            var handleLinePen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 1.0); handleLinePen.Freeze();
            var handleFill    = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xE5, 0xFF));              handleFill.Freeze();
            var handlePen     = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 1.0); handlePen.Freeze();

            var anchorFill        = new SolidColorBrush(Color.FromArgb(0xFF, 0x5C, 0xC8, 0xFF)); anchorFill.Freeze();
            var anchorSelFill     = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)); anchorSelFill.Freeze();
            var anchorBezierFill  = new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0xFF, 0xA8)); anchorBezierFill.Freeze();
            var anchorPen         = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 1.0); anchorPen.Freeze();

            for (int i = 0; i < path.Anchors.Count; i++)
            {
                var anchor = path.Anchors[i];
                bool isSelected = i == _selectedAnchorIndex || _editMultiSelectedAnchors.Contains(i);

                // Screen center of this anchor
                double ax = (anchor.Position.X + 0.5) * ds;
                double ay = (anchor.Position.Y + 0.5) * ds;

                // Draw Bézier handles for selected anchor (or all curved anchors for context)
                if (anchor.Type != AnchorPointType.Corner && (isSelected || anchor.Type != AnchorPointType.Corner))
                {
                    double ihx = anchor.InHandle.X  * ds;
                    double ihy = anchor.InHandle.Y  * ds;
                    double ohx = anchor.OutHandle.X * ds;
                    double ohy = anchor.OutHandle.Y * ds;

                    // Lines from anchor to handles
                    dc.DrawLine(handleLinePen, new Point(ax, ay), new Point(ihx, ihy));
                    dc.DrawLine(handleLinePen, new Point(ax, ay), new Point(ohx, ohy));

                    // Handle circles
                    dc.DrawEllipse(handleFill, handlePen, new Point(ihx, ihy), handleR, handleR);
                    dc.DrawEllipse(handleFill, handlePen, new Point(ohx, ohy), handleR, handleR);
                }

                // Anchor square
                var fill = isSelected ? anchorSelFill
                         : anchor.Type != AnchorPointType.Corner ? anchorBezierFill
                         : anchorFill;
                // Corner = square, Bezier = diamond (rotated 45°)
                if (anchor.Type == AnchorPointType.Corner)
                {
                    dc.DrawRectangle(fill, anchorPen, new Rect(ax - anchorHalf, ay - anchorHalf, anchorHalf * 2, anchorHalf * 2));
                }
                else
                {
                    // Draw a rotated square (diamond) using StreamGeometry
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(new Point(ax, ay - anchorHalf), true, true);
                        ctx.LineTo(new Point(ax + anchorHalf, ay), false, false);
                        ctx.LineTo(new Point(ax, ay + anchorHalf), false, false);
                        ctx.LineTo(new Point(ax - anchorHalf, ay), false, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(fill, anchorPen, geo);
                }
            }
        }

        private void EnterEditMode(int pathIndex)
        {
            if (pathIndex < 0 || pathIndex >= _vectorPaths.Count) return;
            // Commit any in-progress placement first
            if (_currentPathAnchors.Count >= 2) CommitCurrentPath();
            else if (_currentPathAnchors.Count > 0) _currentPathAnchors.Clear();

            _editingPathIndex = pathIndex;
            _selectedAnchorIndex = -1;
            _dragTarget = DragTarget.None;
            _isDraggingInEdit = false;
            // Entering single-path edit mode clears all multi-selection state
            _multiSelectedAnchors.Clear();
            _editMultiSelectedAnchors.Clear();
            _isDraggingMulti = false;
            _multiDragOrigins.Clear();
            _isBoxSelecting = false;
            RemoveSelectionBoxOverlay();

            UpdateAnchorEditPanelVisibility();
            InstructionText.Text = SettingsService.Hints.EditModeInstructions;
            RedrawCanvas();
        }

        private void ExitEditMode()
        {
            _editingPathIndex = -1;
            _selectedAnchorIndex = -1;
            _dragTarget = DragTarget.None;
            _isDraggingInEdit = false;
            _editMultiSelectedAnchors.Clear();
            _isDraggingMulti = false;
            _multiDragOrigins.Clear();
            _isBoxSelecting = false;
            RemoveSelectionBoxOverlay();
            UpdateAnchorEditPanelVisibility();
        }

        private void UpdateAnchorEditPanelVisibility()
        {
            if (AnchorEditPanel == null || FinishEditButton == null) return;
            bool inEdit = _editingPathIndex >= 0;
            bool inShapeLayer = !inEdit
                && _activePencilLayerIndex >= 0
                && _activePencilLayerIndex < _pencilLayers.Count
                && _pencilLayers[_activePencilLayerIndex].IsShapeLayer;

            // Switch between placeholder text, shape-layer panel, and path-edit controls
            if (EditActionsPanel != null)
                EditActionsPanel.Visibility = inEdit ? Visibility.Visible : Visibility.Collapsed;
            if (ShapeLayerPanel != null)
                ShapeLayerPanel.Visibility = (!inEdit && inShapeLayer) ? Visibility.Visible : Visibility.Collapsed;
            if (PropertiesPlaceholder != null)
                PropertiesPlaceholder.Visibility = (!inEdit && !inShapeLayer) ? Visibility.Visible : Visibility.Collapsed;

            FinishEditButton.Visibility = inEdit ? Visibility.Visible : Visibility.Collapsed;
            bool hasSelection = _selectedAnchorIndex >= 0 || _editMultiSelectedAnchors.Count > 0;
            AnchorEditPanel.Visibility  = (inEdit && hasSelection) ? Visibility.Visible : Visibility.Collapsed;

            // Show/sync the fill toggle whenever a path is being edited
            if (EditPathFillButton != null)
            {
                EditPathFillButton.Visibility = inEdit ? Visibility.Visible : Visibility.Collapsed;
                if (inEdit && _editingPathIndex < _vectorPaths.Count)
                {
                    var editVp = _vectorPaths[_editingPathIndex];
                    EditPathFillButton.IsEnabled = editVp.IsClosed;
                    EditPathFillButton.IsChecked = editVp.IsFilled && editVp.IsClosed;
                }
            }

            if (EditLayerColorButton != null)
            {
                EditLayerColorButton.Visibility = inEdit ? Visibility.Visible : Visibility.Collapsed;
                if (inEdit && _editingPathIndex < _vectorPaths.Count && EditLayerColorSwatch != null)
                    EditLayerColorSwatch.Background = new SolidColorBrush(_vectorPaths[_editingPathIndex].Color);
            }

            // Sync shape-layer color swatch
            if (ShapeLayerColorSwatch != null && inShapeLayer)
            {
                var shapeLayer = _pencilLayers[_activePencilLayerIndex];
                var swatchColor = shapeLayer.Strokes.Count > 0 ? shapeLayer.Strokes[0].Color : _drawColor;
                ShapeLayerColorSwatch.Background = new SolidColorBrush(swatchColor);
            }

            if (inEdit && _selectedAnchorIndex >= 0)
            {
                var anchor = _vectorPaths[_editingPathIndex].Anchors[_selectedAnchorIndex];
                CornerTypeButton.IsChecked       = anchor.Type == AnchorPointType.Corner;
                BezierTypeButton.IsChecked       = anchor.Type == AnchorPointType.Bezier;
                BezierCornerTypeButton.IsChecked = anchor.Type == AnchorPointType.BezierCorner;
            }
            else if (inEdit && _editMultiSelectedAnchors.Count > 0)
            {
                var path = _vectorPaths[_editingPathIndex];
                var types = _editMultiSelectedAnchors
                    .Where(i => i >= 0 && i < path.Anchors.Count)
                    .Select(i => path.Anchors[i].Type)
                    .ToHashSet();
                CornerTypeButton.IsChecked       = types.Count == 1 && types.Contains(AnchorPointType.Corner);
                BezierTypeButton.IsChecked       = types.Count == 1 && types.Contains(AnchorPointType.Bezier);
                BezierCornerTypeButton.IsChecked = types.Count == 1 && types.Contains(AnchorPointType.BezierCorner);
            }
        }

        private void ToggleEditPathFill_Click(object sender, RoutedEventArgs e)
        {
            if (_editingPathIndex < 0 || _editingPathIndex >= _vectorPaths.Count) return;
            var vp = _vectorPaths[_editingPathIndex];
            if (!vp.IsClosed) return;
            SaveUndoState();
            vp.IsFilled = EditPathFillButton?.IsChecked == true;
            RefreshLayerList();
            RedrawCanvas();
        }

        private void EditLayerColor_Click(object sender, RoutedEventArgs e)
        {
            if (_editingPathIndex < 0 || _editingPathIndex >= _vectorPaths.Count) return;
            var vp = _vectorPaths[_editingPathIndex];
            var originalColor = vp.Color;
            var dlg = new ColorPickerDialog(vp.Color) { Owner = this };
            dlg.PreviewColorChanged += previewColor =>
            {
                vp.Color = previewColor;
                if (EditLayerColorSwatch != null)
                    EditLayerColorSwatch.Background = new SolidColorBrush(previewColor);
                RedrawCanvas();
            };
            if (dlg.ShowDialog() == true)
            {
                SaveUndoState();
                vp.Color = dlg.SelectedColor;
                UpdateAnchorEditPanelVisibility();
                RefreshLayerList();
                RedrawCanvas();
            }
            else
            {
                vp.Color = originalColor;
                if (EditLayerColorSwatch != null)
                    EditLayerColorSwatch.Background = new SolidColorBrush(originalColor);
                RedrawCanvas();
            }
        }

        /// <summary>Recolors all strokes in the selected shape layer to a new color chosen from the color picker.</summary>
        private void ShapeLayerColor_Click(object sender, RoutedEventArgs e)
        {
            if (_activePencilLayerIndex < 0 || _activePencilLayerIndex >= _pencilLayers.Count) return;
            var layer = _pencilLayers[_activePencilLayerIndex];
            if (!layer.IsShapeLayer || layer.Strokes.Count == 0) return;

            // Capture per-stroke originals so we can restore them if the user cancels.
            var originalColors = layer.Strokes.Select(s => s.Color).ToList();
            var initialColor = originalColors[0];

            var dlg = new ColorPickerDialog(initialColor) { Owner = this };
            dlg.PreviewColorChanged += previewColor =>
            {
                foreach (var s in layer.Strokes) s.Color = previewColor;
                if (ShapeLayerColorSwatch != null)
                    ShapeLayerColorSwatch.Background = new SolidColorBrush(previewColor);
                RedrawCanvas();
            };

            if (dlg.ShowDialog() == true)
            {
                // Restore originals before saving undo so the snapshot reflects the pre-change state.
                for (int i = 0; i < layer.Strokes.Count && i < originalColors.Count; i++)
                    layer.Strokes[i].Color = originalColors[i];
                SaveUndoState();
                foreach (var s in layer.Strokes) s.Color = dlg.SelectedColor;
                UpdateAnchorEditPanelVisibility();
                RedrawCanvas();
            }
            else
            {
                // Cancel: restore each stroke's original color.
                for (int i = 0; i < layer.Strokes.Count && i < originalColors.Count; i++)
                    layer.Strokes[i].Color = originalColors[i];
                if (ShapeLayerColorSwatch != null)
                    ShapeLayerColorSwatch.Background = new SolidColorBrush(initialColor);
                RedrawCanvas();
            }
        }

        private void DrawMoveModePaths(DrawingContext dc)
        {
            double ds = _displayScale;
            double anchorHalf = Math.Max(2.5, ds * 0.28);

            var cornerFill  = new SolidColorBrush(Color.FromArgb(0x99, 0x5C, 0xC8, 0xFF)); cornerFill.Freeze();
            var bezierFill  = new SolidColorBrush(Color.FromArgb(0x99, 0xA8, 0xFF, 0xA8)); bezierFill.Freeze();
            var anchorPen   = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), 1.0); anchorPen.Freeze();
            // Highlighted (selected) anchor appearance
            var selectedFill = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)); selectedFill.Freeze();
            var selectedPen  = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 1.5); selectedPen.Freeze();

            for (int pi = 0; pi < _vectorPaths.Count; pi++)
            {
                var path = _vectorPaths[pi];
                for (int ai = 0; ai < path.Anchors.Count; ai++)
                {
                    var anchor = path.Anchors[ai];
                    bool isSelected = _multiSelectedAnchors.Contains((pi, ai));
                    double ax = (anchor.Position.X + 0.5) * ds;
                    double ay = (anchor.Position.Y + 0.5) * ds;
                    var fill = isSelected ? selectedFill
                             : anchor.Type == AnchorPointType.Corner ? cornerFill : bezierFill;
                    var pen  = isSelected ? selectedPen : anchorPen;
                    double half = isSelected ? anchorHalf * 1.3 : anchorHalf;

                    if (anchor.Type == AnchorPointType.Corner)
                    {
                        dc.DrawRectangle(fill, pen,
                            new Rect(ax - half, ay - half, half * 2, half * 2));
                    }
                    else
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(ax, ay - half), true, true);
                            ctx.LineTo(new Point(ax + half, ay), false, false);
                            ctx.LineTo(new Point(ax, ay + half), false, false);
                            ctx.LineTo(new Point(ax - half, ay), false, false);
                        }
                        geo.Freeze();
                        dc.DrawGeometry(fill, pen, geo);
                    }
                }
            }
        }

        private bool TryShowAnchorContextMenu(Point screenPos)
        {
            // When multiple anchors are selected in edit mode, always show the batch type menu
            if (_editingPathIndex >= 0 && _editMultiSelectedAnchors.Count > 1)
            {
                int selCount = _editMultiSelectedAnchors.Count;
                var batchMenu = new ContextMenu { PlacementTarget = DrawCanvas };
                batchMenu.Items.Add(new MenuItem { Header = $"Set {selCount}  anchor type(s)", IsEnabled = false });
                batchMenu.Items.Add(new Separator());
                var cornerItem2       = new MenuItem { Header = "Corner" };
                var bezierItem2       = new MenuItem { Header = "Bezier Curve" };
                var bezierCornerItem2 = new MenuItem { Header = "Bezier Corner" };
                cornerItem2.Click       += (_, _) => ChangeSelectedAnchorType(AnchorPointType.Corner);
                bezierItem2.Click       += (_, _) => ChangeSelectedAnchorType(AnchorPointType.Bezier);
                bezierCornerItem2.Click += (_, _) => ChangeSelectedAnchorType(AnchorPointType.BezierCorner);
                batchMenu.Items.Add(cornerItem2);
                batchMenu.Items.Add(bezierItem2);
                batchMenu.Items.Add(bezierCornerItem2);
                batchMenu.IsOpen = true;
                return true;
            }

            double hitRadius = Math.Max(6.0, _displayScale * 0.6);
            int pathIdx = -1, anchorIdx = -1;

            // Prefer the currently editing path
            if (_editingPathIndex >= 0 && _editingPathIndex < _vectorPaths.Count)
            {
                var path = _vectorPaths[_editingPathIndex];
                double bestDist = hitRadius;
                for (int i = 0; i < path.Anchors.Count; i++)
                {
                    var anc = path.Anchors[i];
                    double cx = (anc.Position.X + 0.5) * _displayScale;
                    double cy = (anc.Position.Y + 0.5) * _displayScale;
                    double d = Math.Sqrt(Math.Pow(screenPos.X - cx, 2) + Math.Pow(screenPos.Y - cy, 2));
                    if (d < bestDist) { bestDist = d; pathIdx = _editingPathIndex; anchorIdx = i; }
                }
            }

            // Fall back to all paths when in Move mode
            if (pathIdx < 0 && _mode == DrawingMode.Move)
            {
                double bestDist = hitRadius;
                for (int pi = 0; pi < _vectorPaths.Count; pi++)
                {
                    var vp = _vectorPaths[pi];
                    for (int ai = 0; ai < vp.Anchors.Count; ai++)
                    {
                        var anc = vp.Anchors[ai];
                        double cx = (anc.Position.X + 0.5) * _displayScale;
                        double cy = (anc.Position.Y + 0.5) * _displayScale;
                        double d = Math.Sqrt(Math.Pow(screenPos.X - cx, 2) + Math.Pow(screenPos.Y - cy, 2));
                        if (d < bestDist) { bestDist = d; pathIdx = pi; anchorIdx = ai; }
                    }
                }
            }

            if (pathIdx < 0) return false;

            // Select the anchor so type-change handlers operate on it
            if (_editingPathIndex != pathIdx)
                EnterEditMode(pathIdx);
            _selectedAnchorIndex = anchorIdx;
            UpdateAnchorEditPanelVisibility();
            RedrawCanvas();

            var currentType = _vectorPaths[pathIdx].Anchors[anchorIdx].Type;

            var menu = new ContextMenu { PlacementTarget = DrawCanvas };

            var cornerItem      = new MenuItem { Header = "Corner", IsCheckable = true, IsChecked = currentType == AnchorPointType.Corner };
            var bezierItem      = new MenuItem { Header = "Bezier Curve", IsCheckable = true, IsChecked = currentType == AnchorPointType.Bezier };
            var bezierCornerItem = new MenuItem { Header = "Bezier Corner", IsCheckable = true, IsChecked = currentType == AnchorPointType.BezierCorner };
            var deleteItem      = new MenuItem { Header = "Delete anchor" };

            cornerItem.Click      += (_, _) => ChangeSelectedAnchorType(AnchorPointType.Corner);
            bezierItem.Click      += (_, _) => ChangeSelectedAnchorType(AnchorPointType.Bezier);
            bezierCornerItem.Click += (_, _) => ChangeSelectedAnchorType(AnchorPointType.BezierCorner);
            deleteItem.Click      += (_, _) => DeleteSelectedAnchor_Click(null!, null!);

            menu.Items.Add(new MenuItem { Header = "Anchor Type", IsEnabled = false });
            menu.Items.Add(new Separator());
            menu.Items.Add(cornerItem);
            menu.Items.Add(bezierItem);
            menu.Items.Add(bezierCornerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            return true;
        }


        /// Returns (anchorIndex, target) or (-1, None) if nothing was hit.
        /// </summary>
        private (int anchorIndex, DragTarget target) HitTestEditing(Point screenPos)
        {
            if (_editingPathIndex < 0) return (-1, DragTarget.None);
            var path = _vectorPaths[_editingPathIndex];
            double hitRadiusPx = Math.Max(5.0, _displayScale * 0.6);

            double Best(Point logicalPos)
            {
                double cx = (logicalPos.X + 0.5) * _displayScale;
                double cy = (logicalPos.Y + 0.5) * _displayScale;
                double dx = screenPos.X - cx;
                double dy = screenPos.Y - cy;
                return Math.Sqrt(dx * dx + dy * dy);
            }
            double BestF(Point floatPos)
            {
                double cx = floatPos.X * _displayScale;
                double cy = floatPos.Y * _displayScale;
                double dx = screenPos.X - cx;
                double dy = screenPos.Y - cy;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            // Pass 1: handles of the selected anchor (highest priority — keeps fine editing smooth)
            if (_selectedAnchorIndex >= 0 && _selectedAnchorIndex < path.Anchors.Count)
            {
                var sel = path.Anchors[_selectedAnchorIndex];
                if (sel.Type != AnchorPointType.Corner)
                {
                    if (BestF(sel.InHandle) <= hitRadiusPx)
                        return (_selectedAnchorIndex, DragTarget.InHandle);
                    if (BestF(sel.OutHandle) <= hitRadiusPx)
                        return (_selectedAnchorIndex, DragTarget.OutHandle);
                }
            }

            // Pass 2: handles of all other non-corner anchors — allows grabbing a handle directly
            // without having to select its anchor first, which is the key UX improvement
            int bestHandleIdx = -1;
            DragTarget bestHandleTarget = DragTarget.None;
            double bestHandleDist = hitRadiusPx;
            for (int i = 0; i < path.Anchors.Count; i++)
            {
                if (i == _selectedAnchorIndex) continue; // already tested above
                var anchor = path.Anchors[i];
                if (anchor.Type == AnchorPointType.Corner) continue;

                double dIn = BestF(anchor.InHandle);
                if (dIn < bestHandleDist) { bestHandleDist = dIn; bestHandleIdx = i; bestHandleTarget = DragTarget.InHandle; }
                double dOut = BestF(anchor.OutHandle);
                if (dOut < bestHandleDist) { bestHandleDist = dOut; bestHandleIdx = i; bestHandleTarget = DragTarget.OutHandle; }
            }
            if (bestHandleIdx >= 0)
                return (bestHandleIdx, bestHandleTarget);

            // Pass 3: anchor positions
            int bestIdx = -1;
            double bestDist = hitRadiusPx;
            for (int i = 0; i < path.Anchors.Count; i++)
            {
                double d = Best(path.Anchors[i].Position);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            if (bestIdx >= 0) return (bestIdx, DragTarget.Anchor);

            return (-1, DragTarget.None);
        }

        private (int pathIdx, int anchorIdx) HitTestAllAnchors(Point screenPos)
        {
            double hitRadius = Math.Max(8.0, _displayScale * 0.7);
            int bestPathIdx = -1, bestAnchorIdx = -1;
            double bestDist = hitRadius;
            for (int pi = 0; pi < _vectorPaths.Count; pi++)
            {
                var vp = _vectorPaths[pi];
                for (int ai = 0; ai < vp.Anchors.Count; ai++)
                {
                    var anc = vp.Anchors[ai];
                    double cx = (anc.Position.X + 0.5) * _displayScale;
                    double cy = (anc.Position.Y + 0.5) * _displayScale;
                    double dx = screenPos.X - cx, dy = screenPos.Y - cy;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < bestDist) { bestDist = d; bestPathIdx = pi; bestAnchorIdx = ai; }
                }
            }
            return (bestPathIdx, bestAnchorIdx);
        }

        /// <summary>Captures origin positions for all selected anchors and begins a multi-drag.</summary>
        private void StartMultiDrag(Point logicalPos)
        {
            _isDraggingMulti = true;
            _multiDragStartLogical = logicalPos;
            _multiDragOrigins.Clear();
            foreach (var (pi, ai) in _multiSelectedAnchors)
            {
                if (pi >= _vectorPaths.Count) continue;
                var vp = _vectorPaths[pi];
                if (ai >= vp.Anchors.Count) continue;
                var anc = vp.Anchors[ai];
                _multiDragOrigins.Add((pi, ai, anc.Position, anc.InHandle, anc.OutHandle));
            }
        }

        /// <summary>Captures origin positions for all anchors in <see cref="_editMultiSelectedAnchors"/> and begins a multi-drag within the editing path.</summary>
        private void StartMultiDragEdit(Point logicalPos)
        {
            _isDraggingMulti = true;
            _isDraggingInEdit = false;
            _multiDragStartLogical = logicalPos;
            _multiDragOrigins.Clear();
            if (_editingPathIndex < 0 || _editingPathIndex >= _vectorPaths.Count) return;
            var path = _vectorPaths[_editingPathIndex];
            foreach (var ai in _editMultiSelectedAnchors)
            {
                if (ai >= path.Anchors.Count) continue;
                var anc = path.Anchors[ai];
                _multiDragOrigins.Add((_editingPathIndex, ai, anc.Position, anc.InHandle, anc.OutHandle));
            }
        }

        private void FinishBoxSelection()
        {
            if (!_isBoxSelecting) return;
            _isBoxSelecting = false;

            double x1 = Math.Min(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
            double y1 = Math.Min(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);
            double x2 = Math.Max(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
            double y2 = Math.Max(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);

            // Only treat as a box selection when the drag was meaningful; a tiny drag (< 3 px) is treated as a click-deselect
            if (x2 - x1 >= 3 || y2 - y1 >= 3)
            {
                if (_editingPathIndex >= 0 && _editingPathIndex < _vectorPaths.Count)
                {
                    // Edit mode: select anchors within the editing path only
                    var path = _vectorPaths[_editingPathIndex];
                    for (int i = 0; i < path.Anchors.Count; i++)
                    {
                        var anc = path.Anchors[i];
                        double cx = (anc.Position.X + 0.5) * _displayScale;
                        double cy = (anc.Position.Y + 0.5) * _displayScale;
                        if (cx >= x1 && cx <= x2 && cy >= y1 && cy <= y2)
                            _editMultiSelectedAnchors.Add(i);
                    }
                }
                else
                {
                    // Move mode: select anchors across all paths
                    for (int pi = 0; pi < _vectorPaths.Count; pi++)
                    {
                        var vp = _vectorPaths[pi];
                        for (int ai = 0; ai < vp.Anchors.Count; ai++)
                        {
                            var anc = vp.Anchors[ai];
                            double cx = (anc.Position.X + 0.5) * _displayScale;
                            double cy = (anc.Position.Y + 0.5) * _displayScale;
                            if (cx >= x1 && cx <= x2 && cy >= y1 && cy <= y2)
                                _multiSelectedAnchors.Add((pi, ai));
                        }
                    }
                }
            }

            RemoveSelectionBoxOverlay();
            UpdateAnchorEditPanelVisibility();
            RedrawCanvas();
        }

        /// <summary>Removes the selection-box overlay element from the canvas, if present.</summary>
        private void RemoveSelectionBoxOverlay()
        {
            if (_selectionBoxOverlay != null)
            {
                if (DrawCanvas.Children.Contains(_selectionBoxOverlay))
                    DrawCanvas.Children.Remove(_selectionBoxOverlay);
                _selectionBoxOverlay = null;
            }
        }

        /// <summary>Creates or updates the rubber-band rectangle overlay on the canvas.</summary>
        private void UpdateSelectionBoxOverlay()
        {
            RemoveSelectionBoxOverlay();
            if (!_isBoxSelecting) return;

            double x1 = Math.Min(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
            double y1 = Math.Min(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);
            double x2 = Math.Max(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
            double y2 = Math.Max(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);

            var rect = new Rectangle
            {
                Width  = Math.Max(1, x2 - x1),
                Height = Math.Max(1, y2 - y1),
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x5C, 0xC8, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x5C, 0xC8, 0xFF)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, y1);
            _selectionBoxOverlay = rect;
            DrawCanvas.Children.Add(rect);
        }
    }
}
