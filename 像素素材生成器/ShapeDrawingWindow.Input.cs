using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private Point ToLogicalPixel(Point rawPos)
        {
            var lx = rawPos.X / _displayScale;
            var ly = rawPos.Y / _displayScale;
            var ix = Math.Clamp((int)Math.Floor(lx), 0, (int)_canvasSize - 1);
            var iy = Math.Clamp((int)Math.Floor(ly), 0, (int)_canvasSize - 1);
            return new Point(ix, iy);
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var screenPos = e.GetPosition(DrawCanvas);
            var pos = ToLogicalPixel(screenPos);
            _lastMousePos = pos;

            // --- Path edit mode: select / drag anchors and handles ---
            if (_editingPathIndex >= 0)
            {
                var ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var (hitIdx, hitTarget) = HitTestEditing(screenPos);
                if (hitIdx >= 0)
                {
                    if (hitTarget != DragTarget.Anchor)
                    {
                        // Handle hit: single-select and drag the handle only
                        SaveUndoState();
                        _editMultiSelectedAnchors.Clear();
                        _selectedAnchorIndex = hitIdx;
                        _dragTarget = hitTarget;
                        _isDraggingInEdit = true;
                        DrawCanvas.CaptureMouse();
                        UpdateAnchorEditPanelVisibility();
                    }
                    else if (ctrlHeld)
                    {
                        // Ctrl+click: toggle anchor in multi-selection, no drag
                        if (!_editMultiSelectedAnchors.Remove(hitIdx))
                            _editMultiSelectedAnchors.Add(hitIdx);
                        _selectedAnchorIndex = hitIdx;
                        UpdateAnchorEditPanelVisibility();
                    }
                    else if (_editMultiSelectedAnchors.Count > 1 && _editMultiSelectedAnchors.Contains(hitIdx))
                    {
                        // Click on an already-multi-selected anchor: batch-drag all selected anchors
                        SaveUndoState();
                        _selectedAnchorIndex = hitIdx;
                        StartMultiDragEdit(pos);
                        DrawCanvas.CaptureMouse();
                    }
                    else
                    {
                        // Normal anchor click: single select + drag
                        SaveUndoState();
                        _editMultiSelectedAnchors.Clear();
                        _selectedAnchorIndex = hitIdx;
                        _dragTarget = DragTarget.Anchor;
                        _isDraggingInEdit = true;
                        DrawCanvas.CaptureMouse();
                        UpdateAnchorEditPanelVisibility();
                    }
                }
                else
                {
                    // Click on empty area: clear selection (unless Ctrl) and begin rubber-band
                    if (!ctrlHeld)
                    {
                        _editMultiSelectedAnchors.Clear();
                        _selectedAnchorIndex = -1;
                        UpdateAnchorEditPanelVisibility();
                    }
                    _isBoxSelecting = true;
                    _boxSelectStartScreen = screenPos;
                    _boxSelectCurrentScreen = screenPos;
                    DrawCanvas.CaptureMouse();
                }
                RedrawCanvas();
                return;
            }

            // Move/Select mode: rubber-band selection and multi-anchor drag
            if (_mode == DrawingMode.Move && _editingPathIndex < 0)
            {
                var ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                var (hitPathIdx, hitAnchorIdx) = HitTestAllAnchors(screenPos);

                if (hitPathIdx >= 0)
                {
                    var key = (hitPathIdx, hitAnchorIdx);

                    if (ctrlHeld)
                    {
                        // Ctrl+click: toggle anchor in multi-selection, stay out of edit mode
                        if (!_multiSelectedAnchors.Remove(key))
                            _multiSelectedAnchors.Add(key);
                        RedrawCanvas();
                        return;
                    }

                    if (_multiSelectedAnchors.Count > 1 && _multiSelectedAnchors.Contains(key))
                    {
                        // Click on a multi-selected anchor: drag all selected anchors together
                        SaveUndoState();
                        StartMultiDrag(pos);
                        DrawCanvas.CaptureMouse();
                        RedrawCanvas();
                        return;
                    }

                    // Default: single-click on anchor enters edit mode for that path
                    // (restores Bézier handle editing; also fires after "finish editing")
                    SaveUndoState();
                    EnterEditMode(hitPathIdx);   // clears _multiSelectedAnchors
                    _selectedAnchorIndex = hitAnchorIdx;
                    _dragTarget = DragTarget.Anchor;
                    _isDraggingInEdit = true;
                    DrawCanvas.CaptureMouse();
                    UpdateAnchorEditPanelVisibility();
                    RedrawCanvas();
                    return;
                }
                else
                {
                    // Click on empty canvas: clear selection (unless Ctrl) and begin rubber-band
                    if (!ctrlHeld) _multiSelectedAnchors.Clear();
                    _isBoxSelecting = true;
                    _boxSelectStartScreen = screenPos;
                    _boxSelectCurrentScreen = screenPos;
                    DrawCanvas.CaptureMouse();
                    RedrawCanvas();
                }
                return;
            }

            if (_mode == DrawingMode.Path)
            {
                // Check if click is near the first anchor — offer to close the path
                if (_currentPathAnchors.Count >= 2)
                {
                    var firstAnchor = _currentPathAnchors[0];
                    double snapRadius = Math.Max(5.0, _displayScale * 0.6);
                    double fcx = (firstAnchor.X + 0.5) * _displayScale;
                    double fcy = (firstAnchor.Y + 0.5) * _displayScale;
                    double fdx = screenPos.X - fcx, fdy = screenPos.Y - fcy;
                    if (Math.Sqrt(fdx * fdx + fdy * fdy) <= snapRadius)
                    {
                        var result = MessageBox.Show(this, "Close the path?", "Close Path",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result == MessageBoxResult.Yes)
                        {
                            CommitCurrentPath(closePath: true);
                            RedrawCanvas();
                            return;
                        }
                        // User chose No — fall through to add the point normally
                    }
                }

                // In path mode, each click places an anchor point
                SaveUndoState();
                _currentPathAnchors.Add(pos);
                RedrawCanvas();
                return;
            }

            // Pencil mode
            var eraserOn = EraserToggle?.IsChecked == true;
            if (_mode == DrawingMode.ShapeStamp)
            {
                SaveUndoState();
                _isStamping = true;
                _shapeStartPos = pos;
                _shapePreviewEnd = pos;
                DrawCanvas.CaptureMouse();
                ScheduleRedraw();
                return;
            }

            if (eraserOn)
            {
                SaveUndoState();
                _isErasing = true;
                _lastErasePixel = pos;
                EraseAt(pos);
                DrawCanvas.CaptureMouse();
                RedrawCanvas();
                return;
            }

            SaveUndoState();
            _isDrawing = true;
            _currentStroke = new Stroke { Width = _toolSize, Color = _drawColor };
            _currentStroke.Points.Add(pos);
            DrawCanvas.CaptureMouse();
            RedrawCanvas();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var rawPos    = e.GetPosition(DrawCanvas);
            var pos       = ToLogicalPixel(rawPos);
            _lastMousePos = pos;

            // --- Path edit mode dragging ---
            if (_editingPathIndex >= 0 && _isDraggingInEdit && e.LeftButton == MouseButtonState.Pressed)
            {
                var path = _vectorPaths[_editingPathIndex];
                if (_selectedAnchorIndex >= 0 && _selectedAnchorIndex < path.Anchors.Count)
                {
                    var anchor = path.Anchors[_selectedAnchorIndex];
                    if (_dragTarget == DragTarget.Anchor)
                    {
                        var delta = new Point(pos.X - anchor.Position.X, pos.Y - anchor.Position.Y);
                        anchor.Position  = pos;
                        // Move handles with the anchor to keep relative offsets
                        anchor.InHandle  = new Point(anchor.InHandle.X + delta.X,  anchor.InHandle.Y + delta.Y);
                        anchor.OutHandle = new Point(anchor.OutHandle.X + delta.X, anchor.OutHandle.Y + delta.Y);
                    }
                    else if (_dragTarget == DragTarget.InHandle)
                    {
                        var floatPos = ToLogicalCanvas(rawPos);
                        anchor.InHandle = floatPos;
                        // Mirror OutHandle about pixel center for smooth Bézier
                        if (anchor.Type == AnchorPointType.Bezier)
                        {
                            var c = anchor.Center;
                            anchor.OutHandle = new Point(2 * c.X - floatPos.X, 2 * c.Y - floatPos.Y);
                        }
                    }
                    else if (_dragTarget == DragTarget.OutHandle)
                    {
                        var floatPos = ToLogicalCanvas(rawPos);
                        anchor.OutHandle = floatPos;
                        // Mirror InHandle about pixel center for smooth Bézier
                        if (anchor.Type == AnchorPointType.Bezier)
                        {
                            var c = anchor.Center;
                            anchor.InHandle = new Point(2 * c.X - floatPos.X, 2 * c.Y - floatPos.Y);
                        }
                    }
                    ScheduleRedraw();
                }
                return;
            }

            if (_editingPathIndex >= 0)
            {
                if (_isDraggingMulti && e.LeftButton == MouseButtonState.Pressed)
                {
                    var deltaX = pos.X - _multiDragStartLogical.X;
                    var deltaY = pos.Y - _multiDragStartLogical.Y;
                    foreach (var (pi, ai, origPos, origIn, origOut) in _multiDragOrigins)
                    {
                        if (pi >= _vectorPaths.Count) continue;
                        var vp = _vectorPaths[pi];
                        if (ai >= vp.Anchors.Count) continue;
                        var anc = vp.Anchors[ai];
                        var newX = Math.Clamp((int)Math.Round(origPos.X + deltaX), 0, (int)_canvasSize - 1);
                        var newY = Math.Clamp((int)Math.Round(origPos.Y + deltaY), 0, (int)_canvasSize - 1);
                        var dxActual = newX - (int)origPos.X;
                        var dyActual = newY - (int)origPos.Y;
                        anc.Position  = new Point(newX, newY);
                        anc.InHandle  = new Point(origIn.X  + dxActual, origIn.Y  + dyActual);
                        anc.OutHandle = new Point(origOut.X + dxActual, origOut.Y + dyActual);
                    }
                    ScheduleRedraw();
                    return;
                }
                if (_isBoxSelecting && e.LeftButton == MouseButtonState.Pressed)
                {
                    _boxSelectCurrentScreen = rawPos;
                    UpdateSelectionBoxOverlay();
                    return;
                }
                // Hover in edit mode — just update cursor
                UpdateCursorOverlay();
                return;
            }

            // Move/Select mode: multi-drag and rubber-band update
            if (_mode == DrawingMode.Move && _editingPathIndex < 0)
            {
                if (_isDraggingMulti && e.LeftButton == MouseButtonState.Pressed)
                {
                    var deltaX = pos.X - _multiDragStartLogical.X;
                    var deltaY = pos.Y - _multiDragStartLogical.Y;
                    foreach (var (pi, ai, origPos, origIn, origOut) in _multiDragOrigins)
                    {
                        if (pi >= _vectorPaths.Count) continue;
                        var vp = _vectorPaths[pi];
                        if (ai >= vp.Anchors.Count) continue;
                        var anc = vp.Anchors[ai];
                        var newX = Math.Clamp((int)Math.Round(origPos.X + deltaX), 0, (int)_canvasSize - 1);
                        var newY = Math.Clamp((int)Math.Round(origPos.Y + deltaY), 0, (int)_canvasSize - 1);
                        var dxActual = newX - (int)origPos.X;
                        var dyActual = newY - (int)origPos.Y;
                        anc.Position  = new Point(newX, newY);
                        anc.InHandle  = new Point(origIn.X  + dxActual, origIn.Y  + dyActual);
                        anc.OutHandle = new Point(origOut.X + dxActual, origOut.Y + dyActual);
                    }
                    ScheduleRedraw();
                    return;
                }
                if (_isBoxSelecting && e.LeftButton == MouseButtonState.Pressed)
                {
                    _boxSelectCurrentScreen = rawPos;
                    UpdateSelectionBoxOverlay();
                    return;
                }
                // Pure hover in Move mode — no brush-size cursor overlay needed
                return;
            }

            if (_mode == DrawingMode.Path)
            {
                // Redraw to update the preview line from last anchor to cursor
                if (_currentPathAnchors.Count > 0)
                    ScheduleRedraw();
                UpdateCursorOverlay();
                return;
            }

            if (_isStamping && e.LeftButton == MouseButtonState.Pressed)
            {
                _shapePreviewEnd = pos;
                UpdateCursorOverlay();
                ScheduleRedraw();
                return;
            }

            if (_isErasing && e.LeftButton == MouseButtonState.Pressed)
            {
                // interpolate erase path from last erase pixel to current
                if (_lastErasePixel.HasValue)
                {
                    var last = _lastErasePixel.Value;
                    var lx = (int)last.X;
                    var ly = (int)last.Y;
                    var cx = (int)pos.X;
                    var cy = (int)pos.Y;
                    if (lx == cx && ly == cy)
                    {
                        EraseAt(pos);
                    }
                    else
                    {
                        foreach (var pnt in BresenhamLinePoints(lx, ly, cx, cy))
                        {
                            EraseAt(new Point(pnt.X, pnt.Y));
                        }
                    }
                    _lastErasePixel = pos;
                }
                else
                {
                    _lastErasePixel = pos;
                    EraseAt(pos);
                }
                // erasing mutates strokes -> schedule a full redraw at lower priority
                // but update cursor overlay immediately so the indicator follows the mouse responsively
                UpdateCursorOverlay();
                ScheduleRedraw();
                return;
            }

            if (_isDrawing && e.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentStroke != null)
                {
                    var pts = _currentStroke.Points;
                    // If Shift is held, constrain the stroke to a straight line from the
                    // stroke start to the current cursor position. Otherwise behave as freehand.

                    var shiftDown = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                    if (shiftDown && pts.Count > 0)
                    {
                        // compute straight line from first point to current pos
                        var start = pts[0];
                        var sx = (int)start.X;
                        var sy = (int)start.Y;
                        var cx = (int)pos.X;
                        var cy = (int)pos.Y;
                        // replace current stroke points with the straight-line points
                        pts.Clear();
                        foreach (var pnt in BresenhamLinePoints(sx, sy, cx, cy))
                        {
                            pts.Add(new Point(pnt.X, pnt.Y));
                        }
                    }
                    else
                    {
                        // freehand: interpolate between last point and current to avoid gaps
                        if (pts.Count == 0)
                        {
                            pts.Add(pos);
                        }
                        else
                        {
                            var last = pts[^1];
                            var lx = (int)last.X;
                            var ly = (int)last.Y;
                            var cx = (int)pos.X;
                            var cy = (int)pos.Y;
                            // If same as last, nothing to do
                            if (lx == cx && ly == cy)
                            {
                                // no-op
                            }
                            else
                            {
                                foreach (var pnt in BresenhamLinePoints(lx, ly, cx, cy))
                                {
                                    // avoid adding duplicate consecutive points
                                    var lp = pts.Count > 0 ? pts[^1] : default;
                                    if (pts.Count == 0 || lp.X != pnt.X || lp.Y != pnt.Y)
                                        pts.Add(new Point(pnt.X, pnt.Y));
                                }
                            }
                        }
                    }
                }
                // drawing mutates strokes -> full redraw
                // update cursor overlay immediately for responsive feedback and defer the heavy
                // bitmap redraw to a lower-priority dispatcher call to avoid input lag
                UpdateCursorOverlay();
                ScheduleRedraw();
                return;
            }
            // only update cursor overlay for pure hover (no stroke mutation)
            UpdateCursorOverlay();
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Stop edit-mode drag
            if (_isDraggingInEdit)
            {
                _isDraggingInEdit = false;
                _dragTarget = DragTarget.None;
                DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
                return;
            }

            // Stop multi-anchor drag
            if (_isDraggingMulti)
            {
                _isDraggingMulti = false;
                _multiDragOrigins.Clear();
                DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
                return;
            }

            // Complete rubber-band box selection
            if (_isBoxSelecting)
            {
                _boxSelectCurrentScreen = e.GetPosition(DrawCanvas);
                FinishBoxSelection();
                DrawCanvas.ReleaseMouseCapture();
                return;
            }

            // Path mode handles clicks in MouseLeftButtonDown; nothing to do on release
            if (_mode == DrawingMode.Path) return;

            // Finalize shape path placement
            if (_isStamping)
            {
                _isStamping = false;
                CommitShapePath();
                DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
                return;
            }

            if (_isErasing)
            {
                _isErasing = false;
                _lastErasePixel = null;
                DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
                return;
            }

            if (!_isDrawing) return;
                _isDrawing = false;
                if (_currentStroke != null && _currentStroke.Points.Count >= 1)
                {
                    ActivePencilLayer?.Strokes.Add(_currentStroke);
                }
                _currentStroke = null;
                DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var screenPos = e.GetPosition(DrawCanvas);

            // Right-click on an anchor: show type-switching context menu
            // Works in Move mode (all paths) and whenever a path is being edited
            if (_mode == DrawingMode.Move || _editingPathIndex >= 0)
            {
                if (TryShowAnchorContextMenu(screenPos))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_mode == DrawingMode.Path)
            {
                // In path mode: right-click finishes/stops the current path
                if (_currentPathAnchors.Count >= 2)
                {
                    CommitCurrentPath();
                }
                else
                {
                    // Not enough anchors for a valid path, just cancel
                    if (_currentPathAnchors.Count > 0)
                    {
                        SaveUndoState();
                        _currentPathAnchors.Clear();
                    }
                }
                RedrawCanvas();
                return;
            }

            // Pencil mode: no action on right-click (use Ctrl+Z to undo)
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _lastMousePos = null;
            UpdateCursorOverlay();
            if (_mode == DrawingMode.Path)
                ScheduleRedraw();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Don't steal keys from text inputs
            if (Keyboard.FocusedElement is TextBox or PasswordBox) return;

            var settings = SettingsService.Current;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            // Undo
            if (settings.IsMatch(ShortcutAction.Undo, key, mods))
            {
                Undo();
                e.Handled = true;
                return;
            }

            // Redo
            if (settings.IsMatch(ShortcutAction.Redo, key, mods))
            {
                Redo();
                e.Handled = true;
                return;
            }

            // Clear canvas
            if (settings.IsMatch(ShortcutAction.ClearCanvas, key, mods))
            {
                Clear_Click(sender, e);
                e.Handled = true;
                return;
            }

            // Toggle eraser (pencil mode only)
            if (settings.IsMatch(ShortcutAction.ToggleEraser, key, mods) && _mode == DrawingMode.Pencil)
            {
                if (EraserToggle != null)
                    EraserToggle.IsChecked = !(EraserToggle.IsChecked == true);
                e.Handled = true;
                return;
            }

            // Brush / shape size — [ decreases, ] increases
            if (settings.IsMatch(ShortcutAction.BrushSizeIncrease, key, mods))
            {
                SetToolSize(_toolSize + 1);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.BrushSizeDecrease, key, mods))
            {
                SetToolSize(_toolSize - 1);
                e.Handled = true;
                return;
            }

            // Drawing-mode switches
            if (settings.IsMatch(ShortcutAction.DrawingModePencil, key, mods))
            {
                SetMode(DrawingMode.Pencil);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.DrawingModePath, key, mods))
            {
                SetMode(DrawingMode.Path);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.DrawingModeMove, key, mods))
            {
                SetMode(DrawingMode.Move);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.DrawingModeShape, key, mods))
            {
                SetMode(DrawingMode.ShapeStamp);
                e.Handled = true;
                return;
            }

            if (_mode == DrawingMode.Path)
            {
                if (settings.IsMatch(ShortcutAction.FinishPath, key, mods))
                {
                    if (_currentPathAnchors.Count >= 2)
                    {
                        CommitCurrentPath();
                        RedrawCanvas();
                    }
                    e.Handled = true;
                }
                else if (settings.IsMatch(ShortcutAction.Cancel, key, mods))
                {
                    if (_currentPathAnchors.Count > 0)
                    {
                        SaveUndoState();
                        _currentPathAnchors.Clear();
                        RedrawCanvas();
                    }
                    e.Handled = true;
                }
            }
            else if (settings.IsMatch(ShortcutAction.Cancel, key, mods) && _editingPathIndex >= 0 && (_editMultiSelectedAnchors.Count > 0 || _isBoxSelecting))
            {
                // Escape in edit mode: clear multi-selection and any active rubber-band
                _editMultiSelectedAnchors.Clear();
                _selectedAnchorIndex = -1;
                _isBoxSelecting = false;
                _isDraggingMulti = false;
                _multiDragOrigins.Clear();
                RemoveSelectionBoxOverlay();
                if (DrawCanvas.IsMouseCaptured) DrawCanvas.ReleaseMouseCapture();
                UpdateAnchorEditPanelVisibility();
                RedrawCanvas();
                e.Handled = true;
            }
            else if (settings.IsMatch(ShortcutAction.Cancel, key, mods) && _mode == DrawingMode.Move && _editingPathIndex < 0)
            {
                // Escape clears multi-selection and cancels any active rubber-band or drag
                _multiSelectedAnchors.Clear();
                _isBoxSelecting = false;
                _isDraggingMulti = false;
                _multiDragOrigins.Clear();
                RemoveSelectionBoxOverlay();
                if (DrawCanvas.IsMouseCaptured) DrawCanvas.ReleaseMouseCapture();
                RedrawCanvas();
                e.Handled = true;
            }

            // Delete key: remove the selected layer in the layer list
            if (!e.Handled && key == Key.Delete && PathListBox?.SelectedItem is LayerItem)
            {
                DeleteLayer_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
