using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Particle trail node — creates afterimage/trail particles along each particle's
/// motion path. Stores trajectory points across frames and spawns short-lived
/// "ghost" particles at recorded positions.
/// Input: ParticleBuffer, Output: ParticleBuffer.
/// </summary>
public sealed class ParticleTrailNode : IPersistentStateNode
{
    public string TypeName => "ParticleTrail";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("trailLength", 0.5, 0.1, 2.0, 0.1, "拖尾持续时间"),
        NodeParameterDefinition.Integer("segments", 5, 1, 20, 1, "轨迹分段数"),
        NodeParameterDefinition.Number("fadeAlpha", 0.5, 0, 1.0, 0.05, "透明度衰减"),
        NodeParameterDefinition.Number("sizeScale", 0.5, 0, 1.0, 0.05, "大小缩放"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    /// <summary>
    /// Per-particle trajectory: a list of recorded positions with remaining life.
    /// </summary>
    public sealed record TrailState(
        List<(int Index, float X, float Y, float Life)> TrailPoints,
        bool Initialized);

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // ParticleTrailNode doesn't produce a PixelBuffer directly.
        // Trail particles are injected into the emitter's buffer via SimulateFrame.
        return PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Gets or creates the persistent trail state.
    /// </summary>
    public TrailState GetOrCreateState()
    {
        if (PersistentState is TrailState ts && ts.Initialized)
            return ts;

        ts = new TrailState(new List<(int, float, float, float)>(), true);
        PersistentState = ts;
        return ts;
    }

    /// <summary>
    /// Simulates one frame of trail generation.
    /// Records current particle positions and spawns ghost particles on
    /// previously recorded trajectory points.
    /// Called by ParticleEvaluationService after emitter and force simulation.
    /// </summary>
    public void SimulateFrame(
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context,
        ParticleBuffer particleBuffer)
    {
        if (particleBuffer == null) return;

        var state = GetOrCreateState();
        var trailPoints = state.TrailPoints;
        var deltaTime = context.DeltaTime;

        var trailLength = GraphNodeBase.GetFloat(parameters, "trailLength", 0.5f);
        var segments = GraphNodeBase.GetInt(parameters, "segments", 5);
        var fadeAlpha = GraphNodeBase.GetFloat(parameters, "fadeAlpha", 0.5f);
        var sizeScale = GraphNodeBase.GetFloat(parameters, "sizeScale", 0.5f);

        // ── Step 1: Age existing trail points ──
        var lifeDecay = deltaTime / Math.Max(trailLength, 0.001f);
        for (var i = trailPoints.Count - 1; i >= 0; i--)
        {
            var tp = trailPoints[i];
            tp.Life -= lifeDecay;
            if (tp.Life <= 0f)
                trailPoints.RemoveAt(i);
            else
                trailPoints[i] = tp;
        }

        // ── Step 2: Record current positions of active particles ──
        var activeCount = particleBuffer.ActiveCount;
        var span = particleBuffer.AsSpan();

        // Record position every N frames based on segments count
        // (segments controls how many ghost particles trail behind each frame)
        for (var i = 0; i < activeCount; i++)
        {
            ref readonly var p = ref span[i];
            if (!p.Active || p.IsTrailGhost) continue;

            // Add current position as a trail point (full life)
            trailPoints.Add((i, p.X, p.Y, 1f));
        }

        // ── Step 3: Spawn ghost particles from trail points ──
        // We inject ghost particles into the buffer's dead slots.
        // Ghosts are short-lived, faded, smaller copies of the original particle.
        var capacity = particleBuffer.Capacity;
        var trailCount = trailPoints.Count;
        var inserted = 0;
        var maxGhosts = segments * activeCount; // limit ghosts per frame

        for (var ti = 0; ti < trailCount && inserted < maxGhosts; ti++)
        {
            var tp = trailPoints[ti];
            if (tp.Life >= 1f) continue; // skip the current frame's fresh point

            // Find original particle data for color/size reference
            float srcR = 1f, srcG = 1f, srcB = 1f, srcA = 0.5f;
            float srcSize = 0.02f;

            if (tp.Index < activeCount)
            {
                ref readonly var src = ref span[tp.Index];
                if (src.Active)
                {
                    srcR = src.R;
                    srcG = src.G;
                    srcB = src.B;
                    srcA = src.A;
                    srcSize = src.Size;
                }
            }

            // Find a dead slot to place this ghost
            for (var j = 0; j < capacity; j++)
            {
                if (inserted >= segments) break;

                ref var slot = ref span[j];
                if (slot.Active) continue;

                // Ghost particle: short life, faded color, smaller size
                var ghostLife = trailLength * tp.Life * 0.5f;
                if (ghostLife < 0.001f) continue;

                var alphaFactor = tp.Life * (1f - fadeAlpha) + fadeAlpha;
                slot = ParticleData.Create(
                    tp.X, tp.Y,
                    0f, 0f,           // no velocity (stationary ghost)
                    ghostLife,
                    srcSize * sizeScale * (0.5f + tp.Life * 0.5f),
                    0f, 0f,           // no rotation
                    srcR, srcG, srcB, srcA * alphaFactor,
                    srcR, srcG, srcB, 0f, // fade to transparent
                    srcSize * sizeScale, srcSize * sizeScale * 0.1f
                );
                slot.IsTrailGhost = true;
                inserted++;

                // Extend active count if this slot is beyond current range
                if (j >= particleBuffer.ActiveCount)
                    particleBuffer.ActiveCount = j + 1;
                break;
            }
        }
    }
}
