using System;
using System.Windows;
using System.Windows.Controls;
using PixelAssetGenerator.Interop;
using PixelAssetGenerator.Core.Gpu;

namespace PixelAssetGenerator.Controls
{
    /// <summary>
    /// Lightweight control that hosts a native D3D11SwapChainHost for GPU-direct previews.
    /// Set the NodeId property to register the created host with <see cref="NodeGpuPreviewManager"/>.
    /// The control will register on Loaded and unregister/dispose on Unloaded.
    /// </summary>
    public class NodePreviewControl : Grid, IDisposable
    {
        public static readonly DependencyProperty NodeIdProperty = DependencyProperty.Register(
            nameof(NodeId), typeof(int), typeof(NodePreviewControl), new PropertyMetadata(-1, OnNodeIdChanged));

        /// <summary>
        /// The node identifier used when registering the host with NodeGpuPreviewManager.
        /// </summary>
        public int NodeId
        {
            get => (int)GetValue(NodeIdProperty);
            set => SetValue(NodeIdProperty, value);
        }

        // Ensure the control lays out as a square (width == height) so thumbnails are not stretched.
        // We choose the smaller of the available width/height so the preview always fits without clipping.
        protected override Size MeasureOverride(Size availableSize)
        {
            // If parent gives infinite space in one axis, prefer the explicitly set size or a sensible default.
            double availW = double.IsInfinity(availableSize.Width) ? (this.Width > 0 ? this.Width : 96) : availableSize.Width;
            double availH = double.IsInfinity(availableSize.Height) ? (this.Height > 0 ? this.Height : 96) : availableSize.Height;
            var side = Math.Min(availW, availH);
            var sq = new Size(side, side);

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(sq);
            }

            return sq;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var side = Math.Min(finalSize.Width, finalSize.Height);
            var offsetX = (finalSize.Width - side) / 2.0;
            var offsetY = (finalSize.Height - side) / 2.0;
            var rect = new Rect(offsetX, offsetY, side, side);

            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(rect);
            }

            return finalSize;
        }

        private D3D11SwapChainHost? _host;
        private bool _registered;
        private System.Windows.Shapes.Rectangle? _fallbackRect;

        public NodePreviewControl()
        {
            Loaded += NodePreviewControl_Loaded;
            Unloaded += NodePreviewControl_Unloaded;
            try
            {
                // Listen for global pause so the control can hide the HWND when paused
                Core.Gpu.NodeGpuPreviewManager.Instance.PausedChanged += NodeGpuPreviewManager_PausedChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl ctor: subscription PausedChanged 事件失败: {ex}");
                throw;
            }
        }

        private void NodeGpuPreviewManager_PausedChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (_host != null)
                        {
                            var paused = NodeGpuPreviewManager.Instance.IsPaused;
                            _host.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
                            System.Diagnostics.Trace.TraceInformation($"[Debug] NodeGpuPreviewControl: PausedChanged -> IsPaused={paused}, HostVisibility={_host.Visibility}");
                        }
                    }
                    catch (Exception exInner)
                    {
                        System.Diagnostics.Trace.TraceError($"[debug] NodeGpuPreviewControl.NodeGpuPreviewManager_PausedChanged 内部processing异常: {exInner}");
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.NodeGpuPreviewManager_PausedChanged 调用 Dispatcher 异常: {ex}");
                throw;
            }
        }

        private void NodePreviewControl_Loaded(object? sender, RoutedEventArgs e)
        {
            if (_host != null) return;
            // Create a fallback visual bound to DataContext.PreviewBrush so when GPU swapchain
            // is unavailable we still show a rasterized thumbnail. The Rectangle is added
            // behind the D3D11SwapChainHost so it appears when the host is collapsed or not created.
            _fallbackRect = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                RadiusX = 2,
                RadiusY = 2
            };
            // Bind Fill to DataContext.PreviewBrush so it follows ViewModel updates
            try
            {
                var binding = new System.Windows.Data.Binding("PreviewBrush");
                _fallbackRect.SetBinding(System.Windows.Shapes.Rectangle.FillProperty, binding);
            }
            catch { }

            try
            {
                _host = new D3D11SwapChainHost();
                // Make the host fill the control
                _host.HorizontalAlignment = HorizontalAlignment.Stretch;
                _host.VerticalAlignment = VerticalAlignment.Stretch;

                // Add fallback first so swapchain host sits on top when available
                Children.Add(_fallbackRect!);
                Children.Add(_host);

                try { System.Diagnostics.Trace.TraceInformation("[debug] NodePreviewControl:' has been created.并add D3D11SwapChainHost 到视觉树"); } catch (Exception exLog) { System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl: Trace 写入失败: {exLog}"); }

                // Respect current paused state
                try
                {
                    if (Core.Gpu.NodeGpuPreviewManager.Instance.IsPaused)
                    {
                        _host.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _host.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl_Loaded: 读取 IsPaused 或settings Host.Visibility 失败: {ex}");
                    throw;
                }

                // Register with preview manager if NodeId is valid
                // If NodeId already set, register host now. Otherwise registration will occur
                // later when NodeId property changes (see OnNodeIdChanged callback).
                if (NodeId >= 0)
                {
                    TryRegisterHost(NodeId);
                }
            }
            catch (Exception ex)
            {
                // Creation failed; ensure host is disposed then rethrow so caller/global handlers see it
                try { _host?.Dispose(); } catch (Exception dispEx) { System.Diagnostics.Trace.TraceError($"[Debug] NodePreviewControl_Loaded: Dispose failed: {dispEx}"); }
                _host = null;
                System.Diagnostics.Trace.TraceError($"[Debug] NodePreviewControl_Loaded exception: {ex}");
                throw;
            }
        }

        private static void OnNodeIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not NodePreviewControl control) return;

            // If control has an existing registration for an old id, unregister it first.
            try
            {
                var oldId = (int)e.OldValue;
                if (oldId >= 0 && control._registered)
                {
                    try
                    {
                        NodeGpuPreviewManager.Instance.UnregisterHost(oldId);
                        System.Diagnostics.Trace.TraceInformation($"[debug] NodePreviewControl: 已unregister旧主机 id={oldId}");
                    }
                    catch (Exception exUnreg)
                    {
                        System.Diagnostics.Trace.TraceError($"[Debug] NodePreviewControl: UnregisterHost failed id={oldId}: {exUnreg}");
                        throw;
                    }

                    control._registered = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.OnNodeIdChanged 清理旧register时异常: {ex}");
                throw;
            }

            // If we have a host and new id is valid, attempt registration.
            try
            {
                var newId = (int)e.NewValue;
                if (newId >= 0 && control._host != null)
                {
                    control.TryRegisterHost(newId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.OnNodeIdChanged register新主机时异常: {ex}");
                throw;
            }
        }

        private void TryRegisterHost(int nodeId)
        {
            try
            {
                NodeGpuPreviewManager.Instance.RegisterHost(nodeId, _host!);
                _registered = true;
                try { System.Diagnostics.Trace.TraceInformation($"[debug] NodePreviewControl: registered主机 nodeId={nodeId}"); } catch (Exception exLog) { System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl: Trace 写入失败: {exLog}"); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.TryRegisterHost register失败 nodeId={nodeId}: {ex}");
                throw;
            }
        }

        private void NodePreviewControl_Unloaded(object? sender, RoutedEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            try
            {
                Core.Gpu.NodeGpuPreviewManager.Instance.PausedChanged -= NodeGpuPreviewManager_PausedChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.Dispose Cancelsubscription PausedChanged 失败: {ex}");
                // Do not rethrow from Dispose
            }

            if (_registered && NodeId >= 0)
            {
                try
                {
                    NodeGpuPreviewManager.Instance.UnregisterHost(NodeId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"[Debug] NodePreviewControl.Dispose UnregisterHost failed nodeId={NodeId}: {ex}");
                }

                _registered = false;
            }

            if (_host != null)
            {
                try
                {
                    if (Children.Contains(_host)) Children.Remove(_host);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"[debug] NodePreviewControl.Dispose 从视觉树remove Host 失败: {ex}");
                }

                try { _host.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.TraceError($"[Debug] NodePreviewControl.Dispose _host.Dispose failed: {ex}"); }
                _host = null;
            }
        }
    }
}
