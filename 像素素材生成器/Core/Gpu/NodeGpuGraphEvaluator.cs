#if VORTICE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ResourceUsage = Vortice.Direct3D11.Usage;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Core.Gpu
{
    /// <summary>
    /// Evaluates a graph entirely on the GPU by invoking nodes that implement
    /// <see cref="IGpuNativeNode"/> and returning per-node D3D11 textures.
    /// The entire GPU work is executed under <see cref="GpuScheduler"/> to
    /// serialize access to the shared device/context.
    /// </summary>
    internal sealed class NodeGpuGraphEvaluator
    {
        /// <summary>
        /// Evaluate all nodes and return a mapping of node id to GPU texture (may be null on failure).
        /// Throws <see cref="InvalidOperationException"/> when encountering a node that does not
        /// implement <see cref="IGpuNativeNode"/>, since this evaluator assumes a fully GPU-native graph.
        /// </summary>
        public Dictionary<int, ID3D11Texture2D?> EvaluateAllTextures(
            IReadOnlyList<GraphNodeInstance> nodes,
            IReadOnlyList<GraphConnection> connections,
            PixelGraphContext context)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (connections == null) throw new ArgumentNullException(nameof(connections));

            if (nodes.Count == 0) return new Dictionary<int, ID3D11Texture2D?>();

            // Shared topological sort + level computation (reuses NodeGraphEvaluator logic)
            var nodeById = nodes.ToDictionary(n => n.Id);
            var topoResult = NodeGraphEvaluator.TopologicalSort(nodes, connections.ToList());
            if (topoResult == null)
            {
                System.Diagnostics.Trace.TraceWarning("GPU evaluator: cycle detected in graph, aborting.");
                return new Dictionary<int, ID3D11Texture2D?>();
            }

            var sortedOrder = topoResult.SortedOrder;
            var level = topoResult.Level;
            var incomingByTarget = topoResult.IncomingByTarget;
            var groups = sortedOrder.GroupBy(id => level[id]).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

            // Evaluate nodes sequentially. Do NOT wrap the whole evaluation in
            // GpuScheduler.Instance.Execute — many GPU-native node implementations
            // call into GpuScheduler themselves. Wrapping here caused nested semaphore
            // waits and deadlocks when a node attempted to perform GPU work.
            var dict = new Dictionary<int, ID3D11Texture2D?>();

            // Acquire device and (best-effort) immediate context for callers that need them.
            var device = GpuCompute.GetD3D11DeviceForInterop();
            ID3D11DeviceContext? d3dContext = null;
            if (device != null)
            {
                try
                {
                    var prop = device.GetType().GetProperty("ImmediateContext");
                    if (prop != null) d3dContext = prop.GetValue(device) as ID3D11DeviceContext;
                    else
                    {
                        var mi = device.GetType().GetMethod("GetImmediateContext");
                        if (mi != null) d3dContext = mi.Invoke(device, null) as ID3D11DeviceContext;
                    }
                }
                catch { d3dContext = null; }
            }

            if (device == null || d3dContext == null)
            {
                System.Diagnostics.Trace.TraceWarning("GPU evaluator: D3D11 device or immediate context is unavailable.");
                return new Dictionary<int, ID3D11Texture2D?>();
            }

            foreach (var grp in groups)
            {
                // process nodes in this level sequentially.
                foreach (var nodeId in grp)
                {
                    if (!nodeById.TryGetValue(nodeId, out var instance)) continue;

                    var inputCount = instance.Node.InputPorts.Count;
                    var inputTextures = new List<ID3D11Texture2D?>();
                    if (incomingByTarget.TryGetValue(nodeId, out var incomingList))
                    {
                        // ensure list size equals inputCount
                        var temp = new ID3D11Texture2D?[Math.Max(1, inputCount)];
                        foreach (var conn in incomingList)
                        {
                            if (conn.TargetPortIndex >= 0 && conn.TargetPortIndex < temp.Length && dict.TryGetValue(conn.SourceNodeId, out var srcTex))
                            {
                                temp[conn.TargetPortIndex] = srcTex;
                            }
                        }
                        inputTextures.AddRange(temp);
                    }

                    var derivedContext = context.WithSeedOffset(nodeId * 137);

                    if (instance.Node is IGpuNativeNode gpuNode)
                    {
                        try
                        {
                            var tex = gpuNode.ProcessGpuNative(device, d3dContext, inputTextures, instance.ParameterValues, derivedContext);
                            dict[nodeId] = tex;
                        }
                        catch (Exception ex)
                        {
                            // On error, stop and surface a helpful message to caller
                            throw new InvalidOperationException($"GPU processing failed for node {nodeId}: {ex.Message}", ex);
                        }
                    }
                    else
                    {
                        // This evaluator requires every node to be GPU-native
                        throw new InvalidOperationException($"Node {nodeId} ({instance.Node.GetType().Name}) is not GPU-native. Implement IGpuNativeNode to use GPU evaluator.");
                    }
                }
            }

            var textures = dict;

            return textures ?? new Dictionary<int, ID3D11Texture2D?>();
        }
    }
}

#endif
