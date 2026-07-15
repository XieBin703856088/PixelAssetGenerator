using System;
using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Particles;
using PixelAssetGenerator.Core.Particles.Nodes;
using PixelAssetGenerator.Core.Physics.Nodes;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Stateful particle graph runtime. Particle buffers are routed by actual Particle
/// connections, while image/float ports continue through the normal graph evaluator.
/// </summary>
public sealed class ParticleEvaluationService
{
    private readonly Dictionary<int, object> _persistentStates = new();

    public bool HasParticleState => _persistentStates.Values.Any(state => state is ParticleEmitterNode.EmitterState);

    public void RestoreState(IReadOnlyList<GraphNodeInstance> instances)
    {
        foreach (var instance in instances)
            if (instance.Node is IPersistentStateNode stateful &&
                _persistentStates.TryGetValue(instance.Id, out var state))
                stateful.PersistentState = state;
    }

    public void SaveState(IReadOnlyList<GraphNodeInstance> instances)
    {
        foreach (var instance in instances)
            if (instance.Node is IPersistentStateNode { PersistentState: not null } stateful)
                _persistentStates[instance.Id] = stateful.PersistentState;
    }

    /// <summary>Backward-compatible single-emitter simulation entry point.</summary>
    public void SimulateParticleFrame(IReadOnlyList<GraphNodeInstance> instances,
        IReadOnlyDictionary<string, object> globalParameters, PixelGraphContext context)
        => SimulateParticleFrame(instances, Array.Empty<GraphConnection>(), context);

    public void SimulateParticleFrame(IReadOnlyList<GraphNodeInstance> instances,
        IReadOnlyList<GraphConnection> connections, PixelGraphContext context)
    {
        var instanceMap = instances.ToDictionary(instance => instance.Id);
        var emitters = instances
            .Where(instance => instance.Node is ParticleEmitterNode)
            .ToDictionary(instance => instance.Id);

        // Initialize emitters and rebuild the force list every frame. The previous
        // implementation accumulated duplicate force objects indefinitely.
        foreach (var instance in emitters.Values)
        {
            var emitterNode = (ParticleEmitterNode)instance.Node;
            var state = emitterNode.GetOrCreateState(instance.ParameterValues, context);
            state.Simulator.ClearForces();
        }

        foreach (var instance in instances)
        {
            IParticleForce? force = instance.Node switch
            {
                ParticleForceNode forceNode => forceNode.CreateForce(instance.ParameterValues, context),
                InteractiveForceNode interactive => interactive.CreateForce(instance.ParameterValues,
                    interactive.LastPositionXInput, interactive.LastPositionYInput),
                PhysicsFieldNode field => field.CreateForce(instance.ParameterValues, context.GlobalTime),
                _ => null
            };
            if (force == null) continue;

            foreach (var emitter in FindUpstreamEmitters(instance.Id, instanceMap, connections, emitters))
                ((ParticleEmitterNode.EmitterState)((IPersistentStateNode)emitter.Node).PersistentState!)
                    .Simulator.AddForce(force);
        }

        // Emit and integrate only after the complete same-frame force graph is ready.
        foreach (var instance in emitters.Values)
        {
            var emitterNode = (ParticleEmitterNode)instance.Node;
            emitterNode.SimulateFrame(instance.ParameterValues, context,
                ReadScalar(emitterNode.LastPositionXInput), ReadScalar(emitterNode.LastPositionYInput),
                ReadScalar(emitterNode.LastEmissionRateInput), ReadScalar(emitterNode.LastSpeedInput),
                ReadScalar(emitterNode.LastSizeInput));
        }

        // Stateful/post-integration modifiers follow graph topology instead of
        // applying globally to every emitter in the document.
        foreach (var instance in instances)
        {
            var upstream = FindUpstreamEmitters(instance.Id, instanceMap, connections, emitters).ToArray();
            if (upstream.Length == 0) continue;
            foreach (var emitter in upstream)
            {
                var state = (ParticleEmitterNode.EmitterState)((IPersistentStateNode)emitter.Node).PersistentState!;
                switch (instance.Node)
                {
                    case ParticleCollisionNode collision:
                        collision.ApplyCollisions(state.Buffer, context.DeltaTime, instance.ParameterValues);
                        break;
                    case ParticleTrailNode trail:
                        trail.SimulateFrame(instance.ParameterValues, context, state.Buffer);
                        break;
                    case ParticleBehaviorNode behavior:
                        behavior.ApplyBehavior(state.Buffer, instance.ParameterValues, context);
                        break;
                    case PhysicsSimulateNode physics:
                        var constraints = FindDownstreamConstraints(instance.Id, instanceMap, connections)
                            .Select(constraint => ((PhysicsConstraintNode)constraint.Node,
                                (IReadOnlyDictionary<string, object>)constraint.ParameterValues));
                        physics.SimulateFrame(instance.ParameterValues, context, state.Buffer, constraints);
                        break;
                }
            }
        }
    }

    public void RenderParticles(IReadOnlyList<GraphNodeInstance> instances,
        Dictionary<int, PixelBuffer> frameBuffers)
        => RenderParticles(instances, Array.Empty<GraphConnection>(), frameBuffers);

    public void RenderParticles(IReadOnlyList<GraphNodeInstance> instances,
        IReadOnlyList<GraphConnection> connections, Dictionary<int, PixelBuffer> frameBuffers)
    {
        var instanceMap = instances.ToDictionary(instance => instance.Id);
        var emitters = instances
            .Where(instance => instance.Node is ParticleEmitterNode)
            .ToDictionary(instance => instance.Id);

        foreach (var instance in instances)
        {
            if (!frameBuffers.TryGetValue(instance.Id, out var output))
                continue;
            var upstream = FindUpstreamEmitters(instance.Id, instanceMap, connections, emitters).ToArray();
            if (upstream.Length == 0)
                continue;

            switch (instance.Node)
            {
                case ParticleRenderNode renderNode:
                    renderNode.ApplyParameters(instance.ParameterValues);
                    if (renderNode.PersistentState is not ParticleRenderer renderer)
                        continue;
                    foreach (var emitter in upstream)
                    {
                        var emitterPreset = GraphNodeBase.GetChoice(emitter.ParameterValues, "preset", "manual");
                        renderNode.ApplyParameters(instance.ParameterValues, emitterPreset);
                        var state = (ParticleEmitterNode.EmitterState)((IPersistentStateNode)emitter.Node).PersistentState!;
                        renderer.Render(state.Buffer.ActiveSpan(), output, state.Buffer.ActiveCount);
                    }
                    break;

                case ParticleLightNode lightNode:
                    foreach (var emitter in upstream)
                    {
                        var state = (ParticleEmitterNode.EmitterState)((IPersistentStateNode)emitter.Node).PersistentState!;
                        lightNode.RenderGlow(state.Buffer, output, instance.ParameterValues);
                    }
                    break;
            }
        }
    }

    private static IEnumerable<GraphNodeInstance> FindUpstreamEmitters(int targetNodeId,
        IReadOnlyDictionary<int, GraphNodeInstance> instanceMap,
        IReadOnlyList<GraphConnection> connections,
        IReadOnlyDictionary<int, GraphNodeInstance> emitters)
    {
        var found = new HashSet<int>();
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(targetNodeId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (emitters.ContainsKey(current))
            {
                found.Add(current);
                continue;
            }

            foreach (var connection in connections.Where(connection => connection.TargetNodeId == current))
            {
                if (!instanceMap.TryGetValue(connection.TargetNodeId, out var target) ||
                    !instanceMap.TryGetValue(connection.SourceNodeId, out var source) ||
                    connection.TargetPortIndex < 0 || connection.TargetPortIndex >= target.Node.InputPorts.Count ||
                    connection.SourcePortIndex < 0 || connection.SourcePortIndex >= source.Node.OutputPorts.Count)
                    continue;
                if (target.Node.InputPorts[connection.TargetPortIndex].Type == GraphPortType.Particle &&
                    source.Node.OutputPorts[connection.SourcePortIndex].Type == GraphPortType.Particle)
                    stack.Push(connection.SourceNodeId);
            }
        }

        // Old projects could not connect ParticleRender to a particle port because it
        // did not exist. Preserve a useful migration path only when unambiguous.
        if (found.Count == 0 && emitters.Count == 1)
            found.Add(emitters.Keys.Single());
        return found.Select(id => emitters[id]);
    }

    private static IEnumerable<GraphNodeInstance> FindDownstreamConstraints(int sourceNodeId,
        IReadOnlyDictionary<int, GraphNodeInstance> instanceMap,
        IReadOnlyList<GraphConnection> connections)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(sourceNodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current != sourceNodeId && instanceMap.TryGetValue(current, out var instance)
                && instance.Node is PhysicsConstraintNode)
                yield return instance;

            foreach (var connection in connections.Where(connection => connection.SourceNodeId == current))
            {
                if (!instanceMap.TryGetValue(connection.SourceNodeId, out var source)
                    || !instanceMap.TryGetValue(connection.TargetNodeId, out var target)
                    || connection.SourcePortIndex < 0 || connection.SourcePortIndex >= source.Node.OutputPorts.Count
                    || connection.TargetPortIndex < 0 || connection.TargetPortIndex >= target.Node.InputPorts.Count)
                    continue;
                if (source.Node.OutputPorts[connection.SourcePortIndex].Type == GraphPortType.Particle
                    && target.Node.InputPorts[connection.TargetPortIndex].Type == GraphPortType.Particle)
                    queue.Enqueue(connection.TargetNodeId);
            }
        }
    }

    private static float? ReadScalar(PixelBuffer? buffer)
        => buffer == null ? null : buffer.GetPixel(0, 0).R;

    /// <summary>
    /// Clears one node's simulation state without restarting unrelated particle
    /// workflows on the same canvas.
    /// </summary>
    public bool ClearState(int nodeId)
    {
        if (!_persistentStates.Remove(nodeId, out var state))
            return false;
        DisposeState(state);
        return true;
    }

    public void ClearState()
    {
        foreach (var state in _persistentStates.Values)
            DisposeState(state);
        _persistentStates.Clear();
    }

    private static void DisposeState(object state)
    {
        if (state is ParticleEmitterNode.EmitterState emitterState)
            emitterState.Buffer.Dispose();
        else if (state is IDisposable disposable)
            disposable.Dispose();
    }
}
