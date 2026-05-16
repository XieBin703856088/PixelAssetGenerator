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
        // Update the overlay that draws a per-pixel grid over the real-time preview.
        // Reworked to use a vector DrawingBrush (same technique as the canvas grid)
        // instead of generating raster bitmaps. This preserves sharpness, respects
        // preview scale and DPI, and yields a much cleaner result.
        private void UpdatePreviewPixelGridOverlay()
        {
            try
            {
                if (PreviewPixelGridSurface == null)
                    return;

                // If user disabled overlay or we're in tiled preview mode, hide the overlay.
                if (!_showPreviewPixelGrid || _tiledPreviewEnabled)
                {
                    PreviewPixelGridSurface.Visibility = Visibility.Collapsed;
                    return;
                }

                // Require a rendered preview image - if none yet, collapse overlay and defer.
                if (PreviewImage == null || PreviewImage.Source == null)
                {
                    PreviewPixelGridSurface.Visibility = Visibility.Collapsed;
                    Dispatcher.BeginInvoke(new Action(UpdatePreviewPixelGridOverlay), DispatcherPriority.Loaded);
                    return;
                }

                // Defer if layout not ready
                if (PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
                {
                    Dispatcher.BeginInvoke(new Action(UpdatePreviewPixelGridOverlay), DispatcherPriority.Loaded);
                    return;
                }

                var tileSize = GetSelectedTileSize();
                if (tileSize <= 0)
                {
                    PreviewPixelGridSurface.Visibility = Visibility.Collapsed;
                    return;
                }

                // Compute displayed image size (the PreviewImage is centered and uses nearest-neighbor scaling)
                var dispW = Math.Max(1.0, PreviewImage.ActualWidth);
                var dispH = Math.Max(1.0, PreviewImage.ActualHeight);

                // Size overlay rectangle to match displayed image so the brush tiles correctly
                try
                {
                    PreviewPixelGridSurface.Width = dispW;
                    PreviewPixelGridSurface.Height = dispH;
                    PreviewPixelGridSurface.HorizontalAlignment = HorizontalAlignment.Center;
                    PreviewPixelGridSurface.VerticalAlignment = VerticalAlignment.Center;
                }
                catch { }

                // spacing between logical pixels in device-independent units (separate axes)
                var sx = dispW / (double)tileSize;
                var sy = dispH / (double)tileSize;

                // Build a DrawingBrush tile sized to one logical pixel cell (supersampled)
                // Increase supersampling so the visual stroke becomes thinner after downsampling
                // Larger supersample -> visually thinner final line after the scale transform.
                const double supersample = 24.0; // supersample factor to get sub-pixel softness
                var sxHigh = Math.Max(1.0, sx * supersample);
                var syHigh = Math.Max(1.0, sy * supersample);

                var db = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, sxHigh, syHigh),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.None
                };

                var tg = new TransformGroup();
                // first scale down the supersampled tile to sub-pixel thickness
                tg.Children.Add(new ScaleTransform(1.0 / supersample, 1.0 / supersample));
                // then inverse-scale by the preview transform so grid line thickness stays constant when zooming
                // Only scale down the supersampled tile to get sub-pixel thin lines.
                // We DO NOT inverse-scale by the preview transform here — the displayed
                // image size (dispW/dispH) already reflects the preview scale, so the
                // tile size (sx/sy) computed below corresponds to one visual pixel in
                // the preview. Inverse-scaling caused grid spacing to drift away from
                // the visible pixel grid.
                db.Transform = tg;

                var geo = new GeometryGroup();
                // draw a horizontal line at the top edge (y=0.5 in high-res) and
                // a vertical line at the left edge (x=0.5 in high-res) so tiled brush
                // produces lines between pixels that align with the preview image cells.
                geo.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(sxHigh, 0.5)));
                geo.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, syHigh)));

                // Use a gray line to match the canvas grid appearance (not pure black).
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33)), 1.0);
                pen.Freeze();

                var gd = new GeometryDrawing(null, pen, geo);
                db.Drawing = gd;
                db.Freeze();

                PreviewPixelGridSurface.Fill = db;
                PreviewPixelGridSurface.Visibility = Visibility.Visible;
            }
            catch
            {
                try { PreviewPixelGridSurface.Visibility = Visibility.Collapsed; } catch { }
            }
        }

        /// <summary>
        /// 防抖入口：在 NodePreviewDebounceMs 内的多次调用（如 AI 批量创建节点）只触发一次实际渲染。
        /// 必须从 UI 线程调用。
        /// </summary>
        private void ScheduleNodePreviewUpdate()
        {
            if (_nodePreviewDebounceTimer == null)
            {
                _nodePreviewDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(NodePreviewDebounceMs)
                };
                _nodePreviewDebounceTimer.Tick += (_, _) =>
                {
                    _nodePreviewDebounceTimer.Stop();
                    _ = UpdateAllNodePreviewsAsync();
                };
            }
            // 每次调用都重置计时器，保证最后一次变更稳定后才触发
            _nodePreviewDebounceTimer.Stop();
            _nodePreviewDebounceTimer.Start();
        }

        private async Task UpdateAllNodePreviewsAsync()
        {
            // Cancel any in-flight node preview generation
            var generation = Interlocked.Increment(ref _nodePreviewGeneration);
            _nodePreviewCts?.Cancel();
            _nodePreviewCts?.Dispose();
            _nodePreviewCts = new CancellationTokenSource();
            var token = _nodePreviewCts.Token;

            try
            {
                // Snapshot nodes and connections on UI thread to avoid collection modification
                var nodesSnapshot = Nodes.ToList();
                var connectionsSnapshot = NodeConnections.ToList();

                // resolve preview size on UI thread then run generation off-thread
                var previewSize = GetSelectedTileSize();

                // Prepare results off-ui thread. Handle cancellation by returning early
                // instead of throwing OperationCanceledException to avoid first-chance
                // exceptions being raised during normal rapid refreshes.
                var results = await Task.Run(() =>
                {
                    // If cancellation already requested, return empty results
                    if (token.IsCancellationRequested)
                        return new Dictionary<NodeViewModel, Brush?>();

                    // Build the full graph once and evaluate all nodes to get per-node buffers
                    var graphBuffers = EvaluateAllGraphNodeBuffers(nodesSnapshot, connectionsSnapshot, previewSize);

                    var dict = new Dictionary<NodeViewModel, Brush?>();
                    foreach (var node in nodesSnapshot)
                    {
                        try
                        {
                            // If canceled, stop processing and return what we have so far
                            if (token.IsCancellationRequested)
                            {
                                return dict;
                            }

                            Brush? brush = null;
                            if (IsGraphNode(node) && graphBuffers != null && graphBuffers.TryGetValue(node.Id, out var buffer))
                            {
                                var bmp = buffer.ToBitmapSource();
                                brush = CreatePreviewBrushFromBitmap(bmp, previewSize);
                            }
                            else if (node.Kind == NodeLibraryItemKind.Compute)
                            {
                                brush = GenerateComputeNodePreviewBrush(node);
                            }
                            else if (node.TileType != null)
                            {
                                var def = BuildNodeLayerDefinition(node);
                                if (def.HasValue)
                                {
                                    try
                                    {
                                        var bmp = _generator.GenerateTileBitmap(previewSize, new[] { def.Value });
                                        bmp.Freeze();
                                        brush = CreatePreviewBrushFromBitmap(bmp, previewSize);
                                    }
                                    catch
                                    {
                                        brush = node.PreviewBrush;
                                    }
                                }
                            }

                            dict[node] = brush;
                        }
                        catch
                        {
                            // ignore per-node failures
                            dict[node] = node.PreviewBrush;
                        }
                    }

                    return dict;
                }, CancellationToken.None).ConfigureAwait(false);

                // Apply results on UI thread (ensure generation still current)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (generation != _nodePreviewGeneration) return;
                    foreach (var kv in results)
                    {
                        try
                        {
                            var node = kv.Key;
                            var brush = kv.Value;
                            if (brush != null)
                            {
                                node.PreviewBrush = brush;
                            }
                        }
                        catch { }
                    }
                // After applying CPU-generated thumbnail brushes, try to present GPU-native
                // previews into registered per-node swap-chain hosts. This runs asynchronously
                // and is best-effort: failures are ignored and CPU thumbnails remain as fallback.
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (!Core.Gpu.GpuScheduler.Instance.IsSupported)
                            return;

                        // Build graph instances and connections for GPU evaluator
                        var instanceMap = new Dictionary<int, GraphNodeInstance>();
                        var instances = new List<GraphNodeInstance>();
                        var graphConnections = new List<GraphConnection>();

                        foreach (var n in nodesSnapshot)
                        {
                            var prototype = GraphNodeRegistry.Create(n.RegistryKey);
                            if (prototype == null) continue;

                            // Only include nodes that provide a GPU-native implementation
                            // to ensure the GPU evaluator receives a fully GPU-capable subgraph.
                            if (prototype is not Core.Gpu.IGpuNativeNode)
                                continue;

                            var inst = new GraphNodeInstance(n.Id, prototype);
                            // copy parameters (minimal set; evaluator will read required ones)
                            foreach (var p in n.Parameters)
                            {
                                var value = p.Kind switch
                                {
                                    NodeParameterKind.Seed => (object)p.IntValue,
                                    NodeParameterKind.Integer => (object)p.IntValue,
                                    NodeParameterKind.Boolean => p.BoolValue,
                                    NodeParameterKind.Choice => p.SelectedChoice ?? string.Empty,
                                    NodeParameterKind.PointList => (object)(System.Collections.Generic.IReadOnlyList<System.Windows.Point>)p.PointListValue.ToArray(),
                                    NodeParameterKind.Color => (object)p.ColorValue,
                                    NodeParameterKind.Text => p.TextValue ?? string.Empty,
                                    _ => (object)p.NumberValue
                                };
                                inst.ParameterValues[p.Name] = value;
                            }
                            instanceMap[n.Id] = inst;
                            instances.Add(inst);
                        }

                        foreach (var c in connectionsSnapshot.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
                        {
                            if (instanceMap.ContainsKey(c.StartNode!.Id) && instanceMap.ContainsKey(c.EndNode!.Id))
                            {
                                graphConnections.Add(new GraphConnection(c.StartNode!.Id, c.StartPortIndex, c.EndNode!.Id, c.EndPortIndex));
                            }
                        }

                        if (instances.Count == 0) return;

                        var context = new PixelGraphContext { TileSize = previewSize, Seed = 42 };

                        var gpuEvaluator = new Core.Gpu.NodeGpuGraphEvaluator();
                        Dictionary<int, Vortice.Direct3D11.ID3D11Texture2D?> textures = null!;
                        try
                        {
                            textures = gpuEvaluator.EvaluateAllTextures(instances, graphConnections, context);
                        }
                        catch
                        {
                            // GPU evaluation failed; nothing to do.
                            return;
                        }

                        if (textures == null) return;

                        foreach (var kvp in textures)
                        {
                            try
                            {
                                var nodeId = kvp.Key;
                                var tex = kvp.Value;
                                if (tex == null) continue;
                                try
                                {
                                    Core.Gpu.NodeGpuPreviewManager.Instance.PresentTexture(nodeId, tex);
                                }
                                catch { }
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                // canceled - nothing to do
            }
            catch
            {
                // ignore generation errors to avoid UI disruptions
            }
        }

        private void PreviewGridToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                _showPreviewPixelGrid = tb.IsChecked == true;
                UpdatePreviewPixelGridOverlay();
            }
        }

        // Create an ImageBrush that tiles a high-resolution bitmap and maps that bitmap
        // into a logical tile of 'logicalSize' device-independent units. The bitmap is
        // rendered at 'scale' times the logical size and then the image brush tiles the
        // downsampled result which produces subpixel-thin visible lines.
        private ImageBrush CreateSubpixelTiledBrush(int logicalSize, Color lineColor, int scale)
        {
            if (logicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            if (scale <= 0) scale = 1;

            var pixelSize = logicalSize * scale;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Use a sub-1.0 thickness in the high-resolution target so after downsampling
                // the line visually becomes thinner than one physical pixel while keeping
                // a stronger alpha to read as more solid. Use a slightly smaller stroke to
                // produce an even finer visible line after downsample.
                var pen = new Pen(new SolidColorBrush(lineColor), 0.20);
                pen.Freeze();

                // Draw the two edges (top and left) so when tiled they form a grid.
                // Offsetting by 0.5 keeps the stroke aligned to pixel centers in the
                // high-resolution target and produces a cleaner downsampled result.
                dc.DrawLine(pen, new Point(0.5, 0.5), new Point(pixelSize + 0.5, 0.5)); // horizontal
                dc.DrawLine(pen, new Point(0.5, 0.5), new Point(0.5, pixelSize + 0.5)); // vertical
            }

            // Use scaled DPI so 1 device-independent unit maps to multiple source pixels.
            var dpi = 96.0 * scale;
            var rtb = new RenderTargetBitmap(pixelSize, pixelSize, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            var brush = new ImageBrush(rtb)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, logicalSize, logicalSize),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            // Use high-quality scaling during the downsample to get smooth subpixel lines.
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            return brush;
        }

        // Determine a sensible integer scale factor for rendering high-resolution
        // grid tiles used to produce subpixel-thin lines. We pick a scale that
        // produces at least 2x supersampling on standard DPI and increases on
        // high-DPI displays so the final visual thickness remains consistent.
        private int DetermineSubpixelScale()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                // base multiplier - how many source pixels per logical unit we want
                // increased slightly so downsampled lines become thinner visually
                const int baseMultiplier = 8;
                // scale proportional to DPI (96 DPI == 1.0)
                var factor = (int)Math.Ceiling(baseMultiplier * (dpi.PixelsPerInchX / 96.0));
                // keep within reasonable bounds
                return Math.Clamp(factor, 2, 10);
            }
            catch
            {
                return 4;
            }
        }

        private void StartStats()
        {
            if (_statsActive) return;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _fpsTimer.Start();
            _statsActive = true;

            _lastRenderTimestamp = 0;
            _lastFpsUpdateTimestamp = 0;
            _framesSinceLastFpsUpdate = 0;
        }

        private void StopStats()
        {
            if (!_statsActive) return;
            try
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
            }
            catch
            {

            }
            _fpsTimer.Stop();
            _statsActive = false;
        }

        private void MarkStatsActive()
        {

            StartStats();

            _statsIdleTimer.Stop();
            _statsIdleTimer.Start();
        }

        private void StatsIdleTimer_Tick(object? sender, EventArgs e)
        {
            _statsIdleTimer.Stop();

            StopStats();
        }

        private void FpsTimer_Tick(object? sender, EventArgs e)
        {

            var frames = _frameCount;
            _frameCount = 0;
            _nodeCanvasFps = (int)Math.Round(frames / (_fpsTimer.Interval.TotalSeconds > 0 ? _fpsTimer.Interval.TotalSeconds : 1));


            Dispatcher.Invoke(() =>
            {
                if (NodeCanvasStats != null)
                {
                    NodeCanvasStats.Text = $"{_frameRenderMs:0} ms · {_nodeCanvasFps} fps";
                }
            });
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {

            try
            {
                var now = Stopwatch.GetTimestamp();
                if (_lastRenderTimestamp == 0)
                {
                    _lastRenderTimestamp = now;
                }

                _frameCount++;
                _framesSinceLastFpsUpdate++;

                var deltaTicks = now - _lastRenderTimestamp;
                if (deltaTicks > 0)
                {
                    var seconds = (double)deltaTicks / Stopwatch.Frequency;
                    var ms = seconds * 1000.0;
                    _renderAccumulatorCount++;
                    if (_renderAccumulatorStart == 0)
                    {
                        _renderAccumulatorStart = now;
                    }
                    _frameRenderMs = (_frameRenderMs * 0.8) + (ms * 0.2);
                }

                _lastRenderTimestamp = now;

                var nowFps = now;
                if (_lastFpsUpdateTimestamp == 0)
                {
                    _lastFpsUpdateTimestamp = nowFps;
                    _framesSinceLastFpsUpdate = 0;
                }

                var elapsedTicks = nowFps - _lastFpsUpdateTimestamp;
                var elapsedSec = (double)elapsedTicks / Stopwatch.Frequency;
                if (elapsedSec >= 0.1)
                {
                    var fps = _framesSinceLastFpsUpdate / elapsedSec;
                    _nodeCanvasFps = (int)Math.Round(fps);

                    var latency = _lastGenerationLatencyMs > 0 && (Stopwatch.GetTimestamp() - _lastGenerationTimestamp) < Stopwatch.Frequency * 5
                        ? _lastGenerationLatencyMs
                        : (_frameRenderMs > 0 ? _frameRenderMs : 0);

                    try
                    {
                        if (NodeCanvasStats != null)
                        {
                            var displayFps = _nodeCanvasFps;
                            var displayMs = latency > 0 ? latency : (displayFps > 0 ? 1000.0 / displayFps : 0);
                            NodeCanvasStats.Text = $"{displayMs:0} ms · {displayFps} fps";
                        }
                    }
                    catch
                    {

                    }

                    _lastFpsUpdateTimestamp = nowFps;
                    _framesSinceLastFpsUpdate = 0;
                }
            }
            catch
            {

            }
        }

        private void RefreshPreview(bool showStatus)
        {
            RequestPreviewRefresh(showStatus);
        }

        private void RequestPreviewRefresh(bool showStatus)
        {
            if (!_isInitialized)
            {
                return;
            }
            _graphEvaluator.ClearSourceCache();

            _pendingShowStatus |= showStatus;
            _previewDebounceTimer.Stop();

            // Throttle: if enough time has elapsed since the last render was fired,
            // schedule a near-immediate render (16 ms to coalesce same-frame changes).
            // Otherwise wait out only the remaining slice of the throttle window so
            // rapid slider drags produce renders at most every PreviewThrottleMs ms
            // while still guaranteeing a trailing render when the user stops moving.
            var elapsedMs = (Stopwatch.GetTimestamp() - _lastPreviewFireTimestamp)
                            * 1000.0 / Stopwatch.Frequency;
            _previewDebounceTimer.Interval = elapsedMs >= PreviewThrottleMs
                ? TimeSpan.FromMilliseconds(16)
                : TimeSpan.FromMilliseconds(Math.Max(16, PreviewThrottleMs - elapsedMs));

            _previewDebounceTimer.Start();
        }

        private void PreviewDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _previewDebounceTimer.Stop();
            _lastPreviewFireTimestamp = Stopwatch.GetTimestamp();
            var showStatus = _pendingShowStatus;
            _pendingShowStatus = false;
            _ = RefreshPreviewCoreAsync(showStatus);
        }

        private async Task RefreshPreviewCoreAsync(bool showStatus)
        {
            if (!_isInitialized)
            {
                return;
            }

            if (!TryGetNodeOutputSize(out var size))
            {
                if (showStatus)
                {
                    StatusText.Text = "Invalid size";
                }

                return;
            }

            if (SelectedNode == null)
            {
                var black = CreateBlackBitmap(size, size);
                await Dispatcher.InvokeAsync(() =>
                {
                    _lastGenerationLatencyMs = 0;
                    _lastGenerationTimestamp = Stopwatch.GetTimestamp();
                    _frameCount++;

                    _lastGenerated = black;
                    UpdatePreviewDisplay(size);
                    UpdateFitScale();
                    ApplyZoom();

                    if (showStatus)
                    {
                        StatusText.Text = "Complete";
                    }
                }, DispatcherPriority.Background);

                _statsIdleTimer.Stop();
                _statsIdleTimer.Start();
                return;
            }

            // Comparison mode: re-render terminal node output overlay
            if (_comparisonModeEnabled)
            {
                _ = Dispatcher.InvokeAsync(() => RenderComparisonPreview(), DispatcherPriority.Background);
            }

            var layerDefs = BuildNodePreviewLayerDefinitions();

            // Determine the actual preview target based on current mode:
            // Still mode → returns the selected node itself for per-node inspection.
            // Animation mode → returns the terminal/output node for full-graph playback.
            var previewTarget = FindPreviewTargetNode(SelectedNode);
            var actualPreviewTarget = previewTarget ?? SelectedNode;
            var isGraphNode = actualPreviewTarget != null && IsGraphNode(actualPreviewTarget);

            if (isGraphNode)
            {
                var graphGeneration = Interlocked.Increment(ref _previewGeneration);
                _previewCts?.Cancel();
                _previewCts?.Dispose();
                _previewCts = new CancellationTokenSource();
                var graphToken = _previewCts.Token;

                if (showStatus)
                {
                    StatusText.Text = "Generating...";
                    EvalProgressBar.Visibility = Visibility.Visible;
                }
                MarkStatsActive();

                BitmapSource? graphBitmap = null;
                double graphLatency = 0;
                try
                {
                    var sw = Stopwatch.StartNew();
                    graphBitmap = await Task.Run(() =>
                    {
                        graphToken.ThrowIfCancellationRequested();
                        var bmp = EvaluateGraphPipeline(size, actualPreviewTarget);
                        bmp?.Freeze();
                        return bmp;
                    }, graphToken).ConfigureAwait(false);
                    sw.Stop();
                    graphLatency = sw.Elapsed.TotalMilliseconds;
                }
                catch (OperationCanceledException) { return; }

                if (graphBitmap != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (graphGeneration != _previewGeneration) return;
                        _lastGenerationLatencyMs = graphLatency;
                        _lastGenerationTimestamp = Stopwatch.GetTimestamp();
                        _frameCount++;
                        _lastGenerated = graphBitmap;
                        UpdatePreviewDisplay(size);
                        ScheduleNodePreviewUpdate();
                        UpdateFitScale();
                        ApplyZoom();
                        if (showStatus)
                        {
                            StatusText.Text = graphLatency >= 1000
                                ? $"Complete ({graphLatency / 1000:F1}s)"
                                : $"Complete ({graphLatency:F0}ms)";
                            EvalProgressBar.Visibility = Visibility.Collapsed;
                        }
                    }, DispatcherPriority.Background);
                    _statsIdleTimer.Stop();
                    _statsIdleTimer.Start();

                    // Comparison mode: re-render terminal node output overlay
                    if (_comparisonModeEnabled)
                    {
                        _ = Dispatcher.InvokeAsync(() => RenderComparisonPreview(), DispatcherPriority.Background);
                    }

                    return;
                }
            }

            if (layerDefs.Count == 0)
            {
                var black = CreateBlackBitmap(size, size);
                await Dispatcher.InvokeAsync(() =>
                {
                    _lastGenerationLatencyMs = 0;
                    _lastGenerationTimestamp = Stopwatch.GetTimestamp();
                    _frameCount++;

                    _lastGenerated = black;
                    UpdatePreviewDisplay(size);
                    UpdateFitScale();
                    ApplyZoom();

                    if (showStatus)
                    {
                        StatusText.Text = "Complete";
                    }
                }, DispatcherPriority.Background);

                _statsIdleTimer.Stop();
                _statsIdleTimer.Start();

                return;
            }

            var generation = Interlocked.Increment(ref _previewGeneration);
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            if (showStatus)
            {
                StatusText.Text = "Generating...";
                EvalProgressBar.Visibility = Visibility.Visible;
            }

            MarkStatsActive();

            BitmapSource? generated = null;
            double generationLatencyMs = 0;
            try
            {

                var sw = Stopwatch.StartNew();
                generated = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var bitmap = _generator.GenerateTileBitmap(size, layerDefs);
                    bitmap.Freeze();
                    return bitmap;
                }, token).ConfigureAwait(false);
                sw.Stop();
                generationLatencyMs = sw.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException)
            {
                return;
            }


            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _previewGeneration || generated is null)
                {
                    return;
                }

                _lastGenerationLatencyMs = generationLatencyMs;
                _lastGenerationTimestamp = Stopwatch.GetTimestamp();
                _frameCount++;

                _lastGenerated = generated;
                UpdatePreviewDisplay(size);
                UpdateFitScale();
                ApplyZoom();

                ScheduleNodePreviewUpdate();

                if (showStatus)
                {
                    StatusText.Text = generationLatencyMs >= 1000
                        ? $"Complete ({generationLatencyMs / 1000:F1}s)"
                        : $"Complete ({generationLatencyMs:F0}ms)";
                    EvalProgressBar.Visibility = Visibility.Collapsed;
                }
            }, DispatcherPriority.Background);

            _statsIdleTimer.Stop();
            _statsIdleTimer.Start();

            // Comparison mode: re-render terminal node output overlay
            if (_comparisonModeEnabled)
            {
                _ = Dispatcher.InvokeAsync(() => RenderComparisonPreview(), DispatcherPriority.Background);
            }
        }

        private static TileLayerSettings BuildSettings(TileProperties props, byte[]? customFlowerPatternPixels = null, int customFlowerPatternWidth = 0, int customFlowerPatternHeight = 0)
        {
            return new TileLayerSettings(
                Scale: (float)props.Scale,
                Octaves: props.Octaves,
                Persistence: (float)props.Persistence,
                Lacunarity: (float)props.Lacunarity,
                DetailDensity: (float)props.DetailDensity,
                EdgeStrength: (float)props.EdgeStrength,
                MacroScale: (float)props.MacroScale,
                MacroStrength: (float)props.MacroStrength,
                MicroScale: (float)props.MicroScale,
                MicroStrength: (float)props.MicroStrength,
                AccentDensity: (float)props.AccentDensity,
                AccentSize: (float)props.AccentSize,
                ColorVariation: (float)props.ColorVariation,
                GrassBladeDensity: (float)props.GrassBladeDensity,
                GrassBladeHeight: (float)props.GrassBladeHeight,
                GrassPatchiness: (float)props.GrassPatchiness,
                GrassPreset: props.GrassPreset,
                GrassFlowerMode: props.GrassFlowerMode,
                FlowerDensity: (float)props.FlowerDensity,
                FlowerSize: (float)props.FlowerSize,
                FlowerColor: props.FlowerColor,
                FlowerPalette: props.FlowerColors != null ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(props.FlowerColors, e => e.Color)) : System.Array.Empty<System.Windows.Media.Color>(),
                FlowerWeights: props.FlowerColors != null ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(props.FlowerColors, e => (float)e.Weight)) : System.Array.Empty<float>(),
                CustomFlowerPatternPixels: customFlowerPatternPixels,
                CustomFlowerPatternWidth: customFlowerPatternWidth,
                CustomFlowerPatternHeight: customFlowerPatternHeight,
                StoneCrackDensity: (float)props.StoneCrackDensity,
                StoneMossDensity: (float)props.StoneMossDensity,
                WaterWaveScale: (float)props.WaterWaveScale,
                WaterWaveChoppiness: (float)props.WaterWaveChoppiness,
                WaterFoamDensity: (float)props.WaterFoamDensity,
                WaterFoamSize: (float)props.WaterFoamSize,
                WaterDepthVariation: (float)props.WaterDepthVariation,
                SandDuneScale: (float)props.SandDuneScale,
                SandDuneSharpness: (float)props.SandDuneSharpness,
                SandRippleStrength: (float)props.SandRippleStrength,
                SandPebbleDensity: (float)props.SandPebbleDensity,
                SandPebbleSize: (float)props.SandPebbleSize,
                RoadWidth: (float)props.RoadWidth,
                RoadEdgeRoughness: (float)props.RoadEdgeRoughness,
                RoadRutDepth: (float)props.RoadRutDepth,
                RoadGravelDensity: (float)props.RoadGravelDensity,
                RoadShoulderWidth: (float)props.RoadShoulderWidth,
                RoadShoulderRoughness: (float)props.RoadShoulderRoughness,
                RoadLayout: props.RoadLayout,
                RoadCornerRoundness: (float)props.RoadCornerRoundness,
                StoneHorizontalTileCount: props.StoneHorizontalTileCount,
                StoneVerticalTileCount: props.StoneVerticalTileCount,
                WaterCurrentDirection: (float)props.WaterCurrentDirection,
                WaterCurrentStrength: (float)props.WaterCurrentStrength,
                SandRippleDirection: (float)props.SandRippleDirection,
                SandRippleScale: (float)props.SandRippleScale,
                RoadCenterLine: (float)props.RoadCenterLine,
                RoadCenterLineRoughness: (float)props.RoadCenterLineRoughness,
                ErosionStrength: (float)props.ErosionStrength,
                ErosionScale: (float)props.ErosionScale,
                NineSliceEnabled: props.NineSliceEnabled,
                NineSliceEdgeSize: (float)props.NineSliceEdgeSize,
                NineSliceMaskSize: (float)props.NineSliceMaskSize,
                NineSliceEdgeFeather: (float)props.NineSliceEdgeFeather,
                MaskEnabled: props.MaskEnabled,
                MaskInvert: props.MaskInvert,
                MaskElement: props.MaskElement,
                Seed: props.Seed);
        }

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateFitScale();
            SetZoom(1d);
        }

        private void TileToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb)
            {
                SetTiledPreviewMode(tb.IsChecked == true);
            }
            else
            {
                SetTiledPreviewMode(!_tiledPreviewEnabled);
            }
        }

        private void SetTiledPreviewMode(bool enabled)
        {
            _tiledPreviewEnabled = enabled;
            if (TileToggleButton != null && TileToggleButton.IsChecked != enabled)
            {
                TileToggleButton.IsChecked = enabled;
            }

            // When enabling tiled preview, automatically disable the real-time pixel grid
            // since tiling and the pixel-grid overlay don't make sense together.
            try
            {
                if (PreviewGridToggleButton != null)
                {
                    // Disable the toggle so user cannot change grid while tiled preview is active
                    PreviewGridToggleButton.IsEnabled = !enabled;
                    if (enabled)
                    {
                        PreviewGridToggleButton.IsChecked = false;
                        _showPreviewPixelGrid = false;
                    }
                }
            }
            catch { }

            if (enabled)
            {
                UpdatePreviewPixelGridOverlay();
            }

            if (PreviewImage != null)
            {
                PreviewImage.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            }

            if (TiledPreviewSurface != null)
            {
                TiledPreviewSurface.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdatePreviewDisplay();
            UpdateFitScale();
            ApplyZoom();
        }

        private void UpdatePreviewDisplay(int? tileSize = null)
        {
            if (PreviewImage == null || TiledPreviewSurface == null)
            {
                return;
            }

            if (_lastGenerated is null)
            {
                PreviewImage.Source = null;
                PreviewImage.Width = 0;
                PreviewImage.Height = 0;
                TiledPreviewSurface.Fill = null;
                _tiledPreviewContentSize = 0d;
                return;
            }

            var size = Math.Max(1, tileSize ?? _lastGenerated.PixelWidth);
            _tiledPreviewContentSize = size;

            if (!_tiledPreviewEnabled)
            {
                PreviewImage.Source = _lastGenerated;
                PreviewImage.Width = size;
                PreviewImage.Height = size;
                PreviewImage.Visibility = Visibility.Visible;
                TiledPreviewSurface.Visibility = Visibility.Collapsed;
                TiledPreviewSurface.Fill = null;
                return;
            }

            PreviewImage.Visibility = Visibility.Collapsed;
            TiledPreviewSurface.Visibility = Visibility.Visible;

            var tiledBrush = new ImageBrush(_lastGenerated)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, _lastGenerated.PixelWidth, _lastGenerated.PixelHeight),
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            RenderOptions.SetBitmapScalingMode(tiledBrush, BitmapScalingMode.NearestNeighbor);
            TiledPreviewSurface.Fill = tiledBrush;
            UpdateTiledPreviewBrushViewport(_fitScale * _zoomFactor);
        }

        private double GetFitContentSize()
        {
            if (_tiledPreviewContentSize > 0d)
            {
                return _tiledPreviewContentSize;
            }

            return _lastGenerated?.PixelWidth ?? 0;
        }

        private void UpdateTiledPreviewBrushViewport(double scale)
        {
            if (!_tiledPreviewEnabled || TiledPreviewSurface?.Fill is not ImageBrush tiledBrush || _lastGenerated is null || PreviewViewport == null)
            {
                return;
            }

            var pad = PreviewViewport.Padding;
            var availW = Math.Max(1d, PreviewViewport.ActualWidth - pad.Left - pad.Right);
            var availH = Math.Max(1d, PreviewViewport.ActualHeight - pad.Top - pad.Bottom);
            var clampedScale = Math.Max(scale, 0.01d);
            var tileWidth = Math.Max(1d, _lastGenerated.PixelWidth * clampedScale);
            var tileHeight = Math.Max(1d, _lastGenerated.PixelHeight * clampedScale);
            var offsetX = (availW - tileWidth) / 2d;
            var offsetY = (availH - tileHeight) / 2d;

            tiledBrush.Viewbox = new Rect(0, 0, _lastGenerated.PixelWidth, _lastGenerated.PixelHeight);
            tiledBrush.Viewport = new Rect(offsetX, offsetY, tileWidth, tileHeight);
        }

        private BitmapSource? BuildNineSliceComposite(int size)
        {
            var tileNodes = GetNodeGraphTileNodes();
            if (tileNodes.Count == 0)
            {
                return null;
            }

            var compositeSize = size * 3;
            var layers = new List<(BitmapSource Bitmap, LayerBlendMode BlendMode, float Opacity)>();

            foreach (var node in tileNodes)
            {
                var def = BuildNodeLayerDefinition(node);
                if (!def.HasValue) continue;
                var tile = _generator.GenerateTileBitmap(size, new[] { def.Value });
                tile.Freeze();
                var tiled = TileToGrid(tile, size, compositeSize);
                layers.Add((tiled, LayerBlendMode.Normal, 1f));
            }

            return _generator.ComposeBitmapLayers(compositeSize, layers);
        }

        /// <summary>
        /// Finds the best node to use as the preview target.
        /// In Still mode, returns the selected node itself so users see its output.
        /// In Animation mode, uses the existing priority logic:
        ///   1. ParticleRenderNode (if any exists in the graph)
        ///   2. Output node (if any)
        ///   3. A node with no outgoing connections (terminal)
        ///   4. The currently selected node (fallback)
        /// </summary>
        private NodeViewModel? FindPreviewTargetNode(NodeViewModel? selected)
        {
            // Still mode: show the selected node's own output
            if (_previewMode == PreviewMode.Still && selected != null)
                return selected;

            // Animation mode: following priority logic
            // Priority 1: ParticleRenderNode — show particles when they exist
            foreach (var n in Nodes)
            {
                if (n.TypeName == "ParticleRender" && IsGraphNode(n))
                    return n;
            }

            // Priority 2: Output node
            var outputNode = GetOutputNode();
            if (outputNode != null)
                return outputNode;

            // Priority 3: Terminal node (no outgoing connections)
            foreach (var n in Nodes)
            {
                var hasOutgoing = NodeConnections.Any(c =>
                    !c.IsPreview && c.StartNode != null && c.StartNode.Id == n.Id);
                if (!hasOutgoing && IsGraphNode(n))
                    return n;
            }

            // Priority 4: Fallback to selected node
            return selected;
        }

        private static BitmapSource TileToGrid(BitmapSource tile, int tileSize, int outputSize)
        {
            var tilePixels = new byte[tileSize * tileSize * 4];
            tile.CopyPixels(tilePixels, tileSize * 4, 0);

            var outputPixels = new byte[outputSize * outputSize * 4];
            for (var y = 0; y < outputSize; y++)
            {
                for (var x = 0; x < outputSize; x++)
                {
                    var localX = x % tileSize;
                    var localY = y % tileSize;
                    var srcIndex = (localY * tileSize + localX) * 4;
                    var destIndex = (y * outputSize + x) * 4;
                    Buffer.BlockCopy(tilePixels, srcIndex, outputPixels, destIndex, 4);
                }
            }

            var output = new WriteableBitmap(outputSize, outputSize, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, outputSize, outputSize), outputPixels, outputSize * 4, 0);
            return output;
        }

        private void PreviewContainer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var delta = e.Delta > 0 ? 0.1 : -0.1;
            SetZoom(Math.Clamp(_zoomFactor + delta, 0.1, 16.0));
        }

        private void SetZoom(double factor)
        {
            _zoomFactor = factor;
            ApplyZoom();
        }

        private void UpdateFitScale()
        {
            if (_lastGenerated == null)
            {
                _fitScale = 1d;
                return;
            }

            var contentSize = GetFitContentSize();
            if (contentSize <= 0)
            {
                _fitScale = 1d;
                return;
            }

            var pad = PreviewViewport.Padding;
            var availW = PreviewViewport.ActualWidth - pad.Left - pad.Right;
            var availH = PreviewViewport.ActualHeight - pad.Top - pad.Bottom;
            if (availW <= 0 || availH <= 0)
            {
                _fitScale = 1d;
                return;
            }

            _fitScale = Math.Min(availW, availH) / contentSize;
            if (_fitScale <= 0)
            {
                _fitScale = 1d;
            }
        }

        private void ApplyZoom()
        {
            var scale = _fitScale * _zoomFactor;
            if (scale <= 0)
            {
                scale = 1d;
            }

            if (PreviewScale == null || ZoomText == null)
            {
                return;
            }

            if (_tiledPreviewEnabled)
            {
                PreviewScale.ScaleX = 1d;
                PreviewScale.ScaleY = 1d;
                UpdateTiledPreviewBrushViewport(scale);
            }
            else
            {
                PreviewScale.ScaleX = scale;
                PreviewScale.ScaleY = scale;
            }

            ZoomText.Text = $"{_zoomFactor * 100:0}%";
        }
    }
}
