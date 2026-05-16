using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using PixelAssetGenerator.Interop;
using Vortice.Direct3D11;

namespace PixelAssetGenerator.Core.Gpu
{
    /// <summary>
    /// Manages per-node GPU preview hosts (swap-chain hosts) and presents render targets
    /// produced by GpuCompute. UI code should register an instance of
    /// <see cref="Interop.D3D11SwapChainHost"/> for a node id and call
    /// <see cref="PresentShapes(int,int,ShapeParams[])"/> when the node's GPU output
    /// needs to be shown.
    /// </summary>
    internal sealed class NodeGpuPreviewManager
    {
        public event EventHandler? PausedChanged;
        private bool _paused;
        public bool IsPaused => _paused;

        /// <summary>
        /// Globally pause/resume presenting into registered hosts. When paused,
        /// PresentShapes will no-op. Raises <see cref="PausedChanged"/> when state changes.
        /// </summary>
        public void SetPaused(bool paused)
        {
            lock (_lock)
            {
                if (_paused == paused) return;
                _paused = paused;
            }

            try
            {
                PausedChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.SetPaused 事件处理器抛出异常: {ex}");
                throw;
            }
        }

        private static readonly Lazy<NodeGpuPreviewManager> s_lazy = new(() => new NodeGpuPreviewManager());
        public static NodeGpuPreviewManager Instance => s_lazy.Value;

        // Map nodeId -> host and last-used render target index (toggle 0/1)
        private readonly Dictionary<int, (D3D11SwapChainHost Host, int LastIndex)> _hosts = new();
        private readonly object _lock = new();

        private NodeGpuPreviewManager() { }

        /// <summary>
        /// Registers a swap-chain host to be used when presenting GPU previews for a node id.
        /// Calling this will replace any previously registered host for the same node id.
        /// </summary>
        public void RegisterHost(int nodeId, D3D11SwapChainHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            lock (_lock)
            {
                _hosts[nodeId] = (host, 0);
            }
        }

        /// <summary>
        /// Unregisters the host associated with a node id. Safe to call multiple times.
        /// </summary>
        public void UnregisterHost(int nodeId)
        {
            lock (_lock)
            {
                if (_hosts.ContainsKey(nodeId)) _hosts.Remove(nodeId);
            }
        }

        /// <summary>
        /// Presents a batch of shapes for the given node id into the registered host.
        /// This method serializes GPU access by using GpuScheduler and will post the
        /// Present call to the UI thread. Returns true on success.
        /// </summary>
        public bool PresentShapes(int nodeId, int size, GpuCompute.ShapeParams[] shapes)
        {
            // If globally paused (e.g. during interactive canvas operations) skip presenting
            if (IsPaused) return false;

            if (shapes == null) return false;
            try { System.Diagnostics.Trace.TraceInformation($"[调试] PresentShapes 调用: nodeId={nodeId}, size={size}, shapesCount={(shapes?.Length ?? 0)}"); } catch { }

            D3D11SwapChainHost? host = null;
            int index = 0;
            lock (_lock)
            {
                if (!_hosts.TryGetValue(nodeId, out var pair)) return false;
                host = pair.Host;
                index = pair.LastIndex ^ 1; // toggle index
                _hosts[nodeId] = (pair.Host, index);
            }

            if (host == null) return false;

            try
            {
                // Ensure resources exist for the requested size
                GpuScheduler.Instance.EnsureRenderResources(size);

                // Run GPU render and acquire the D3D11 texture for interop on the GPU thread
                var tex = GpuScheduler.Instance.Execute(() =>
                {
                    try
                    {
                        // Render to internal render target and then fetch the render texture object
                        GpuCompute.RenderToRenderTarget(index, size, shapes);
                        return GpuCompute.GetRenderTextureForInterop(index);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager: GPU 渲染线程抛出异常 nodeId={nodeId}, index={index}: {ex}");
                        throw;
                    }
                });

                if (tex == null)
                {
                    try { System.Diagnostics.Trace.TraceWarning($"[调试] NodeGpuPreviewManager.PresentShapes: 渲染返回空纹理 nodeId={nodeId}, index={index}"); } catch { }
                    return false;
                }

                // Present must occur on UI thread — dispatch async to avoid blocking the GPU scheduler.
                try
                {
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            host.EnsureBufferSize(size, size);
                        }
                        catch (Exception exBuf)
                        {
                            System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager: EnsureBufferSize 异常: {exBuf}");
                            return;
                        }

                        try
                        {
                            host.PresentRenderTexture(tex);
                        }
                        catch (Exception exPresent)
                        {
                            System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager: PresentRenderTexture 在 Dispatcher 内部抛出异常 nodeId={nodeId}: {exPresent}");
                        }
                    });
                }
                catch (Exception exDispatch)
                {
                    System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager: Dispatcher.BeginInvoke 异常 nodeId={nodeId}: {exDispatch}");
                    throw;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentShapes 异常 nodeId={nodeId}: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Present an existing D3D11 texture into the registered host for <paramref name="nodeId"/>.
        /// This is intended for nodes that render directly to a GPU texture (IGpuNativeNode).
        /// Presentation is dispatched to the UI thread because swap-chains/hosts are owned there.
        /// Returns true on success.
        /// </summary>
        public bool PresentTexture(int nodeId, ID3D11Texture2D? texture)
        {
            if (IsPaused) return false;
            if (texture == null) return false;

            D3D11SwapChainHost? host = null;
            lock (_lock)
            {
                if (!_hosts.TryGetValue(nodeId, out var pair)) return false;
                host = pair.Host;
            }

            if (host == null) return false;

            try
            {
                // Present must occur on UI thread — dispatch async to avoid blocking the GPU scheduler.
                try
                {
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var desc = texture.Description;
                            try { host.EnsureBufferSize(desc.Width, desc.Height); }
                            catch (Exception exBuf)
                            {
                                System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentTexture EnsureBufferSize 异常: {exBuf}");
                                try { host.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                return;
                            }
                        }
                        catch (Exception exDesc)
                        {
                            System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentTexture 读取 Description 异常: {exDesc}");
                            try { host.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                            return;
                        }

                        try
                        {
                            host.PresentRenderTexture(texture);
                        }
                        catch (Exception exPresent)
                        {
                            System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentTexture 在 Dispatcher 内部呈现失败 nodeId={nodeId}: {exPresent}");
                            try { host.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                            return;
                        }
                        try { host.Visibility = System.Windows.Visibility.Visible; } catch { }
                    });
                }
                catch (Exception exDispatch)
                {
                    System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentTexture Dispatcher.BeginInvoke 异常 nodeId={nodeId}: {exDispatch}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[调试] NodeGpuPreviewManager.PresentTexture 异常 nodeId={nodeId}: {ex}");
                try { host.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                return false;
            }
        }

        /// <summary>
        /// Evaluates the GPU graph from source nodes up to a specific target node ID,
        /// then presents the result texture into the registered host for that node.
        /// Only works if the subgraph feeding into <paramref name="targetNodeId"/>
        /// consists entirely of IGpuNativeNode nodes.
        /// Returns true if GPU evaluation and presentation succeeded.
        /// </summary>
        public bool EvaluateAndPresentNode(
            int targetNodeId,
            int tileSize,
            IReadOnlyList<GraphNodeInstance> allInstances,
            IReadOnlyList<GraphConnection> allConnections,
            PixelGraphContext context)
        {
            if (IsPaused) return false;
            if (!GpuScheduler.Instance.IsSupported) return false;

            // Verify the target node has a registered host
            D3D11SwapChainHost? host = null;
            lock (_lock)
            {
                if (!_hosts.TryGetValue(targetNodeId, out var pair)) return false;
                host = pair.Host;
            }
            if (host == null) return false;

            // Find the subgraph feeding into the target: collect all upstream nodes
            var relevantIds = new HashSet<int>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(targetNodeId);

            var nodeById = allInstances.ToDictionary(n => n.Id);
            var incomingByTarget = allConnections
                .GroupBy(c => c.TargetNodeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!visited.Add(id)) continue;
                relevantIds.Add(id);

                if (incomingByTarget.TryGetValue(id, out var incs))
                {
                    foreach (var c in incs)
                        stack.Push(c.SourceNodeId);
                }
            }

            // Filter to GPU-native nodes only
            var gpuInstances = new List<GraphNodeInstance>();
            var gpuInstanceMap = new Dictionary<int, GraphNodeInstance>();
            foreach (var id in relevantIds)
            {
                if (!nodeById.TryGetValue(id, out var inst)) continue;
                if (inst.Node is not IGpuNativeNode) continue;
                var gpuInst = new GraphNodeInstance(inst.Id, inst.Node);
                foreach (var kv in inst.ParameterValues)
                    gpuInst.ParameterValues[kv.Key] = kv.Value;
                gpuInstanceMap[id] = gpuInst;
                gpuInstances.Add(gpuInst);
            }

            if (gpuInstances.Count == 0) return false;

            // Build filtered connections among GPU nodes
            var gpuConnections = new List<GraphConnection>();
            foreach (var conn in allConnections)
            {
                if (gpuInstanceMap.ContainsKey(conn.SourceNodeId) && gpuInstanceMap.ContainsKey(conn.TargetNodeId))
                    gpuConnections.Add(conn);
            }

            try
            {
                var gpuEval = new NodeGpuGraphEvaluator();
                var textures = gpuEval.EvaluateAllTextures(gpuInstances, gpuConnections, context);

                if (textures == null || !textures.TryGetValue(targetNodeId, out var tex) || tex == null)
                    return false;

                // Present on UI thread
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var desc = tex.Description;
                        host.EnsureBufferSize(desc.Width, desc.Height);
                        host.PresentRenderTexture(tex);
                        host.Visibility = Visibility.Visible;
                    }
                    catch { }
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the output texture for a specific node from a full GPU graph evaluation result.
        /// Returns the texture directly without presenting it.
        /// </summary>
        public bool TryGetNodeTexture(int nodeId, out ID3D11Texture2D? texture,
            IReadOnlyList<GraphNodeInstance> allInstances,
            IReadOnlyList<GraphConnection> allConnections,
            PixelGraphContext context)
        {
            texture = null;
            if (!GpuScheduler.Instance.IsSupported) return false;

            var gpuInstances = new List<GraphNodeInstance>();
            var gpuInstanceMap = new Dictionary<int, GraphNodeInstance>();

            foreach (var inst in allInstances)
            {
                if (inst.Node is not IGpuNativeNode) continue;
                var gpuInst = new GraphNodeInstance(inst.Id, inst.Node);
                foreach (var kv in inst.ParameterValues)
                    gpuInst.ParameterValues[kv.Key] = kv.Value;
                gpuInstanceMap[inst.Id] = gpuInst;
                gpuInstances.Add(gpuInst);
            }

            if (gpuInstances.Count == 0) return false;

            var gpuConnections = new List<GraphConnection>();
            foreach (var conn in allConnections)
            {
                if (gpuInstanceMap.ContainsKey(conn.SourceNodeId) && gpuInstanceMap.ContainsKey(conn.TargetNodeId))
                    gpuConnections.Add(conn);
            }

            try
            {
                var gpuEval = new NodeGpuGraphEvaluator();
                var textures = gpuEval.EvaluateAllTextures(gpuInstances, gpuConnections, context);
                if (textures == null) return false;
                return textures.TryGetValue(nodeId, out texture);
            }
            catch
            {
                return false;
            }
        }
    }
}

