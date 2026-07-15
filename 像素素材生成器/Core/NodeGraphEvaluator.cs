using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
// PointList uses (double X, double Y) tuple instead of System.Windows.Point
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Represents a connection between two nodes in the graph.
/// </summary>
public sealed record GraphConnection(int SourceNodeId, int SourcePortIndex, int TargetNodeId, int TargetPortIndex);

/// <summary>
/// Runtime instance of a node in the graph, holding its type and parameter values.
/// </summary>
public sealed class GraphNodeInstance
{
    public int Id { get; }
    public IGraphNode Node { get; }
    public Dictionary<string, object> ParameterValues { get; } = new();

    /// <summary>Persistent state carried across evaluation frames (e.g., particle buffers).</summary>
    public object? PersistentState { get; set; }

    /// <summary>Last frame index this node was evaluated at. Used for cross-frame state.</summary>
    public int LastEvaluatedFrame { get; set; } = -1;

    public GraphNodeInstance(int id, IGraphNode node)
    {
        Id = id;
        Node = node;

        // Initialize defaults
        foreach (var param in node.Parameters)
        {
            ParameterValues[param.Name] = param.Kind switch
            {
                NodeParameterKind.Number => (object)param.DefaultNumber,
                NodeParameterKind.Seed => param.DefaultInt,
                NodeParameterKind.Integer => param.DefaultInt,
                NodeParameterKind.Boolean => param.DefaultBool,
                NodeParameterKind.Choice => param.DefaultChoice ?? string.Empty,
                NodeParameterKind.PointList => (object)new List<(double X, double Y)>(),
                NodeParameterKind.Color => (object)param.DefaultColor,
                NodeParameterKind.Text => string.Empty,
                _ => 0.0
            };
        }
    }
}

/// <summary>
/// Evaluates a node graph by performing topological sort and processing nodes in order.
/// Each node receives its input PixelBuffers and produces an output PixelBuffer.
/// </summary>
public sealed class NodeGraphEvaluator
{
    private readonly GraphEvaluationCache _resultCache = new();
    private readonly object _topologyGate = new();
    private ulong _topologyFingerprint;
    private TopologyResult? _cachedTopology;
    private long _evaluationGeneration;

    /// <summary>Statistics for the most recently completed CPU evaluation.</summary>
    public GraphEvaluationMetrics LastMetrics { get; private set; } =
        new(TimeSpan.Zero, 0, 0, 0, false, 0);

    /// <summary>
    /// Fired when a non-fatal evaluation error occurs (e.g. cycle detected).
    /// The UI can subscribe to show user-visible messages.
    /// </summary>
    public Action<string>? OnEvaluationError { get; set; }

    /// <summary>
    /// Evaluates the entire graph and returns the output buffer(s).
    /// If targetNodeId is specified, returns only the buffer for that node.
    /// Otherwise returns the buffer for the first output/terminal node found.
    /// </summary>
    public PixelBuffer? Evaluate(
        IReadOnlyList<GraphNodeInstance> nodes,
        IReadOnlyList<GraphConnection> connections,
        PixelGraphContext context,
        int? targetNodeId = null,
        CancellationToken ct = default)
    {
        // Delegate to EvaluateAll which uses a level-based parallel evaluation to
        // process independent nodes concurrently (improves CPU utilization on multi-core machines).
        if (nodes.Count == 0)
            return null;

        IReadOnlyList<GraphNodeInstance> evaluationNodes = nodes;
        IReadOnlyList<GraphConnection> evaluationConnections = connections;
        if (targetNodeId.HasValue)
        {
            if (!nodes.Any(node => node.Id == targetNodeId.Value)) return null;

            // A targeted preview only needs the target's transitive ancestors. Large
            // canvases commonly contain multiple experiments; avoiding unrelated branches
            // is substantially cheaper than evaluating the entire document.
            var incoming = connections.GroupBy(edge => edge.TargetNodeId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var required = new HashSet<int> { targetNodeId.Value };
            var pending = new Stack<int>();
            pending.Push(targetNodeId.Value);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!incoming.TryGetValue(current, out var parentEdges)) continue;
                foreach (var edge in parentEdges)
                    if (required.Add(edge.SourceNodeId)) pending.Push(edge.SourceNodeId);
            }

            evaluationNodes = nodes.Where(node => required.Contains(node.Id)).ToArray();
            evaluationConnections = connections
                .Where(edge => required.Contains(edge.SourceNodeId) && required.Contains(edge.TargetNodeId))
                .ToArray();
        }

        var all = EvaluateAll(evaluationNodes, evaluationConnections, context, ct);

        if (targetNodeId.HasValue)
        {
            if (all.TryGetValue(targetNodeId.Value, out var targetBuffer))
                return TakeResult(all, targetBuffer);
            DisposeResults(all, null);
            return null;
        }

        var sourceIds = evaluationConnections.Select(connection => connection.SourceNodeId).ToHashSet();
        var terminalIds = evaluationNodes
            .Where(n => string.Equals(n.Node.TypeName, "Output", StringComparison.OrdinalIgnoreCase)
                        || !sourceIds.Contains(n.Id))
            .Select(n => n.Id)
            .ToList();

        foreach (var tid in terminalIds)
        {
            if (all.TryGetValue(tid, out var terminalBuffer))
                return TakeResult(all, terminalBuffer);
        }

        var fallback = all.Values.LastOrDefault();
        return fallback == null ? null : TakeResult(all, fallback);
    }

    private static PixelBuffer TakeResult(IReadOnlyDictionary<int, PixelBuffer> results, PixelBuffer selected)
    {
        DisposeResults(results, selected);
        return selected;
    }

    private static void DisposeResults(IReadOnlyDictionary<int, PixelBuffer> results, PixelBuffer? except)
    {
        var disposed = new HashSet<PixelBuffer>(ReferenceEqualityComparer.Instance);
        foreach (var buffer in results.Values)
        {
            if (ReferenceEquals(buffer, except) || !disposed.Add(buffer)) continue;
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Clears incremental results. Kept under the old name for source compatibility;
    /// normal parameter edits no longer need to call it because fingerprints invalidate
    /// only the affected node and its downstream dependants.
    /// </summary>
    public void ClearSourceCache() => _resultCache.Clear();

    public void ClearCaches()
    {
        _resultCache.Clear();
        lock (_topologyGate)
        {
            _cachedTopology = null;
            _topologyFingerprint = 0;
        }
    }

    /// <summary>
    /// Evaluates and returns buffers for all nodes (useful for per-node preview).
    /// </summary>
    public Dictionary<int, PixelBuffer> EvaluateAll(
        IReadOnlyList<GraphNodeInstance> nodes,
        IReadOnlyList<GraphConnection> connections,
        PixelGraphContext context,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (nodes.Count == 0)
            return new Dictionary<int, PixelBuffer>();

        var evaluationGeneration = unchecked((ulong)System.Threading.Interlocked.Increment(ref _evaluationGeneration));
        _resultCache.RetainNodes(nodes.Select(node => node.Id).ToHashSet());

        ct.ThrowIfCancellationRequested();

        var validation = GraphValidator.Validate(nodes, connections);
        var validationErrors = validation.Diagnostics
            .Where(diagnostic => diagnostic.Severity == GraphDiagnosticSeverity.Error)
            .ToArray();
        if (validationErrors.Length > 0)
        {
            var message = string.Join(" ", validationErrors.Take(3).Select(diagnostic => diagnostic.Message));
            OnEvaluationError?.Invoke(message);
            LastMetrics = new(stopwatch.Elapsed, nodes.Count, 0, 0, false, validation.Diagnostics.Count);
            return new Dictionary<int, PixelBuffer>();
        }

        // Prepopulated results from partial GPU evaluation (id -> PixelBuffer)
        Dictionary<int, PixelBuffer>? prepopForCpu = null;
#if VORTICE
        // If the GPU subsystem is available and ALL nodes are GPU-native, use the
        // GPU-native evaluator which returns D3D11 textures per node and then
        // read them back to PixelBuffer for compatibility with existing callers.
        try
        {
                if (Gpu.GpuScheduler.Instance.IsSupported && nodes.All(n => n.Node is Gpu.IGpuNativeNode))
                {
                    var gpuEval = new Gpu.NodeGpuGraphEvaluator();
                    var texMap = gpuEval.EvaluateAllTextures(nodes, connections, context);

                    // Convert textures to PixelBuffer using staged readback under scheduler.
                    // If any node returns null (signals GPU path unavailable for that node)
                    // or any readback fails, fall back to the CPU evaluation path instead
                    // of throwing — this matches the documented IGpuNativeNode contract.
                    var resultMap = new Dictionary<int, PixelBuffer>();
                    var failedNodes = new List<int>();
                    foreach (var kv in texMap)
                    {
                        var nodeId = kv.Key;
                        var tex = kv.Value;
                        if (tex == null)
                        {
                            failedNodes.Add(nodeId);
                            continue;
                        }

                        PixelBuffer? pb = null;
                        try
                        {
                            pb = Gpu.GpuScheduler.Instance.Execute(() => Gpu.GpuCompute.ReadTextureToPixelBuffer(tex));
                        }
                        catch (SharpGen.Runtime.SharpGenException sgx)
                        {
                            System.Diagnostics.Trace.TraceWarning($"GPU readback SharpGenException for node {nodeId}: {sgx.Message}");
                            pb = null;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.TraceWarning($"GPU readback failed for node {nodeId}: {ex.Message}");
                            pb = null;
                        }
                        finally
                        {
                            tex?.Dispose();
                        }

                        if (pb == null)
                        {
                            failedNodes.Add(nodeId);
                            continue;
                        }

                        resultMap[nodeId] = pb;
                    }

                    if (failedNodes.Count > 0)
                    {
                        // Release any GPU-produced PixelBuffers we created as part of the failed full-GPU attempt
                        // Do not dispose PixelBuffer here; let normal ownership/GC handle them.
                        // (Existing callers expect returned PixelBuffers to be live.)

                        System.Diagnostics.Trace.TraceWarning($"GPU full-graph evaluation produced null/failed results for nodes: {string.Join(',', failedNodes)}; falling back to CPU path.");
                        // Fall through to CPU evaluation below
                    }
                    else
                    {
                        stopwatch.Stop();
                        LastMetrics = new(stopwatch.Elapsed, nodes.Count, nodes.Count, 0, false,
                            validation.Diagnostics.Count);
                        return resultMap;
                    }
                }

            // Partial GPU evaluation: identify maximal subgraphs composed solely of
            // IGpuNativeNode nodes that have no incoming edges from non-GPU nodes.
            // Evaluate each such subgraph using the GPU evaluator and read back
            // textures to PixelBuffers so downstream CPU nodes can consume them.
            if (Gpu.GpuScheduler.Instance.IsSupported)
            {
                var gpuNodeIds = new HashSet<int>(nodes.Where(n => n.Node is Gpu.IGpuNativeNode).Select(n => n.Id));

                // Map targetId -> incoming connections
                var incomingByTargetLocal = connections.GroupBy(c => c.TargetNodeId).ToDictionary(g => g.Key, g => g.ToList());

                // Eligible GPU nodes: GPU-native and none of their incoming connections originate from non-GPU nodes
                var eligible = new HashSet<int>();
                foreach (var n in nodes)
                {
                    if (!gpuNodeIds.Contains(n.Id)) continue;
                    var ok = true;
                    if (incomingByTargetLocal.TryGetValue(n.Id, out var incs))
                    {
                        foreach (var ic in incs)
                        {
                            if (!gpuNodeIds.Contains(ic.SourceNodeId)) { ok = false; break; }
                        }
                    }
                    if (ok) eligible.Add(n.Id);
                }

                if (eligible.Count > 0)
                {
                    // Build induced subgraph of eligible nodes and find connected components
                    var edgesAmongEligible = connections.Where(c => eligible.Contains(c.SourceNodeId) && eligible.Contains(c.TargetNodeId)).ToList();
                    var visited = new HashSet<int>();
                    var components = new List<List<int>>();

                    var adj = new Dictionary<int, List<int>>();
                    foreach (var id in eligible) adj[id] = new List<int>();
                    foreach (var e in edgesAmongEligible)
                    {
                        adj[e.SourceNodeId].Add(e.TargetNodeId);
                        adj[e.TargetNodeId].Add(e.SourceNodeId);
                    }

                    foreach (var id in eligible)
                    {
                        if (visited.Contains(id)) continue;
                        var stack = new Stack<int>();
                        var comp = new List<int>();
                        stack.Push(id);
                        visited.Add(id);
                        while (stack.Count > 0)
                        {
                            var cur = stack.Pop();
                            comp.Add(cur);
                            foreach (var nb in adj[cur])
                            {
                                if (!visited.Contains(nb)) { visited.Add(nb); stack.Push(nb); }
                            }
                        }
                        components.Add(comp);
                    }

                    // Evaluate each component independently on GPU
                    var resultsFromGpu = new Dictionary<int, PixelBuffer>();
                    foreach (var comp in components)
                    {
                        try
                        {
                            // Prepare instances and connections limited to this component
                            var compInstances = nodes.Where(n => comp.Contains(n.Id)).ToList();
                            var compConns = connections.Where(c => comp.Contains(c.SourceNodeId) && comp.Contains(c.TargetNodeId)).ToList();

                            var gpuEval = new Gpu.NodeGpuGraphEvaluator();
                            var texMap = gpuEval.EvaluateAllTextures(compInstances, compConns, context);
                            foreach (var kv in texMap)
                            {
                                var nid = kv.Key;
                                var tex = kv.Value;
                                if (tex == null) continue;
                                PixelBuffer? pb = null;
                                try
                                {
                                    pb = Gpu.GpuScheduler.Instance.Execute(() => Gpu.GpuCompute.ReadTextureToPixelBuffer(tex));
                                }
                                catch (SharpGen.Runtime.SharpGenException sgx)
                                {
                                    System.Diagnostics.Trace.TraceWarning($"Partial GPU readback SharpGenException for node {nid}: {sgx.Message}");
                                    pb = null;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Trace.TraceWarning($"Partial GPU readback failed for node {nid}: {ex.Message}");
                                    pb = null;
                                }
                                finally
                                {
                                    tex?.Dispose();
                                }

                                if (pb != null) resultsFromGpu[nid] = pb;
                            }
                        }
                        catch (Exception exComp)
                        {
                            System.Diagnostics.Trace.TraceWarning($"Partial GPU evaluation failed for component starting at {comp.FirstOrDefault()}: {exComp.Message}");
                        }
                    }

                    if (resultsFromGpu.Count > 0)
                    {
                        // Return the GPU-produced PixelBuffers merged with CPU path could also use them.
                        // Instead of returning immediately, we'll keep these ready and inject them into
                        // the CPU evaluation below by pre-populating the result dictionary.
                        // To do that, create a dictionary and later merge.
                        var prepop = new Dictionary<int, PixelBuffer>(resultsFromGpu);
                        // Merge with CPU path: we will use these as starting results.
                        // For simplicity, if prepop covers all nodes, return immediately.
                        if (prepop.Count == nodes.Count)
                        {
                            stopwatch.Stop();
                            LastMetrics = new(stopwatch.Elapsed, nodes.Count, nodes.Count, 0, false,
                                validation.Diagnostics.Count);
                            return prepop.ToDictionary(kv => kv.Key, kv => kv.Value);
                        }
                        // Otherwise fall through to CPU evaluation but with prepop seed.
                        // We'll carry 'prepop' forward by inserting into localResult when executing levels below.
                        // Store in a local variable via closure by assigning to a specially named variable.
                        prepopForCpu = prepop;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // If GPU path fails, fall back to CPU path below but surface debug info via trace
            System.Diagnostics.Trace.TraceWarning($"GPU evaluation path failed: {ex.Message}");
        }
#endif
        var nodeById = nodes.ToDictionary(n => n.Id);

        // Validate all source node IDs exist before topology processing.
        var validNodeIds = new HashSet<int>(nodes.Select(n => n.Id));
        var validConnections = new List<GraphConnection>();
        foreach (var conn in connections)
        {
            if (validNodeIds.Contains(conn.SourceNodeId) && validNodeIds.Contains(conn.TargetNodeId))
            {
                validConnections.Add(conn);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NodeGraphEvaluator: skipping dangling connection {conn.SourceNodeId}->{conn.TargetNodeId}");
            }
        }

        // Compile topology once and reuse it until nodes or edges change.
        var structureFingerprint = GraphFingerprint.ForStructure(nodes, validConnections);
        TopologyResult? topoResult;
        var reusedExecutionPlan = false;
        lock (_topologyGate)
        {
            if (_cachedTopology != null && _topologyFingerprint == structureFingerprint)
            {
                topoResult = _cachedTopology;
                reusedExecutionPlan = true;
            }
            else
            {
                topoResult = TopologicalSort(nodes, validConnections);
                _cachedTopology = topoResult;
                _topologyFingerprint = structureFingerprint;
            }
        }
        if (topoResult == null)
        {
            var msg = $"Circular dependency detected! Check connections.";
            System.Diagnostics.Debug.WriteLine($"NodeGraphEvaluator: {msg}");
            OnEvaluationError?.Invoke(msg);
            return new Dictionary<int, PixelBuffer>();
        }

        var sortedOrder = topoResult.SortedOrder;
        var level = topoResult.Level;
        var incomingByTarget = topoResult.IncomingByTarget;
        var groups = sortedOrder.GroupBy(id => level[id]).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

        // Store per-port outputs: result[nodeId][portIndex].  Single-output nodes wrap
        // their one buffer at index 0; IMultiOutputNode nodes fill all ports.
        var result = new System.Collections.Concurrent.ConcurrentDictionary<int, PixelBuffer[]>();
        var nodeFingerprints = new System.Collections.Concurrent.ConcurrentDictionary<int, ulong>();
        var evaluatedNodeCount = 0;
        var cacheHitCount = 0;
        var incrementalCacheEnabled = prepopForCpu == null;
        // If partial GPU evaluation produced prepopulated results, inject them here so
        // subsequent CPU evaluation can reuse them as upstream inputs.
        if (prepopForCpu != null)
        {
            foreach (var kv in prepopForCpu)
            {
                result[kv.Key] = [kv.Value];
            }
        }

        foreach (var grp in groups)
        {
            // Evaluate all nodes in this level in parallel
            System.Threading.Tasks.Parallel.ForEach(grp, new System.Threading.Tasks.ParallelOptions { CancellationToken = ct }, nodeId =>
            {
                // Skip nodes that were already evaluated by the GPU path (B1 fix)
                if (result.ContainsKey(nodeId))
                {
                    nodeFingerprints[nodeId] = GraphFingerprint.ForNode(
                        nodeById[nodeId], nodeById[nodeId].ParameterValues, context,
                        Array.Empty<(int Port, ulong Fingerprint)>());
                    return;
                }

                if (!nodeById.TryGetValue(nodeId, out var instance))
                    return;

                var inputCount = instance.Node.InputPorts.Count;
                var inputs = new PixelBuffer?[Math.Max(1, inputCount)];

                if (incomingByTarget.TryGetValue(nodeId, out var incomingList))
                {
                    foreach (var conn in incomingList)
                    {
                        if (conn.TargetPortIndex >= 0 && conn.TargetPortIndex < inputs.Length
                            && result.TryGetValue(conn.SourceNodeId, out var sourceBuffers)
                            && conn.SourcePortIndex >= 0 && conn.SourcePortIndex < sourceBuffers.Length
                            && sourceBuffers[conn.SourcePortIndex] is { } sourceBuffer)
                        {
                            // When a Mask-type output feeds an Any-type input (e.g. PreviewNode),
                            // convert the mask to a grayscale RGB view so it displays correctly.
                            PixelBuffer inputBuffer = sourceBuffer;
                            if (nodeById.TryGetValue(conn.SourceNodeId, out var srcInst)
                                && conn.SourcePortIndex < srcInst.Node.OutputPorts.Count
                                && conn.TargetPortIndex < instance.Node.InputPorts.Count)
                            {
                                var srcPortType = srcInst.Node.OutputPorts[conn.SourcePortIndex].Type;
                                var dstPortType = instance.Node.InputPorts[conn.TargetPortIndex].Type;

                                // Mask → Any: show mask as grayscale RGB
                                if (srcPortType == GraphPortType.Mask && dstPortType == GraphPortType.Any)
                                {
                                    inputBuffer = PixelBuffer.CreateMaskView(sourceBuffer);
                                }
                                // Image/Tile → Mask: convert using alpha-luminance heuristic
                                // for mask blending (prefers alpha channel when available,
                                // falls back to luminance for opaque pattern buffers)
                                else if (srcPortType == GraphPortType.Image && dstPortType == GraphPortType.Mask)
                                {
                                    inputBuffer = PixelBuffer.CreateMaskView(sourceBuffer);
                                }
                            }
                            inputs[conn.TargetPortIndex] = inputBuffer;
                        }
                    }
                }

                // Restore persistent state for IPersistentStateNode
                if (instance.Node is IPersistentStateNode psNode && instance.PersistentState != null)
                {
                    psNode.PersistentState = instance.PersistentState;
                }

                var derivedContext = context.WithSeedOffset(nodeId * 137);

                // Apply semantic parameter overrides from SemanticControlNode.
                // Use a merged dictionary so the original instance.ParameterValues is never mutated,
                // avoiding thread-safety issues in parallel evaluation.
                IReadOnlyDictionary<string, object> effectiveParams = instance.ParameterValues;
                if (context.SemanticOverrides is { Count: > 0 })
                {
                    var merged = new Dictionary<string, object>(instance.ParameterValues);
                    foreach (var (key, value) in context.SemanticOverrides)
                    {
                        if (merged.ContainsKey(key))
                        {
                            merged[key] = (double)value;
                        }
                    }
                    effectiveParams = merged;
                }

                var upstreamFingerprints = incomingByTarget.TryGetValue(nodeId, out var fingerprintInputs)
                    ? fingerprintInputs.Select(connection =>
                        (connection.TargetPortIndex,
                            nodeFingerprints.TryGetValue(connection.SourceNodeId, out var fingerprint)
                                ? fingerprint
                                : 0UL))
                    : Enumerable.Empty<(int, ulong)>();
                var nodeFingerprint = GraphFingerprint.ForNode(
                    instance, effectiveParams, derivedContext, upstreamFingerprints);

                var traits = instance.Node.Traits;
                var cacheable = incrementalCacheEnabled
                    && traits.HasFlag(GraphNodeTraits.Pure)
                    && traits.HasFlag(GraphNodeTraits.Deterministic)
                    && !traits.HasFlag(GraphNodeTraits.Stateful)
                    && !traits.HasFlag(GraphNodeTraits.TimeDependent)
                    && instance.Node is not IPersistentStateNode
                    && context.AnimationTime is null
                    && !instance.Node.Category.Equals("Animation", StringComparison.OrdinalIgnoreCase)
                    && !instance.Node.Category.Equals("Particle", StringComparison.OrdinalIgnoreCase)
                    && !instance.Node.Category.Equals("Physics", StringComparison.OrdinalIgnoreCase);

                // A volatile node must invalidate every downstream cache entry, even when
                // its parameters are unchanged (state, time or an external source may differ).
                if (!cacheable)
                    nodeFingerprint ^= evaluationGeneration * 1099511628211UL;
                nodeFingerprints[nodeId] = nodeFingerprint;

                if (cacheable && _resultCache.TryGet(nodeId, nodeFingerprint, out var cachedBuffers))
                {
                    result[nodeId] = cachedBuffers;
                    System.Threading.Interlocked.Increment(ref cacheHitCount);
                    return;
                }

                System.Threading.Interlocked.Increment(ref evaluatedNodeCount);

                // Prefer GPU-accelerated path when available. Nodes may optionally implement
                // IGpuAcceleratedNode and return a PixelBuffer from the GPU implementation.
                PixelBuffer? outBuf = null;
                if (instance.Node is IGpuAcceleratedNode gpuNode)
                {
                    try
                    {
                        outBuf = gpuNode.ProcessGpu(inputs, effectiveParams, derivedContext);
                    }
                    catch (Exception exGpu)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NodeGraphEvaluator] GPU node {instance.Node.TypeName} failed, falling back to CPU: {exGpu.Message}");
                        outBuf = null;
                    }
                }

                if (outBuf == null)
                {
                    // IMultiOutputNode nodes fill all output ports; single-output nodes wrap port 0.
                    // Try GPU-accelerated multi-output first if available.
                    if (instance.Node is IMultiOutputNode multiNode)
                    {
                        if (instance.Node is IGpuAcceleratedMultiOutputNode gpuMultiNode)
                        {
                            try
                            {
                                var gpuResult = gpuMultiNode.ProcessGpuMulti(inputs, effectiveParams, derivedContext);
                                if (gpuResult != null && gpuResult.Length >= 1)
                                {
                                    result[nodeId] = gpuResult;
                                    if (cacheable) _resultCache.Store(nodeId, nodeFingerprint, gpuResult);
                                    return;
                                }
                            }
                            catch (Exception exGpuMulti)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NodeGraphEvaluator] GPU multi-output node {instance.Node.TypeName} failed, falling back to CPU: {exGpuMulti.Message}");
                            }
                        }

                        var multiResult = multiNode.ProcessMulti(inputs, effectiveParams, derivedContext);
                        result[nodeId] = multiResult;
                        if (cacheable) _resultCache.Store(nodeId, nodeFingerprint, multiResult);
                        return;
                    }

                    outBuf = instance.Node.Process(inputs, effectiveParams, derivedContext);
                }

                result[nodeId] = [outBuf];
                if (cacheable) _resultCache.Store(nodeId, nodeFingerprint, [outBuf]);
            });
        }

        // Save persistent state from IPersistentStateNode back to GraphNodeInstance
        foreach (var inst in nodes)
        {
            if (inst.Node is IPersistentStateNode psNode && psNode.PersistentState != null)
            {
                inst.PersistentState = psNode.PersistentState;
                inst.LastEvaluatedFrame = context.AnimationFrame ?? 0;
            }
        }

        // Public API returns port-0 buffer per node for backward compatibility.
        stopwatch.Stop();
        LastMetrics = new(stopwatch.Elapsed, nodes.Count, evaluatedNodeCount, cacheHitCount,
            reusedExecutionPlan, validation.Diagnostics.Count);
        return result.ToDictionary(kv => kv.Key, kv => kv.Value[0]);
    }

    /// <summary>
    /// Shared topology result: sorted node IDs, level map, and incoming connections by target.
    /// </summary>
    internal sealed record TopologyResult(
        List<int> SortedOrder,
        Dictionary<int, int> Level,
        Dictionary<int, List<GraphConnection>> IncomingByTarget
    );

    /// <summary>
    /// Performs Kahn topological sort + level computation.
    /// Returns null when a cycle is detected.
    /// Shared between CPU and GPU evaluators.
    /// </summary>
    internal static TopologyResult? TopologicalSort(
        IReadOnlyList<GraphNodeInstance> nodes,
        IReadOnlyList<GraphConnection> validConnections)
    {
        // Pre-build O(1) lookup structures: O(E) total
        var incomingByTarget = new Dictionary<int, List<GraphConnection>>();
        var outgoingBySource = new Dictionary<int, List<GraphConnection>>();
        var inDegree = new Dictionary<int, int>();

        foreach (var node in nodes)
        {
            inDegree[node.Id] = 0;
            outgoingBySource[node.Id] = new List<GraphConnection>();
        }

        foreach (var conn in validConnections)
        {
            // Track incoming connections by target
            if (!incomingByTarget.TryGetValue(conn.TargetNodeId, out var incList))
                incomingByTarget[conn.TargetNodeId] = incList = new List<GraphConnection>();
            incList.Add(conn);

            // Track outgoing connections by source
            if (outgoingBySource.TryGetValue(conn.SourceNodeId, out var outList))
                outList.Add(conn);

            // Count in-degree
            if (inDegree.ContainsKey(conn.TargetNodeId))
                inDegree[conn.TargetNodeId]++;
        }

        // Kahn's algorithm — queue nodes with zero in-degree
        var queue = new Queue<int>();
        foreach (var kv in inDegree)
        {
            if (kv.Value == 0)
                queue.Enqueue(kv.Key);
        }

        var sortedOrder = new List<int>(nodes.Count);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            sortedOrder.Add(id);

            // O(1) lookup instead of validConnections.Where(...) which was O(E) per iteration
            if (outgoingBySource.TryGetValue(id, out var outConns))
            {
                foreach (var conn in outConns)
                {
                    if (inDegree.ContainsKey(conn.TargetNodeId))
                    {
                        inDegree[conn.TargetNodeId]--;
                        if (inDegree[conn.TargetNodeId] == 0)
                            queue.Enqueue(conn.TargetNodeId);
                    }
                }
            }
        }

        if (sortedOrder.Count != nodes.Count)
            return null; // cycle detected

        // Compute level for each node (distance from sources) — reuse incomingByTarget
        var level = new Dictionary<int, int>(nodes.Count);
        foreach (var id in sortedOrder)
        {
            if (incomingByTarget.TryGetValue(id, out var incConns) && incConns.Count > 0)
            {
                var max = 0;
                foreach (var c in incConns)
                {
                    var pLevel = level[c.SourceNodeId];
                    if (pLevel > max) max = pLevel;
                }
                level[id] = max + 1;
            }
            else
            {
                level[id] = 0;
            }
        }

        return new TopologyResult(sortedOrder, level, incomingByTarget);
    }
}
