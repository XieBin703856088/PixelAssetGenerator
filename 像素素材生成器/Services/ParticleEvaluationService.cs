using System;
using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Particles;
using PixelAssetGenerator.Core.Particles.Nodes;
using PixelAssetGenerator.Core.Physics.Nodes;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Coordinates particle system evaluation across frames.
/// Manages the persistent state of particle emitter/force/render nodes
/// and bridges particle data to the pixel rendering pipeline.
/// Now also integrates PhysicsSimulateNode into the simulation loop.
/// </summary>
public sealed class ParticleEvaluationService
{
    private readonly Dictionary<int, object> _persistentStates = new();

    public void RestoreState(IReadOnlyList<GraphNodeInstance> instances)
    {
        foreach (var inst in instances)
        {
            if (inst.Node is IPersistentStateNode psNode)
            {
                if (_persistentStates.TryGetValue(inst.Id, out var state))
                    psNode.PersistentState = state;
            }
        }
    }

    public void SaveState(IReadOnlyList<GraphNodeInstance> instances)
    {
        foreach (var inst in instances)
        {
            if (inst.Node is IPersistentStateNode psNode && psNode.PersistentState != null)
                _persistentStates[inst.Id] = psNode.PersistentState;
        }
    }

    public void SimulateParticleFrame(
        IReadOnlyList<GraphNodeInstance> instances,
        IReadOnlyDictionary<string, object> globalParameters,
        PixelGraphContext context)
    {
        // Step 1: Simulate all emitter nodes
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleEmitterNode emitter)
            {
                emitter.GetOrCreateState(inst.ParameterValues, context);
                emitter.SimulateFrame(inst.ParameterValues, context);
            }
        }

        // Step 2: Apply force nodes
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleForceNode forceNode)
            {
                var force = forceNode.CreateForce(inst.ParameterValues);
                // Pass global time to noise-based forces
                if (force is NoiseMotionForce nmf)
                    nmf.GlobalTime = context.GlobalTime;
                ApplyForceToEmitters(instances, force, context.DeltaTime);
            }
        }

        // Step 3: Apply physics simulation nodes
        foreach (var inst in instances)
        {
            if (inst.Node is PhysicsSimulateNode physNode)
            {
                var particleBuffer = FindUpstreamParticleBuffer(inst.Id, instances);
                if (particleBuffer != null)
                    physNode.SimulateFrame(inst.ParameterValues, context, particleBuffer);
            }
        }

        // Step 3b: Apply trail nodes
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleTrailNode trailNode)
            {
                var particleBuffer = FindUpstreamParticleBuffer(inst.Id, instances);
                if (particleBuffer != null)
                    trailNode.SimulateFrame(inst.ParameterValues, context, particleBuffer);
            }
        }

        // Step 4: Apply physics field nodes
        foreach (var inst in instances)
        {
            if (inst.Node is PhysicsFieldNode fieldNode)
            {
                var force = fieldNode.CreateForce(inst.ParameterValues, context.GlobalTime);
                ApplyForceToEmitters(instances, force, context.DeltaTime);
            }
        }
    }

    private static void ApplyForceToEmitters(
        IReadOnlyList<GraphNodeInstance> instances,
        IParticleForce force,
        float deltaTime)
    {
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleEmitterNode emitter)
            {
                if (emitter.PersistentState is ParticleEmitterNode.EmitterState es)
                {
                    es.Simulator.AddForce(force);
                }
            }
        }
    }

    /// <summary>
    /// Finds the upstream particle buffer for a physics node.
    /// Currently scans all emitters (works for single-emitter graphs).
    /// TODO: Use graph topology for multi-emitter support.
    /// </summary>
    private static ParticleBuffer? FindUpstreamParticleBuffer(
        int physicsNodeId,
        IReadOnlyList<GraphNodeInstance> instances)
    {
        // Try matching by PersistedStateKey if available
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleEmitterNode emitter)
            {
                if (emitter.PersistentState is ParticleEmitterNode.EmitterState es
                    && es.Initialized && es.Buffer.ActiveCount > 0)
                    return es.Buffer;
            }
        }
        return null;
    }

    public void RenderParticles(
        IReadOnlyList<GraphNodeInstance> instances,
        Dictionary<int, PixelBuffer> frameBuffers)
    {
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleRenderNode renderNode)
            {
                renderNode.ApplyParameters(inst.ParameterValues);
                var renderer = renderNode.PersistentState as ParticleRenderer;
                if (renderer == null) continue;

                var emitterBuffer = FindUpstreamEmitterBuffer(inst.Id, instances);
                if (emitterBuffer == null) continue;

                if (frameBuffers.TryGetValue(inst.Id, out var output))
                    renderer.Render(emitterBuffer.ActiveSpan(), output, emitterBuffer.ActiveCount);
            }
        }
    }

    /// <summary>
    /// Finds the upstream particle buffer for a render node.
    /// Currently scans all emitters (works for single-emitter graphs).
    /// TODO: Use graph topology for multi-emitter support.
    /// </summary>
    private static ParticleBuffer? FindUpstreamEmitterBuffer(
        int renderNodeId,
        IReadOnlyList<GraphNodeInstance> instances)
    {
        foreach (var inst in instances)
        {
            if (inst.Node is ParticleEmitterNode emitter)
            {
                if (emitter.PersistentState is ParticleEmitterNode.EmitterState es
                    && es.Initialized && es.Buffer.ActiveCount > 0)
                    return es.Buffer;
            }
        }
        return null;
    }

    public void ClearState()
    {
        foreach (var state in _persistentStates.Values)
        {
            if (state is ParticleEmitterNode.EmitterState es)
            {
                es.Buffer.Dispose();
            }
        }
        _persistentStates.Clear();
    }
}
