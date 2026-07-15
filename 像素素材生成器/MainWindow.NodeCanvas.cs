using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Gpu;
using PixelAssetGenerator.Services;
using ExportFormat = PixelAssetGenerator.Services.ExportService.ExportFormat;

namespace PixelAssetGenerator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static bool IsElementInsideNodeOrPort(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is FrameworkElement fe && (fe.DataContext is NodeViewModel || fe.DataContext is NodePortViewModel))
                {
                    return true;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        private static bool IsElementInsideConnection(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is FrameworkElement fe && fe.DataContext is NodeConnectionViewModel)
                {
                    return true;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        private bool NodeCanvasFilter(object? obj)
        {
            if (obj is not NodeViewModel node) return false;
            return true;
        }

        private bool NodeConnectionsFilter(object? obj)
        {
            if (obj is not NodeConnectionViewModel conn) return false;
            if (conn.IsPreview) return true;
            return true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {

            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox || Keyboard.FocusedElement is ComboBox)
            {
                return;
            }

            var settings = SettingsService.Current;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (key == Key.F2 && mods == ModifierKeys.None && SelectedNode != null)
            {
                RenameNode(SelectedNode);
                e.Handled = true;
                return;
            }

            if (key == Key.D && mods == ModifierKeys.Control && SelectedNode != null)
            {
                DuplicateContextSelection(SelectedNode);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.SelectAll, key, mods))
            {
                SelectAllNodes();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.CopyNodes, key, mods))
            {
                CopySelectedNodes();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.PasteNodes, key, mods))
            {
                PasteClipboardAtMouse();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.TogglePreviewGrid, key, mods))
            {
                _showPreviewPixelGrid = !_showPreviewPixelGrid;
                try
                {
                    if (PreviewGridToggleButton != null)
                        PreviewGridToggleButton.IsChecked = _showPreviewPixelGrid;
                }
                catch { }
                UpdatePreviewPixelGridOverlay();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.ToggleTilePreview, key, mods))
            {
                SetTiledPreviewMode(!_tiledPreviewEnabled);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.FitPreview, key, mods))
            {
                UpdateFitScale();
                SetZoom(1d);
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.DeleteSelected, key, mods))
            {
                if (_selectedConnection != null)
                {
                    RecordUndoSnapshot();
                    NodeConnections.Remove(_selectedConnection);
                    _selectedConnection = null;
                    NodeConnectionsView?.Refresh();
                    RequestPreviewRefresh(false);
                    e.Handled = true;
                    return;
                }
                DeleteSelectedNodes();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.Undo, key, mods))
            {
                Undo();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.Redo, key, mods))
            {
                Redo();
                e.Handled = true;
                return;
            }

            if (settings.IsMatch(ShortcutAction.ZoomToSelected, key, mods))
            {
                ZoomToSelectedNodes();
                e.Handled = true;
                return;
            }
        }

        private void SelectAllNodes()
        {
            _nodeGraphController.SelectAllNodes();
        }

        private void ClearNodeSelection()
        {
            _nodeGraphController.ClearNodeSelection();
        }

        private void ClearConnectionSelection()
        {
            NodeGraphController.ClearConnectionSelection(ref _selectedConnection);
        }

        private void ConnectionLine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is NodeConnectionViewModel connection && !connection.IsPreview)
            {
                ClearNodeSelection();
                ClearConnectionSelection();
                connection.IsSelected = true;
                _selectedConnection = connection;
                e.Handled = true;
            }
        }

        private void ClearSelectionExcept(NodeViewModel keep)
        {
            _nodeGraphController.ClearSelectionExcept(keep);
        }

        private List<NodeViewModel> GetSelectedNodes()
        {
            return _nodeGraphController.GetSelectedNodes(SelectedNode);
        }

        private void DeleteSelectedNodes()
        {
            _selectedConnection = null;
            var removed = _nodeGraphController.DeleteSelectedNodes(SelectedNode);
            if (SelectedNode != null && removed.Contains(SelectedNode))
                SelectedNode = Nodes.LastOrDefault(node => node.IsSelected) ?? Nodes.LastOrDefault();
            _particleEvalService?.ClearState();
            _lastParticleSimulationFrame = -1;
        }

        private void DeleteNodes(IEnumerable<NodeViewModel> nodesToRemove)
        {
            _selectedConnection = null;
            var removed = _nodeGraphController.DeleteNodes(nodesToRemove);
            if (SelectedNode != null && removed.Contains(SelectedNode))
                SelectedNode = Nodes.LastOrDefault(node => node.IsSelected) ?? Nodes.LastOrDefault();
            _particleEvalService?.ClearState();
            _lastParticleSimulationFrame = -1;
        }

        private void CopySelectedNodes()
        {
            _nodeClipboard = _nodeGraphController.CopySelectedNodes(SelectedNode, GetSelectedTileSize());
        }

        private void PasteClipboardAtMouse()
        {
            if (_nodeClipboard == null) return;
            var created = _nodeGraphController.PasteClipboardAtMouse(
                _nodeClipboard, NodeCanvasScale,
                () => { try { return Mouse.GetPosition(NodeCanvasHost); } catch { return new Point(0, 0); } },
                () => NodeCanvasHost?.ActualWidth ?? 0,
                () => NodeCanvasHost?.ActualHeight ?? 0);
            if (created.Count > 0)
                SelectedNode = created.LastOrDefault();
        }

        private void NodeCanvasHost_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _lastNodeCanvasRightClick = e.GetPosition(NodeCanvasHost);
            }
            catch
            {
                _lastNodeCanvasRightClick = new Point(0, 0);
            }
        }

        private void Context_SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SelectAllNodes();
        }

        private void Context_DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedNodes();
        }

        private void Context_AutoLayout_Click(object sender, RoutedEventArgs e)
        {
            AutoArrangeNodes();
        }

        private void Context_AddNoiseNode_Click(object sender, RoutedEventArgs e)
        {
            var lib = NodeLibrary.FirstOrDefault(item => item.Name == "����");
            if (lib == null) return;
            RecordUndoSnapshot();
            var contentPos = GetNodeCanvasPosition(_lastNodeCanvasRightClick, NodeCanvasScale);
            var node = CreateNodeFromLibraryItem(lib, contentPos.X, contentPos.Y);
            Nodes.Add(node);
            SelectedNode = node;
        }

        private void Node_Context_Rename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is NodeViewModel node)
            {
                RenameNode(node);
            }
        }

        private void Node_Context_Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is NodeViewModel node)
            {
                DuplicateContextSelection(node);
            }
        }

        private void Node_Context_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is NodeViewModel node)
            {
                PrepareContextNode(node);
                DeleteSelectedNodes();
            }
        }

        private void DuplicateNode(NodeViewModel source)
        {
            RecordUndoSnapshot();
            var copy = _nodeGraphController.DuplicateNode(source);
            SelectedNode = copy;
        }

        private void NodeLibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void NodeLibraryList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var position = e.GetPosition(null);
            if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (sender is not ListBox listBox)
            {
                return;
            }

            var item = GetListBoxItemAtPoint(listBox, e.GetPosition(listBox));
            if (item?.DataContext is NodeLibraryEntry entry && entry.Item != null)
            {
                DragDrop.DoDragDrop(item, entry.Item, DragDropEffects.Copy);
            }
        }

        private void NodeCanvasHost_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(NodeLibraryItem)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void NodeCanvasHost_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NodeLibraryItem)))
            {
                if (e.Data.GetData(typeof(NodeLibraryItem)) is NodeLibraryItem libraryItem)
                {
                    RecordUndoSnapshot();
                    var contentPosition = GetContentPositionFromDrag(e);
                    var node = CreateNodeFromLibraryItem(libraryItem, contentPosition.X, contentPosition.Y);
                    Nodes.Add(node);
                    SelectedNode = node;
                }
                return;
            }

            if (e.Data.GetDataPresent("pixelgraph-template"))
            {
                var templatePath = e.Data.GetData("pixelgraph-template") as string;
                if (templatePath != null && System.IO.File.Exists(templatePath))
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(templatePath);
                        var data = System.Text.Json.JsonSerializer.Deserialize<ProjectFileService.ProjectData>(json);
                        if (data != null)
                        {
                            var contentPosition = GetContentPositionFromDrag(e);
                            LoadTemplateData(data, contentPosition.X, contentPosition.Y);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Loading template failed: {ex.Message}", "Error");
                    }
                }
                return;
            }
        }

        private void NodeCanvasHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double zoomStep = 0.05d;
            var oldScale = NodeCanvasScale;
            var targetScale = e.Delta > 0 ? oldScale + zoomStep : oldScale - zoomStep;
            var newScale = Math.Clamp(targetScale, NodeCanvasMinScale, NodeCanvasMaxScale);
            if (Math.Abs(newScale - oldScale) < 0.0001)
            {
                return;
            }

            var position = e.GetPosition(NodeCanvasHost);
            var contentPosition = GetContentPositionFromMouse(e);
            NodeCanvasScale = newScale;
            try
            {
                var hostAfter = ContentToHost(contentPosition);
                NodeCanvasOffsetX += position.X - hostAfter.X;
                NodeCanvasOffsetY += position.Y - hostAfter.Y;
            }
            catch
            {
                NodeCanvasOffsetX = position.X - contentPosition.X * newScale;
                NodeCanvasOffsetY = position.Y - contentPosition.Y * newScale;
            }

            e.Handled = true;
        }

        private void NodeCanvasHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Reserve right-click for the canvas/node context menus. Canvas panning
            // uses the middle mouse button so a simple right-click is never captured.
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isNodePanning = true;
                _nodePanStart = e.GetPosition(NodeCanvasHost);
                _nodePanHorizontalOffset = NodeCanvasOffsetX;
                _nodePanVerticalOffset = NodeCanvasOffsetY;
                NodeCanvasHost.CaptureMouse();
                MarkStatsActive();

                // Pause GPU previews during panning to avoid HWND composition issues
                try
                {
                    if (!_nodePreviewsPausedByInteraction)
                    {
                        NodeGpuPreviewManager.Instance.SetPaused(true);
                        _nodePreviewsPausedByInteraction = true;
                    }
                }
                catch { }
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                // Reset any pan state left over from a pan whose MouseUp was never received
                // (e.g. window lost focus while middle/right button was held).
                if (_isNodePanning)
                {
                    _isNodePanning = false;
                    try { NodeCanvasHost.ReleaseMouseCapture(); } catch { }
                }

                var element = e.OriginalSource as DependencyObject;

                // If the click occurred inside a node or a port, don't start marquee so node handlers can run.
                // Use both visual-tree check and a content-position hit test to be robust against template changes.
                var clickedInsideNode = IsElementInsideNodeOrPort(element);
                try
                {
                    var pos = e.GetPosition(NodeCanvasHost);
                    var contentPos = GetNodeCanvasPosition(pos, NodeCanvasScale);
                    const double nodeWidth = 180d;
                    const double nodeHeight = 120d;
                    foreach (var n in Nodes)
                    {
                        if (contentPos.X >= n.X && contentPos.X <= n.X + nodeWidth && contentPos.Y >= n.Y && contentPos.Y <= n.Y + nodeHeight)
                        {
                            clickedInsideNode = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // ignore hit-test failures and fall back to visual check
                }

                if (clickedInsideNode)
                {
                    return;
                }

                // If the click occurred on a connection line, let the event propagate
                // so the connection's MouseLeftButtonDown handler can fire.
                if (IsElementInsideConnection(element))
                {
                    return;
                }

                // Begin marquee selection when clicking on empty canvas area.
                var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (!ctrl)
                {
                    ClearNodeSelection();
                    ClearConnectionSelection();
                }

                _isMarqueeSelecting = true;
                try { NodeCanvasHost.CaptureMouse(); } catch { }
                // store start in content coordinates and compute matching viewport coordinates
                _marqueeStartContent = GetContentPositionFromMouse(e);
                _marqueeStartHost = e.GetPosition(NodeCanvasHost);
                _marqueeStartViewport = ContentToHost(_marqueeStartContent);
                if (SelectionMarquee != null)
                {
                    SelectionMarquee.Visibility = Visibility.Visible;
                    SelectionMarquee.Margin = new Thickness(_marqueeStartViewport.X, _marqueeStartViewport.Y, 0, 0);
                    SelectionMarquee.Width = 0;
                    SelectionMarquee.Height = 0;
                }
                // Pause GPU previews during marquee selection so HWND hosts do not show visual glitches
                try
                {
                    if (!_nodePreviewsPausedByInteraction)
                    {
                        NodeGpuPreviewManager.Instance.SetPaused(true);
                        _nodePreviewsPausedByInteraction = true;
                    }
                }
                catch { }
                MarkStatsActive();
                e.Handled = true;
                return;
            }
        }

        private void NodeCanvasHost_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isMarqueeSelecting)
            {
                // Use consistent coordinates: convert mouse to content (canvas) coordinates
                var contentPos = GetContentPositionFromMouse(e);

                // Convert content (layer) coords back to viewport (host) coords for marquee rectangle placement
                var viewportPoint = ContentToHost(contentPos);
                var viewportX = viewportPoint.X;
                var viewportY = viewportPoint.Y;

                var x = Math.Min(viewportX, _marqueeStartViewport.X);
                var y = Math.Min(viewportY, _marqueeStartViewport.Y);
                var w = Math.Abs(viewportX - _marqueeStartViewport.X);
                var h = Math.Abs(viewportY - _marqueeStartViewport.Y);
                if (SelectionMarquee != null)
                {
                    SelectionMarquee.Margin = new Thickness(x, y, 0, 0);
                    SelectionMarquee.Width = w;
                    SelectionMarquee.Height = h;
                }

                // Select nodes within marquee rect (transform host/viewport coords to canvas content coords)
                var topLeft = HostToContent(new Point(x, y));
                var bottomRight = HostToContent(new Point(x + w, y + h));
                var rectLeft = Math.Min(topLeft.X, bottomRight.X);
                var rectTop = Math.Min(topLeft.Y, bottomRight.Y);
                var rectRight = Math.Max(topLeft.X, bottomRight.X);
                var rectBottom = Math.Max(topLeft.Y, bottomRight.Y);

                foreach (var node in Nodes)
                {
                    var nodeLeft = node.X;
                    var nodeTop = node.Y;
                    const double nodeWidth = 180d;
                    const double nodeHeight = 120d;
                    var nodeRight = nodeLeft + nodeWidth;
                    var nodeBottom = nodeTop + nodeHeight;
                    var intersects = !(nodeRight < rectLeft || nodeLeft > rectRight || nodeBottom < rectTop || nodeTop > rectBottom);
                    node.IsSelected = intersects ? true : node.IsSelected;
                }

                MarkStatsActive();
                e.Handled = true;
                return;
            }

            if (!_isNodePanning)
            {
                if (_activeConnection != null)
                {
                    var contentPos = GetContentPositionFromMouse(e);

                    // Snap to nearest input port within threshold so users can "����" (snap) when connecting.
                    const double snapThreshold = 18.0; // pixels
                    NodeViewModel? nearestNode = null;
                    int nearestPortIndex = -1;
                    double nearestDistSq = double.MaxValue;

                    foreach (var n in Nodes)
                    {
                        for (int i = 0; i < n.InputPorts.Count; i++)
                        {
                            if (_activeConnection.StartNode != null && !CanSnapConnectionTarget(_activeConnection.StartNode, _activeConnection.StartPortIndex, n, i))
                            {
                                continue;
                            }

                            var p = GetCachedPortPosition(n, false, i);
                            var dx = p.X - contentPos.X;
                            var dy = p.Y - contentPos.Y;
                            var d2 = dx * dx + dy * dy;
                            if (d2 < nearestDistSq)
                            {
                                nearestDistSq = d2;
                                nearestNode = n;
                                nearestPortIndex = i;
                            }
                        }
                    }

                    if (nearestNode != null && nearestDistSq <= snapThreshold * snapThreshold)
                    {
                        var snapPoint = GetCachedPortPosition(nearestNode, false, nearestPortIndex);
                        _activeConnection.EndX = snapPoint.X;
                        _activeConnection.EndY = snapPoint.Y;
                        _snappedNode = nearestNode;
                        _snappedPortIndex = nearestPortIndex;
                    }
                    else
                    {
                        _activeConnection.EndX = contentPos.X;
                        _activeConnection.EndY = contentPos.Y;
                        _snappedNode = null;
                        _snappedPortIndex = -1;
                    }

                    MarkStatsActive();
                    e.Handled = true;
                }

                return;
            }

            var position = e.GetPosition(NodeCanvasHost);
            var delta = position - _nodePanStart;

            NodeCanvasOffsetX = _nodePanHorizontalOffset + delta.X;
            NodeCanvasOffsetY = _nodePanVerticalOffset + delta.Y;
            e.Handled = true;
        }

        private void NodeCanvasHost_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {

            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                if (_isNodePanning)
                {
                    _isNodePanning = false;
                    try { NodeCanvasHost.ReleaseMouseCapture(); } catch { }

                    _statsIdleTimer.Stop();
                    _statsIdleTimer.Start();
                    // Resume GPU previews after panning
                    try
                    {
                        if (_nodePreviewsPausedByInteraction)
                        {
                            NodeGpuPreviewManager.Instance.SetPaused(false);
                            _nodePreviewsPausedByInteraction = false;
                        }
                    }
                    catch { }
                    e.Handled = true;
                }

                return;
            }


            if (e.ChangedButton == MouseButton.Left && (_activeConnection != null || NodeConnections.Any(c => c.IsPreview)))
            {
                var port = FindPortFromSource(e.OriginalSource as DependencyObject);
                var node = port != null ? FindNodeFromPort(port) : null;

                // If releasing over an input port while we have an active connection, let that handler run first.
                if (node != null && port != null && node.InputPorts.Contains(port) && _activeConnection != null)
                {
                    return;
                }

                // If releasing over an output port while dragging from an input port, finalize the connection
                if (node != null && port != null && node.OutputPorts.Contains(port) && _activeInputNode != null
                    && _activeConnection != null && e.OriginalSource is DependencyObject depObj)
                {
                    var outputIndex = node.OutputPorts.IndexOf(port);
                    if (outputIndex >= 0)
                    {
                        var fe = depObj as FrameworkElement;
                        if (fe != null)
                        {
                            var endPoint = GetPortCenter(fe);
                            _activeConnection.EndX = endPoint.X;
                            _activeConnection.EndY = endPoint.Y;
                        }
                        TryFinalizeReverseConnection(node, outputIndex, _activeInputNode, _activeInputPortIndex);
                        e.Handled = true;
                        return;
                    }
                }

                // If we snapped to a nearby input port but the mouse isn't over the actual UI element,
                // finalize the connection to the snapped target here.
                if (_activeConnection != null && _snappedNode != null && _snappedPortIndex >= 0)
                {
                    var snapPoint = GetCachedPortPosition(_snappedNode, false, _snappedPortIndex);
                    _activeConnection.EndX = snapPoint.X;
                    _activeConnection.EndY = snapPoint.Y;
                    TryFinalizeActiveConnection(_snappedNode, _snappedPortIndex);

                    e.Handled = true;
                    return;
                }

                // Capture the failed connection info BEFORE cleanup so the menu can use it
                var failedConnection = _activeConnection;
                var failedConnectionPos = _activeConnection != null
                    ? new Point(_activeConnection.EndX, _activeConnection.EndY)
                    : new Point();
                var failedInputNode = _activeInputNode;
                var failedInputPortIndex = _activeInputPortIndex;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var previewConnections = NodeConnections.Where(c => c.IsPreview).ToList();
                        foreach (var preview in previewConnections)
                        {
                            try { NodeConnections.Remove(preview); } catch { }
                        }

                        _activeConnection = null;
                        _activeInputNode = null;
                        _activeInputPortIndex = -1;

                        try { Mouse.Capture(null); } catch { }

                        try
                        {
                            NodeConnectionsView?.Refresh();
                            UpdateConnectionPositions();
                            NodeConnectionLayer?.InvalidateVisual();
                        }
                        catch { }

                        // If connection dropped on empty space, show node creation menu
                        if (failedConnection?.StartNode != null && failedConnection.StartPortIndex >= 0
                            && failedConnection.StartNode.OutputPorts.Count > failedConnection.StartPortIndex)
                        {
                            ShowConnectionCreationMenu(failedConnectionPos,
                                failedConnection.StartNode, failedConnection.StartPortIndex,
                                failedConnection.StartNode.OutputPorts[failedConnection.StartPortIndex].Type);
                        }
                        else if (failedInputNode != null && failedInputPortIndex >= 0
                            && failedInputNode.InputPorts.Count > failedInputPortIndex)
                        {
                            ShowInputConnectionCreationMenu(failedConnectionPos,
                                failedInputNode, failedInputPortIndex,
                                failedInputNode.InputPorts[failedInputPortIndex].Type);
                        }
                    }
                    catch { }
                }), DispatcherPriority.Input);

                e.Handled = true;
            }

            // End marquee selection
            if (e.ChangedButton == MouseButton.Left && _isMarqueeSelecting)
            {
                _isMarqueeSelecting = false;
                try { NodeCanvasHost.ReleaseMouseCapture(); } catch { }
                if (SelectionMarquee != null)
                {
                    SelectionMarquee.Visibility = Visibility.Collapsed;
                    SelectionMarquee.Width = 0;
                    SelectionMarquee.Height = 0;
                }

                _statsIdleTimer.Stop();
                _statsIdleTimer.Start();
                e.Handled = true;
            }
        }

        private void NodeCanvasHost_LostMouseCapture(object? sender, MouseEventArgs e)
        {
            // Reset pan/marquee states when capture is lost unexpectedly (e.g. Alt+Tab
            // during a middle/right-click pan or marquee, which releases capture without
            // triggering PreviewMouseUp and would leave _isNodePanning stuck true).
            if (_isNodePanning)
            {
                _isNodePanning = false;
            }

            if (_isMarqueeSelecting)
            {
                _isMarqueeSelecting = false;
                try
                {
                    if (SelectionMarquee != null)
                        SelectionMarquee.Visibility = Visibility.Collapsed;
                }
                catch { }
            }

            try
            {
                var previewConnections = NodeConnections.Where(c => c.IsPreview).ToList();
                foreach (var preview in previewConnections)
                {
                    try { NodeConnections.Remove(preview); } catch { }
                }

                _activeConnection = null;
                _activeInputNode = null;
                _activeInputPortIndex = -1;
                try { NodeConnectionsView?.Refresh(); } catch { }
                try { UpdateConnectionPositions(); } catch { }
                try { NodeConnectionLayer?.InvalidateVisual(); } catch { }
            }
            catch
            {

            }

            // Ensure GPU previews are resumed if we paused them for interaction
            try
            {
                if (_nodePreviewsPausedByInteraction)
                {
                    NodeGpuPreviewManager.Instance.SetPaused(false);
                    _nodePreviewsPausedByInteraction = false;
                }
            }
            catch { }
        }

        private void MainWindow_PreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            // Connection handling (cleanup, menu) is now done in NodeCanvasHost_PreviewMouseUp
            // to ensure reliable execution. This handler only cleans up marquee and GPU state.

            // Ensure marquee ends if mouse released outside host
            if (_isMarqueeSelecting)
            {
                _isMarqueeSelecting = false;
                try { NodeCanvasHost.ReleaseMouseCapture(); } catch { }
                if (SelectionMarquee != null)
                {
                    SelectionMarquee.Visibility = Visibility.Collapsed;
                    SelectionMarquee.Width = 0;
                    SelectionMarquee.Height = 0;
                }
            }

            // Resume GPU previews if they were paused for interaction
            try
            {
                if (_nodePreviewsPausedByInteraction)
                {
                    NodeGpuPreviewManager.Instance.SetPaused(false);
                    _nodePreviewsPausedByInteraction = false;
                }
            }
            catch { }
        }

        private Point GetNodeCanvasPosition(Point viewportPosition, double scale)
        {
            // Prefer using WPF visual transforms to map a point from the host/viewport
            // coordinate space to the canvas/content coordinate space. This handles
            // RenderTransform, layout offsets and DPI correctly.
            try
            {
                if (NodeCanvasHost != null && NodeConnectionLayer != null)
                {
                    var t = NodeCanvasHost.TransformToVisual(NodeConnectionLayer);
                    return t.Transform(viewportPosition);
                }
            }
            catch
            {
            }

            var x = (viewportPosition.X - NodeCanvasOffsetX) / (scale <= 0 ? 1d : scale);
            var y = (viewportPosition.Y - NodeCanvasOffsetY) / (scale <= 0 ? 1d : scale);
            return new Point(x, y);
        }

        private Point GetContentPositionFromMouse(MouseEventArgs e)
        {
            // Prefer asking the mouse position relative to the content canvas layer. This
            // returns coordinates in the canvas/content coordinate space and automatically
            // accounts for current transforms, avoiding manual math and rounding issues.
            try
            {
                if (NodeConnectionLayer != null)
                {
                    return e.GetPosition(NodeConnectionLayer);
                }
            }
            catch { }

            var p = e.GetPosition(NodeCanvasHost);
            return GetNodeCanvasPosition(p, NodeCanvasScale);
        }

        private Point GetContentPositionFromDrag(DragEventArgs e)
        {
            try
            {
                if (NodeConnectionLayer != null)
                {
                    return e.GetPosition(NodeConnectionLayer);
                }
            }
            catch { }

            try
            {
                var p = e.GetPosition(NodeCanvasHost);
                return GetNodeCanvasPosition(p, NodeCanvasScale);
            }
            catch
            {
                return new Point(0, 0);
            }
        }

        private Point ContentToHost(Point contentPoint)
        {
            // Prefer using WPF visual transforms to convert a point from the content/canvas
            // coordinate space to the host/viewport coordinate space. This automatically
            // accounts for RenderTransforms, layout changes, DPI and other transforms and
            // avoids manual math that can become incorrect after resizing or fullscreen.
            try
            {
                if (NodeConnectionLayer != null && NodeCanvasHost != null)
                {
                    var t = NodeConnectionLayer.TransformToVisual(NodeCanvasHost);
                    return t.Transform(contentPoint);
                }
            }
            catch
            {
            }

            var scale = NodeCanvasScale <= 0 ? 1d : NodeCanvasScale;
            return new Point(contentPoint.X * scale + NodeCanvasOffsetX, contentPoint.Y * scale + NodeCanvasOffsetY);
        }

        private Point HostToContent(Point hostPoint)
        {
            try
            {
                if (NodeConnectionLayer != null && NodeCanvasHost != null)
                {
                    var t = NodeCanvasHost.TransformToVisual(NodeConnectionLayer);
                    return t.Transform(hostPoint);
                }
            }
            catch
            {
            }

            var scale = NodeCanvasScale <= 0 ? 1d : NodeCanvasScale;
            return new Point((hostPoint.X - NodeCanvasOffsetX) / scale, (hostPoint.Y - NodeCanvasOffsetY) / scale);
        }

        private void AutoArrangeNodes()
        {
            if (Nodes.Count == 0) return;

            RecordUndoSnapshot();

            // Build adjacency: node → list of output-connected nodes
            var outEdges = new Dictionary<int, List<int>>();
            var inEdges = new Dictionary<int, List<int>>();
            foreach (var node in Nodes)
            {
                outEdges[node.Id] = new List<int>();
                inEdges[node.Id] = new List<int>();
            }
            foreach (var conn in NodeConnections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
            {
                if (outEdges.ContainsKey(conn.StartNode!.Id) && inEdges.ContainsKey(conn.EndNode!.Id))
                {
                    outEdges[conn.StartNode.Id].Add(conn.EndNode.Id);
                    inEdges[conn.EndNode.Id].Add(conn.StartNode.Id);
                }
            }

            // Topological sort with layers (BFS)
            var layers = new Dictionary<int, int>(); // nodeId → layer (column)
            var queue = new Queue<int>();
            foreach (var node in Nodes)
            {
                if (inEdges[node.Id].Count == 0)
                {
                    layers[node.Id] = 0;
                    queue.Enqueue(node.Id);
                }
            }
            // If no roots (cyclic), pick any node as root
            if (queue.Count == 0 && Nodes.Count > 0)
            {
                layers[Nodes[0].Id] = 0;
                queue.Enqueue(Nodes[0].Id);
            }

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var next in outEdges[id])
                {
                    var newLayer = layers[id] + 1;
                    if (!layers.ContainsKey(next) || layers[next] < newLayer)
                    {
                        layers[next] = newLayer;
                        queue.Enqueue(next);
                    }
                }
            }

            // Group nodes by layer
            var layerGroups = new Dictionary<int, List<NodeViewModel>>();
            foreach (var node in Nodes)
            {
                var layer = layers.TryGetValue(node.Id, out var l) ? l : 0;
                if (!layerGroups.ContainsKey(layer))
                    layerGroups[layer] = new List<NodeViewModel>();
                layerGroups[layer].Add(node);
            }

            // Position: columns spaced by 250px, rows spaced by 140px
            const double colSpacing = 250;
            const double rowSpacing = 140;
            const double startX = 40;
            const double startY = 40;

            foreach (var kvp in layerGroups)
            {
                var col = kvp.Key;
                var nodesInLayer = kvp.Value;
                var x = startX + col * colSpacing;
                var totalHeight = (nodesInLayer.Count - 1) * rowSpacing;
                var baseY = startY + (nodesInLayer.Count > 1 ? 0 : totalHeight / 2);

                for (int i = 0; i < nodesInLayer.Count; i++)
                {
                    nodesInLayer[i].X = x;
                    nodesInLayer[i].Y = baseY + i * rowSpacing;
                }
            }

            UpdateNodeCanvasExtent();
            UpdateConnectionPositions();
            ScheduleConnectionGeometryRefresh();
            NodeConnectionLayer?.InvalidateVisual();
            MarkStatsActive();
        }

        private void ZoomToSelectedNodes()
        {

            var selected = GetSelectedNodes();
            if (selected.Count == 0 && SelectedNode != null)
            {
                selected = new List<NodeViewModel> { SelectedNode };
            }

            if (selected.Count == 0)
            {
                return;
            }


            const double nodeWidth = 180d;
            const double nodeHeight = 120d;

            var minX = selected.Min(n => n.X);
            var minY = selected.Min(n => n.Y);
            var maxX = selected.Max(n => n.X) + nodeWidth;
            var maxY = selected.Max(n => n.Y) + nodeHeight;

            var contentWidth = Math.Max(1.0, maxX - minX);
            var contentHeight = Math.Max(1.0, maxY - minY);


            if (NodeCanvasHost == null)
            {
                return;
            }

            var viewportWidth = NodeCanvasHost.ActualWidth;
            var viewportHeight = NodeCanvasHost.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }


            const double margin = 40d;
            var availW = Math.Max(1.0, viewportWidth - margin);
            var availH = Math.Max(1.0, viewportHeight - margin);

            var scaleX = availW / contentWidth;
            var scaleY = availH / contentHeight;
            var targetScale = Math.Min(scaleX, scaleY);


            targetScale *= 0.9;
            targetScale = Math.Clamp(targetScale, NodeCanvasMinScale, NodeCanvasMaxScale);


            var centerX = (minX + maxX) / 2.0;
            var centerY = (minY + maxY) / 2.0;

            var hostCenterX = viewportWidth / 2.0;
            var hostCenterY = viewportHeight / 2.0;

            NodeCanvasScale = targetScale;

            // Use the same visual-transform delta approach as wheel zoom so that all
            // coordinate-space differences (e.g. NodeCanvasHost padding vs the inner
            // NodeCanvasViewport where the RenderTransform lives) are handled correctly.
            try
            {
                var afterPos = ContentToHost(new Point(centerX, centerY));
                NodeCanvasOffsetX += hostCenterX - afterPos.X;
                NodeCanvasOffsetY += hostCenterY - afterPos.Y;
            }
            catch
            {
                NodeCanvasOffsetX = hostCenterX - centerX * NodeCanvasScale;
                NodeCanvasOffsetY = hostCenterY - centerY * NodeCanvasScale;
            }


            UpdateConnectionPositions();
            MarkStatsActive();
        }

        private void NodeItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not NodeViewModel node)
            {
                return;
            }

            ClearConnectionSelection();

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl-click toggles selection on the clicked node
                node.IsSelected = !node.IsSelected;
                // Do not change SelectedNode here to preserve multi-selection
            }
            else
            {
                // If the clicked node is already selected, keep the current multi-selection
                // so the user can drag multiple selected nodes together.
                if (!node.IsSelected)
                {
                    // Click on an unselected node clears other selections and selects this one
                    ClearSelectionExcept(node);
                    node.IsSelected = true;
                    SelectedNode = node;
                }
            }

            MarkStatsActive();
            // Store drag start in both content coordinates and host coordinates so node movement
            // remains stable even if transforms change during dragging (layout/resize/fullscreen).
            // Use helper that prefers the content layer and accounts for transforms.
            _nodeDragStart = GetContentPositionFromMouse(e);
            try
            {
                if (NodeCanvasHost != null)
                {
                    _nodeDragStartHost = e.GetPosition(NodeCanvasHost);
                }
                else
                {
                    _nodeDragStartHost = new Point(0, 0);
                }
            }
            catch
            {
                _nodeDragStartHost = new Point(0, 0);
            }
            _nodeDragStartScale = NodeCanvasScale <= 0 ? 1d : NodeCanvasScale;

            // If the clicked node is selected and multiple nodes are selected, start a multi-node drag
            var selected = Nodes.Where(n => n.IsSelected).ToList();
            if (node.IsSelected && selected.Count > 1)
            {
                _draggingNodes = new List<(NodeViewModel Node, double OriginX, double OriginY)>();
                foreach (var n in selected)
                {
                    _draggingNodes.Add((n, n.X, n.Y));
                }
                // still set primary dragging node for compatibility
                _draggingNode = node;
                // Treat this as a multi-selection drag: keep SelectedNode unchanged to avoid
                // side-effects from changing the active node while dragging multiple items.
            }
            else
            {
                _draggingNodes = null;
                _draggingNode = node;
                _nodeDragOriginX = node.X;
                _nodeDragOriginY = node.Y;
            }
            border.CaptureMouse();
            e.Handled = true;
        }

        private void NodeItem_MouseMove(object sender, MouseEventArgs e)
        {
            if ((_draggingNode == null && (_draggingNodes == null || _draggingNodes.Count == 0)) || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            MarkStatsActive();

            // Pause GPU-backed HWND previews during interactive dragging to avoid
            // HWND composition/transform artifacts. We resume when dragging ends.
            try
            {
                if (!_nodePreviewsPausedByInteraction)
                {
                    NodeGpuPreviewManager.Instance.SetPaused(true);
                    _nodePreviewsPausedByInteraction = true;
                }
            }
            catch { }
            // Compute delta based on host-space movement but convert to content-space using
            // the scale at drag start. This keeps the content delta stable even if NodeCanvasScale
            // or layout changes during dragging.
            Point contentDelta;
            try
            {
                if (NodeCanvasHost != null)
                {
                    var hostPos = e.GetPosition(NodeCanvasHost);
                    var hostDx = hostPos.X - _nodeDragStartHost.X;
                    var hostDy = hostPos.Y - _nodeDragStartHost.Y;
                    var scale = _nodeDragStartScale <= 0 ? 1d : _nodeDragStartScale;
                    contentDelta = new Point(hostDx / scale, hostDy / scale);
                }
                else
                {
                    var position = GetContentPositionFromMouse(e);
                    contentDelta = new Point(position.X - _nodeDragStart.X, position.Y - _nodeDragStart.Y);
                }
            }
            catch
            {
                var position = GetContentPositionFromMouse(e);
                contentDelta = new Point(position.X - _nodeDragStart.X, position.Y - _nodeDragStart.Y);
            }

            var deltaX = contentDelta.X;
            var deltaY = contentDelta.Y;

            // Don't start moving nodes until the pointer has exceeded the system drag-start thresholds.
            // Without this guard a slight, fast movement (e.g. 5 px horizontal, 0 px vertical) passes
            // the MouseUp revert check (which requires BOTH axes to be below threshold) and permanently
            // displaces every node in the current multi-selection.
            try
            {
                if (NodeCanvasHost != null)
                {
                    var curHostPos = e.GetPosition(NodeCanvasHost);
                    var dxHost = Math.Abs(curHostPos.X - _nodeDragStartHost.X);
                    var dyHost = Math.Abs(curHostPos.Y - _nodeDragStartHost.Y);
                    if (dxHost < SystemParameters.MinimumHorizontalDragDistance &&
                        dyHost < SystemParameters.MinimumVerticalDragDistance)
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch { }

            var now = Environment.TickCount;
            if (now - _lastNodeDragUpdateTick >= NodeDragThrottleMs)
            {
                _lastNodeDragUpdateTick = now;
                if (_draggingNodes != null && _draggingNodes.Count > 0)
                {
                    for (int i = 0; i < _draggingNodes.Count; i++)
                    {
                        var entry = _draggingNodes[i];
                        entry.Node.X = entry.OriginX + deltaX;
                        entry.Node.Y = entry.OriginY + deltaY;
                    }
                }
                else if (_draggingNode != null)
                {
                    _draggingNode.X = _nodeDragOriginX + deltaX;
                    _draggingNode.Y = _nodeDragOriginY + deltaY;
                }
            }

            try
            {
                if (NodeCanvasStats != null)
                {
                    NodeCanvasStats.Text = $"{_frameRenderMs:0} ms · {_nodeCanvasFps} fps";
                }
            }
            catch
            {

            }

            e.Handled = true;
        }

        private void NodeItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode == null && (_draggingNodes == null || _draggingNodes.Count == 0))
            {
                return;
            }

            // If the mouse was released without a meaningful move, treat it as a click.
            // When multiple nodes were selected and the user clicks one of them (without dragging),
            // convert the multi-selection into a single selection for that node. If the user
            // actually dragged (movement exceeds system thresholds), preserve the multi-selection
            // so group-drag continues to work.
            if (sender is Border border && border.DataContext is NodeViewModel node)
            {
                try
                {
                    // Use host-space coordinates so the threshold is independent of canvas zoom level.
                    // Content-space coordinates are scaled by NodeCanvasScale, which makes tiny movements
                    // exceed SystemParameters.MinimumHorizontalDragDistance when zoomed out, incorrectly
                    // triggering multi-drag for what was actually just a click.
                    var hostPos = NodeCanvasHost != null ? e.GetPosition(NodeCanvasHost) : default;
                    var hdx = Math.Abs(hostPos.X - _nodeDragStartHost.X);
                    var hdy = Math.Abs(hostPos.Y - _nodeDragStartHost.Y);
                    if (hdx < SystemParameters.MinimumHorizontalDragDistance && hdy < SystemParameters.MinimumVerticalDragDistance)
                    {
                        // Click without drag: if multiple nodes were selected and this node is among them,
                        // switch to single selection for this node and revert any accidental movement.
                        if (_draggingNodes != null && _draggingNodes.Count > 1 && node.IsSelected)
                        {
                            foreach (var entry in _draggingNodes)
                            {
                                entry.Node.X = entry.OriginX;
                                entry.Node.Y = entry.OriginY;
                            }
                            ClearSelectionExcept(node);
                            node.IsSelected = true;
                            SelectedNode = node;
                        }
                    }
                }
                catch
                {
                    // ignore hit-test or input errors
                }

                try { border.ReleaseMouseCapture(); } catch { }
            }

            _draggingNode = null;
            _draggingNodes = null;

            UpdateNodeCanvasExtent();
            UpdateConnectionPositions();
            ScheduleConnectionGeometryRefresh();


            _statsIdleTimer.Stop();
            _statsIdleTimer.Start();

            // Resume GPU previews if they were paused for interaction
            try
            {
                if (_nodePreviewsPausedByInteraction)
                {
                    NodeGpuPreviewManager.Instance.SetPaused(false);
                    _nodePreviewsPausedByInteraction = false;
                }
            }
            catch { }

            e.Handled = true;
        }

        private void NodeOutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not NodePortViewModel port)
            {
                return;
            }

            var node = FindNodeFromPort(port);
            if (node == null)
            {
                return;
            }

            var portIndex = node.OutputPorts.IndexOf(port);
            if (portIndex < 0)
            {
                return;
            }

            var startPoint = GetPortCenter(element);
            _activeConnection = new NodeConnectionViewModel
            {
                StartNode = node,
                StartPortIndex = portIndex,
                StartX = startPoint.X,
                StartY = startPoint.Y,
                EndX = startPoint.X,
                EndY = startPoint.Y,
                IsPreview = true
            };

            NodeConnections.Add(_activeConnection);

            try { Mouse.Capture(NodeCanvasHost, CaptureMode.SubTree); } catch { }
            MarkStatsActive();
            e.Handled = true;
        }

        private void NodePort_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is Grid g && g.DataContext is NodePortViewModel port)
                {
                    var brush = port.FillBrush as SolidColorBrush;
                    var color = brush?.Color ?? Colors.White;
                    g.Effect = new DropShadowEffect
                    {
                        Color = color,
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.9
                    };
                }
            }
            catch { }
        }

        private void NodePort_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is Grid g)
                {
                    g.Effect = null;
                }
            }
            catch { }
        }

        private void NodeInputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Don't interfere if we're already dragging a connection
            if (_activeConnection != null)
            {
                return;
            }

            if (sender is not FrameworkElement element || element.DataContext is not NodePortViewModel port)
            {
                return;
            }

            var node = FindNodeFromPort(port);
            if (node == null)
            {
                return;
            }

            var portIndex = node.InputPorts.IndexOf(port);
            if (portIndex < 0)
            {
                return;
            }

            // Find existing connection to this input port
            var existing = NodeConnections.FirstOrDefault(c => !c.IsPreview && c.EndNode == node && c.EndPortIndex == portIndex);
            if (existing != null)
            {
                // Detach: remove the connection and re-drag from its output end
                var startNode = existing.StartNode;
                var startPortIndex = existing.StartPortIndex;
                NodeConnections.Remove(existing);

                if (startNode != null)
                {
                    var startPoint = GetCachedPortPosition(startNode, true, startPortIndex);
                    _activeConnection = new NodeConnectionViewModel
                    {
                        StartNode = startNode,
                        StartPortIndex = startPortIndex,
                        StartX = startPoint.X,
                        StartY = startPoint.Y,
                        EndX = startPoint.X,
                        EndY = startPoint.Y,
                        IsPreview = true
                    };

                    NodeConnections.Add(_activeConnection);
                    try { Mouse.Capture(NodeCanvasHost, CaptureMode.SubTree); } catch { }
                    MarkStatsActive();
                    RequestPreviewRefresh(false);
                }
            }
            else
            {
                // No existing connection — start dragging from input port to find a compatible output
                // Create a "reverse" preview connection that ends at this input port
                var endPoint = GetPortCenter(element);
                _activeInputNode = node;
                _activeInputPortIndex = portIndex;
                _activeConnection = new NodeConnectionViewModel
                {
                    StartNode = null,
                    StartPortIndex = -1,
                    StartX = endPoint.X,
                    StartY = endPoint.Y,
                    EndX = endPoint.X,
                    EndY = endPoint.Y,
                    IsPreview = true
                };
                NodeConnections.Add(_activeConnection);
                try { Mouse.Capture(NodeCanvasHost, CaptureMode.SubTree); } catch { }
                MarkStatsActive();
            }

            e.Handled = true;
        }

        private void NodeInputPort_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_activeConnection == null || sender is not FrameworkElement element || element.DataContext is not NodePortViewModel port)
            {
                return;
            }

            var node = FindNodeFromPort(port);
            if (node == null)
            {
                return;
            }

            var portIndex = node.InputPorts.IndexOf(port);
            if (portIndex < 0)
            {
                return;
            }

            var endPoint = GetPortCenter(element);
            // If we had snapped while dragging, prefer the snapped target
            if (_snappedNode != null && ReferenceEquals(_snappedNode, node) && _snappedPortIndex == portIndex)
            {
                _activeConnection.EndX = GetCachedPortPosition(node, false, portIndex).X;
                _activeConnection.EndY = GetCachedPortPosition(node, false, portIndex).Y;
            }
            else
            {
                _activeConnection.EndX = endPoint.X;
                _activeConnection.EndY = endPoint.Y;
            }

            TryFinalizeActiveConnection(node, portIndex);
            e.Handled = true;
        }

        private Point GetPortCenter(FrameworkElement portElement)
        {
            if (portElement.ActualWidth <= 0 || portElement.ActualHeight <= 0)
            {
                portElement.UpdateLayout();
            }

            var center = new Point(portElement.ActualWidth / 2d, portElement.ActualHeight / 2d);
            return portElement.TranslatePoint(center, NodeConnectionLayer);
        }

        private NodeViewModel? FindNodeFromPort(NodePortViewModel port)
        {
            return Nodes.FirstOrDefault(node => node.InputPorts.Contains(port) || node.OutputPorts.Contains(port));
        }

        private static NodePortViewModel? FindPortFromSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is NodePortViewModel port)
                {
                    return port;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }
}
