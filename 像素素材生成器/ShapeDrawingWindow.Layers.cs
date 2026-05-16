using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Shapes;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator
{
    public partial class ShapeDrawingWindow : Window
    {
        private void PathListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDraggingLayer) return;
            if (PathListBox?.SelectedItem is not LayerItem item) return;

            if (item.Kind == LayerKind.Pencil)
            {
                // Selecting a pencil/shape layer: exit path edit mode, set active layer
                ExitEditMode();
                _activePencilLayerIndex = item.Index;
                var selectedLayer = _pencilLayers[item.Index];
                var targetMode = selectedLayer.IsShapeLayer ? DrawingMode.ShapeStamp : DrawingMode.Pencil;
                // SetMode would auto-create a shape layer, but we already have one selected;
                // guard against unnecessary creation by only calling if mode differs.
                if (_mode != targetMode)
                    SetMode(targetMode);
                else
                    UpdateAnchorEditPanelVisibility(); // mode unchanged: re-sync panel with the new layer
                RefreshLayerList();
                RedrawCanvas();
            }
            else // LayerKind.Path
            {
                if (_mode != DrawingMode.Move)
                    SetMode(DrawingMode.Move);
                if (item.Index != _editingPathIndex)
                    EnterEditMode(item.Index);
            }
        }

        private void ToggleLayerVisibility_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not LayerItem item) return;
            if (item.Kind == LayerKind.Pencil && item.Index < _pencilLayers.Count)
                _pencilLayers[item.Index].IsVisible = !_pencilLayers[item.Index].IsVisible;
            else if (item.Kind == LayerKind.Path && item.Index < _vectorPaths.Count)
                _vectorPaths[item.Index].IsVisible = !_vectorPaths[item.Index].IsVisible;
            // Prevent the click from bubbling to the ListBoxItem selection handler
            e.Handled = true;
            RefreshLayerList();
            RedrawCanvas();
        }

        private void PathListBox_DragMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(PathListBox);
            _dragLayerUiIndex = GetLayerUiIndexAtPoint(pt);
            _dragLayerStartPoint = pt;
            _isDraggingLayer = false;
        }

        private void PathListBox_DragMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragLayerUiIndex < 0) return;
            var pt = e.GetPosition(PathListBox);
            if (!_isDraggingLayer)
            {
                var dx = pt.X - _dragLayerStartPoint.X;
                var dy = pt.Y - _dragLayerStartPoint.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 6) return;
                _isDraggingLayer = true;
                PathListBox.CaptureMouse();
            }
            _dragInsertBeforeUiIndex = GetInsertBeforeUiIndex(pt);
            UpdateDragIndicator(_dragInsertBeforeUiIndex);
        }

        private void PathListBox_DragMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingLayer)
            {
                _dragLayerUiIndex = -1;
                return;
            }
            PathListBox.ReleaseMouseCapture();
            HideDragIndicator();
            var from = _dragLayerUiIndex;
            var insertBefore = _dragInsertBeforeUiIndex;
            _isDraggingLayer = false;
            _dragLayerUiIndex = -1;
            _dragInsertBeforeUiIndex = -1;
            if (from >= 0 && insertBefore >= 0 && insertBefore != from && insertBefore != from + 1)
            {
                SaveUndoState();
                MoveLayerInOrder(from, insertBefore);
                RefreshLayerList();
                RedrawCanvas();
            }
            e.Handled = true;
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            SaveUndoState();
            int cnt = _pencilLayers.Count(l => !l.IsShapeLayer) + 1;
            var newLayer = new PencilLayer { Name = $"Layer {cnt}" };
            _pencilLayers.Add(newLayer);
            _layerOrder.Add((LayerKind.Pencil, _pencilLayers.Count - 1));
            _activePencilLayerIndex = _pencilLayers.Count - 1;
            ExitEditMode();
            if (_mode != DrawingMode.Pencil)
                SetMode(DrawingMode.Pencil);
            RefreshLayerList();
            RedrawCanvas();
        }

        /// <summary>Delete the currently selected layer (pencil or path).</summary>
        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            if (PathListBox?.SelectedItem is not LayerItem item) return;

            if (item.Kind == LayerKind.Pencil)
            {
                // Always keep at least one pencil layer
                if (_pencilLayers.Count <= 1)
                {
                    SaveUndoState();
                    _pencilLayers[0].Strokes.Clear();
                    _pencilLayers[0].Name = "Layer 1";
                }
                else
                {
                    SaveUndoState();
                    RemovePencilLayerAt(item.Index);
                    _activePencilLayerIndex = Math.Clamp(_activePencilLayerIndex, 0, _pencilLayers.Count - 1);
                }
                ExitEditMode();
                RefreshLayerList();
                RedrawCanvas();
            }
            else // LayerKind.Path
            {
                SaveUndoState();
                RemoveVectorPathAt(item.Index);
                ExitEditMode();
                RefreshLayerList();
                RedrawCanvas();
            }
        }

        private void MoveLayerUp_Click(object sender, RoutedEventArgs e)
        {
            if (PathListBox?.SelectedItem is not LayerItem item) return;
            MoveLayer(item, up: true);
        }

        private void MoveLayerDown_Click(object sender, RoutedEventArgs e)
        {
            if (PathListBox?.SelectedItem is not LayerItem item) return;
            MoveLayer(item, up: false);
        }

        /// <summary>Moves a layer one position up or down in the unified <see cref="_layerOrder"/>, allowing free mixing of pencil and path layers.</summary>
        private void MoveLayer(LayerItem item, bool up)
        {
            int listIdx = _layerOrder.FindIndex(e => e.kind == item.Kind && e.index == item.Index);
            if (listIdx < 0) return;
            // "up" in the UI means visually higher in the list = higher render order = toward the end of _layerOrder
            // (RefreshLayerList shows _layerOrder in reverse, so end of list = top of panel).
            int newListIdx = up ? listIdx + 1 : listIdx - 1;
            if (newListIdx < 0 || newListIdx >= _layerOrder.Count) return;

            SaveUndoState();
            (_layerOrder[listIdx], _layerOrder[newListIdx]) = (_layerOrder[newListIdx], _layerOrder[listIdx]);
            RefreshLayerList();
            RedrawCanvas();
        }

        private void RemovePencilLayerAt(int kindIdx)
        {
            _pencilLayers.RemoveAt(kindIdx);
            _layerOrder.RemoveAll(e => e.kind == LayerKind.Pencil && e.index == kindIdx);
            for (int i = 0; i < _layerOrder.Count; i++)
            {
                var e = _layerOrder[i];
                if (e.kind == LayerKind.Pencil && e.index > kindIdx)
                    _layerOrder[i] = (e.kind, e.index - 1);
            }
        }

        /// <summary>Removes the vector path at <paramref name="kindIdx"/> and fixes up <see cref="_layerOrder"/>.</summary>
        private void RemoveVectorPathAt(int kindIdx)
        {
            _vectorPaths.RemoveAt(kindIdx);
            _layerOrder.RemoveAll(e => e.kind == LayerKind.Path && e.index == kindIdx);
            for (int i = 0; i < _layerOrder.Count; i++)
            {
                var e = _layerOrder[i];
                if (e.kind == LayerKind.Path && e.index > kindIdx)
                    _layerOrder[i] = (e.kind, e.index - 1);
            }
        }

        private int GetLayerUiIndexAtPoint(Point pt)
        {
            var hit = VisualTreeHelper.HitTest(PathListBox, pt);
            if (hit?.VisualHit == null) return -1;
            var dep = hit.VisualHit as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
                dep = VisualTreeHelper.GetParent(dep);
            if (dep is ListBoxItem lbi)
            {
                var idx = PathListBox.ItemContainerGenerator.IndexFromContainer(lbi);
                return idx >= 0 ? idx : -1;
            }
            return -1;
        }

        private int GetInsertBeforeUiIndex(Point ptInListBox)
        {
            int count = PathListBox.Items.Count;
            for (int i = 0; i < count; i++)
            {
                if (PathListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
                var topLeft = container.TransformToAncestor(PathListBox).Transform(new Point(0, 0));
                if (ptInListBox.Y < topLeft.Y + container.ActualHeight / 2) return i;
            }
            return count;
        }

        private double GetIndicatorY(int insertBeforeUiIdx)
        {
            int count = PathListBox.Items.Count;
            if (insertBeforeUiIdx >= 0 && insertBeforeUiIdx < count)
            {
                if (PathListBox.ItemContainerGenerator.ContainerFromIndex(insertBeforeUiIdx) is ListBoxItem container)
                    return container.TransformToAncestor(PathListBox).Transform(new Point(0, 0)).Y;
            }
            // Bottom of last item
            if (count > 0 && PathListBox.ItemContainerGenerator.ContainerFromIndex(count - 1) is ListBoxItem last)
                return last.TransformToAncestor(PathListBox).Transform(new Point(0, last.ActualHeight)).Y;
            return 0;
        }

        private void UpdateDragIndicator(int insertBeforeUiIdx)
        {
            var layer = AdornerLayer.GetAdornerLayer(PathListBox);
            if (layer == null) return;
            if (_dragInsertAdorner == null)
            {
                var accent = TryFindResource("Accent") as SolidColorBrush
                             ?? new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF));
                _dragInsertAdorner = new DragInsertAdorner(PathListBox, accent);
                layer.Add(_dragInsertAdorner);
            }
            ((DragInsertAdorner)_dragInsertAdorner).SetY(GetIndicatorY(insertBeforeUiIdx));
            _dragInsertAdorner.Visibility = Visibility.Visible;
        }

        private void HideDragIndicator()
        {
            if (_dragInsertAdorner != null)
                _dragInsertAdorner.Visibility = Visibility.Collapsed;
        }

        private void MoveLayerInOrder(int fromUiIdx, int insertBeforeUiIdx)
        {
            int count = _layerOrder.Count;
            if (count < 2 || fromUiIdx < 0 || fromUiIdx >= count) return;
            if (insertBeforeUiIdx == fromUiIdx || insertBeforeUiIdx == fromUiIdx + 1) return;

            // Work in UI order (reversed _layerOrder: index 0 = top of panel = last in render order)
            var uiOrder = _layerOrder.AsEnumerable().Reverse().ToList();
            var item = uiOrder[fromUiIdx];
            uiOrder.RemoveAt(fromUiIdx);

            int insertIdx = insertBeforeUiIdx > fromUiIdx ? insertBeforeUiIdx - 1 : insertBeforeUiIdx;
            insertIdx = Math.Clamp(insertIdx, 0, uiOrder.Count);
            uiOrder.Insert(insertIdx, item);

            // Write back as render order (reversed)
            uiOrder.Reverse();
            _layerOrder.Clear();
            _layerOrder.AddRange(uiOrder);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private LayerItem? GetLayerItemAtPoint(Point pt)
        {
            var hit = VisualTreeHelper.HitTest(PathListBox, pt);
            if (hit?.VisualHit == null) return null;
            var dep = hit.VisualHit as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
                dep = VisualTreeHelper.GetParent(dep);
            return (dep as ListBoxItem)?.Content as LayerItem;
        }

        private void PathListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = GetLayerItemAtPoint(e.GetPosition(PathListBox));
            if (item == null) return;
            StartRenameLayer(item);
            e.Handled = true;
        }

        /// <summary>
        /// Right-click on a layer item shows a context menu with rename, move up/down, and delete commands.
        /// The menu is built dynamically so each item captures the target <see cref="LayerItem"/> via a closure.
        /// </summary>
        private void PathListBox_LayerContextMenu(object sender, MouseButtonEventArgs e)
        {
            var item = GetLayerItemAtPoint(e.GetPosition(PathListBox));
            if (item == null) return;

            var menu = new ContextMenu();

            var renameItem = new MenuItem { Header = "Rename" };
            renameItem.Click += (_, _) =>
            {
                PathListBox.SelectedItem = item;
                StartRenameLayer(item);
            };

            var moveUpItem   = new MenuItem { Header = "Move Up" };
            moveUpItem.Click += (_, _) => MoveLayer(item, up: true);

            var moveDownItem   = new MenuItem { Header = "Move Down" };
            moveDownItem.Click += (_, _) => MoveLayer(item, up: false);

            var deleteItem   = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                // Temporarily select the item so DeleteLayer_Click can resolve it
                var prev = PathListBox.SelectedItem;
                PathListBox.SelectedItem = item;
                DeleteLayer_Click(deleteItem, new RoutedEventArgs());
                if (PathListBox.SelectedItem == item) // restore if it wasn't consumed
                    PathListBox.SelectedItem = prev;
            };

            // Disable move commands when they would be no-ops
            int listIdx = _layerOrder.FindIndex(e => e.kind == item.Kind && e.index == item.Index);
            int uiCount = _layerOrder.Count;
            moveUpItem.IsEnabled   = listIdx >= 0 && listIdx < uiCount - 1;
            moveDownItem.IsEnabled = listIdx > 0;

            menu.Items.Add(renameItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(moveUpItem);
            menu.Items.Add(moveDownItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(deleteItem);

            menu.PlacementTarget = PathListBox;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void RefreshLayerList()
        {
            if (PathListBox == null) return;

            // If a rename is in progress, commit the data-model update inline so the
            // rebuilt list reflects the new name without a second recursive refresh.
            if (_isRenamingLayer && _renamingItem != null)
            {
                var pending = _renamingItem;
                var newName = pending.EditName.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    if (pending.Kind == LayerKind.Pencil && pending.Index < _pencilLayers.Count)
                        _pencilLayers[pending.Index].Name = newName;
                    else if (pending.Kind == LayerKind.Path && pending.Index < _vectorPaths.Count)
                        _vectorPaths[pending.Index].Name = newName;
                }
                pending.IsRenaming = false;
                _renamingItem = null;
                _isRenamingLayer = false;
            }

            if (_isRefreshingLayerList) return;
            _isRefreshingLayerList = true;
            try
            {
            var items = new List<LayerItem>();
            // Iterate in reverse so the topmost-rendered layer (last in _layerOrder) appears at the top of the list,
            // matching Photoshop layer panel conventions.
            foreach (var (kind, kindIdx) in _layerOrder.AsEnumerable().Reverse())
            {
                if (kind == LayerKind.Pencil && kindIdx < _pencilLayers.Count)
                {
                    var layer = _pencilLayers[kindIdx];
                    items.Add(new LayerItem
                    {
                        Icon = layer.IsShapeLayer ? "⬟" : "✏",
                        DisplayName = layer.Name,
                        Info = $"{layer.Strokes.Count} strokes",
                        Kind = LayerKind.Pencil,
                        Index = kindIdx,
                        IsVisible = layer.IsVisible
                    });
                }
                else if (kind == LayerKind.Path && kindIdx < _vectorPaths.Count)
                {
                    var vp = _vectorPaths[kindIdx];
                    items.Add(new LayerItem
                    {
                        Icon = "📐",
                        DisplayName = vp.Name,
                        Info = $"{vp.Anchors.Count} anchor(s){(vp.IsClosed ? " | closed" : "")}{(vp.IsFilled && vp.IsClosed ? " | filled" : "")}",
                        Kind = LayerKind.Path,
                        Index = kindIdx,
                        IsVisible = vp.IsVisible
                    });
                }
            }
            PathListBox.ItemsSource = items;

            // Sync selection
            if (_editingPathIndex >= 0)
            {
                var sel = items.FirstOrDefault(it => it.Kind == LayerKind.Path && it.Index == _editingPathIndex);
                PathListBox.SelectedItem = sel;
            }
            else
            {
                var sel = items.FirstOrDefault(it => it.Kind == LayerKind.Pencil && it.Index == _activePencilLayerIndex);
                PathListBox.SelectedItem = sel;
            }
            }
            finally
            {
                _isRefreshingLayerList = false;
            }
        }

        private void StartRenameLayer(LayerItem item)
        {
            // Commit any other pending rename first
            if (_isRenamingLayer && _renamingItem != null && _renamingItem != item)
                CommitRenameLayer(_renamingItem);

            _renamingItem = item;
            _isRenamingLayer = true;
            item.EditName = item.DisplayName;
            item.IsRenaming = true;

            // Defer focus so WPF has time to show the TextBox after the visibility change
            Dispatcher.BeginInvoke(() =>
            {
                if (PathListBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem lbi)
                {
                    var tb = FindVisualChild<TextBox>(lbi);
                    if (tb != null) { tb.Focus(); tb.SelectAll(); }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>Persists the edited name to the data model and exits rename mode.</summary>
        private void CommitRenameLayer(LayerItem item)
        {
            if (!_isRenamingLayer || item != _renamingItem) return;

            var newName = item.EditName.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                if (item.Kind == LayerKind.Pencil && item.Index < _pencilLayers.Count)
                    _pencilLayers[item.Index].Name = newName;
                else if (item.Kind == LayerKind.Path && item.Index < _vectorPaths.Count)
                    _vectorPaths[item.Index].Name = newName;
            }

            item.IsRenaming = false;
            _renamingItem = null;
            _isRenamingLayer = false;
            RefreshLayerList();
        }

        /// <summary>Cancels rename without saving; restores the original display name via binding.</summary>
        private void CancelRenameLayer(LayerItem item)
        {
            if (!_isRenamingLayer || item != _renamingItem) return;
            item.IsRenaming = false;
            _renamingItem = null;
            _isRenamingLayer = false;
            // INotifyPropertyChanged propagates IsNotRenaming → no full list rebuild needed
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext is LayerItem item)
                CommitRenameLayer(item);
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if ((sender as TextBox)?.DataContext is not LayerItem item) return;
            if (e.Key == Key.Enter)        { CommitRenameLayer(item); e.Handled = true; }
            else if (e.Key == Key.Escape)  { CancelRenameLayer(item); e.Handled = true; }
        }
    }
}
