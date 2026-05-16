using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Particle emitter node — emits particles based on configuration parameters.
/// Maintains persistent state across frames via IPersistentStateNode.
/// Outputs a ParticleBuffer that can be fed into physics/force/render nodes.
/// Input mask controls the spawn position (optional).
/// </summary>
public sealed class ParticleEmitterNode : IPersistentStateNode, IExclusiveInputNode
{
    public string TypeName => "ParticleEmitter";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Mask", GraphPortType.Mask)
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("emissionRate", 50, 1, 500, 1, "发射率"),
        NodeParameterDefinition.Integer("burstCount", 20, 0, 200, 1, "爆发数量"),
        NodeParameterDefinition.Number("lifespan", 2.0, 0.1, 10.0, 0.1, "粒子寿命"),
        NodeParameterDefinition.Number("speed", 0.3, 0.01, 1.0, 0.01, "速度"),
        NodeParameterDefinition.Number("speedRandom", 0.5, 0, 1.0, 0.01, "速度随机"),
        NodeParameterDefinition.Number("angle", 270, 0, 360, 1, "发射角度"),
        NodeParameterDefinition.Number("spread", 30, 0, 180, 1, "扩散角度"),
        NodeParameterDefinition.Number("startSize", 0.05, 0.005, 0.5, 0.005, "起始大小"),
        NodeParameterDefinition.Number("endSize", 0.01, 0, 0.5, 0.005, "结束大小"),
        NodeParameterDefinition.Color("startColor", Colors.White, "起始颜色"),
        NodeParameterDefinition.Color("endColor", Color.FromArgb(0, 255, 255, 255), "结束颜色"),
        NodeParameterDefinition.Number("gravity", 0.2, -1.0, 1.0, 0.01, "重力"),
        NodeParameterDefinition.Number("damping", 0.98, 0.0, 1.0, 0.005, "阻尼"),
        NodeParameterDefinition.Boolean("oneShot", false, "一次性爆发"),
        NodeParameterDefinition.Boolean("tiling", true, "平铺模式"),
        NodeParameterDefinition.Number("wind", 0, -1.0, 1.0, 0.01, "风力"),
        NodeParameterDefinition.Boolean("emitFromMask", false, "从遮罩采样发射"),
        NodeParameterDefinition.Integer("maxParticles", 500, 1, 10000, 1, "最大粒子数"),
        NodeParameterDefinition.Choice("emissionShape", "point", new[] { "point", "line", "rectangle", "circle", "ring" },
            new[] { "点", "线", "矩形", "圆形", "环形" }, "发射形状"),
        NodeParameterDefinition.Number("shapeWidth", 0.1, 0, 1.0, 0.01, "形状宽度"),
        NodeParameterDefinition.Number("shapeHeight", 0.1, 0, 1.0, 0.01, "形状高度"),
        NodeParameterDefinition.Number("rotationRandom", 3.14, 0, 6.28, 0.01, "随机旋转"),
        NodeParameterDefinition.Number("angularVelocityRandom", 0, 0, 10, 0.1, "随机角速度"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    /// <summary>
    /// Internal state carried across frames: emitter + simulator + particle buffer.
    /// </summary>
    public sealed record EmitterState(
        ParticleEmitter Emitter,
        ParticleSimulator Simulator,
        ParticleBuffer Buffer,
        bool Initialized);

    // ── Process ──

    private static PixelBuffer? _sharedPlaceholder;

    /// <summary>
    /// Cache of the latest mask input, used by SimulateFrame for emission sampling.
    /// </summary>
    internal PixelBuffer? LastMaskInput { get; private set; }

    /// <summary>
    /// Pre-built position lookup table from mask for mask-based emission.
    /// Rebuilt when mask changes.
    /// </summary>
    private List<(float X, float Y)>? _maskPositions;
    private int _lastMaskHash;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // ParticleEmitterNode doesn't produce a PixelBuffer output directly.
        // The actual pixel data is produced by ParticleRenderNode.
        // Cache mask input (port 0) for emission position sampling in SimulateFrame.
        LastMaskInput = inputs.Length > 0 ? inputs[0] : null;

        // Return a shared 1x1 transparent placeholder to satisfy the evaluator
        // without allocating per-frame buffers.
        if (_sharedPlaceholder == null)
        {
            _sharedPlaceholder = PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
        }
        return _sharedPlaceholder;
    }

    /// <summary>
    /// Gets or creates the persistent emitter state for this frame.
    /// This is called by the evaluator via IPersistentStateNode.
    /// </summary>
    public EmitterState GetOrCreateState(
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        if (PersistentState is EmitterState es && es.Initialized)
            return es;

        var maxParticles = GraphNodeBase.GetInt(parameters, "maxParticles", 500);
        var buffer = new ParticleBuffer(maxParticles);

        var emitter = new ParticleEmitter(context.Seed)
        {
            EmissionRate = GraphNodeBase.GetFloat(parameters, "emissionRate", 50),
            BurstCount = GraphNodeBase.GetInt(parameters, "burstCount", 20),
            LifeMin = GraphNodeBase.GetFloat(parameters, "lifespan", 2f),
            LifeMax = GraphNodeBase.GetFloat(parameters, "lifespan", 2f),
            SpeedMin = 0f,
            SpeedMax = GraphNodeBase.GetFloat(parameters, "speed", 0.3f) * (1f + GraphNodeBase.GetFloat(parameters, "speedRandom", 0.5f)),
            Angle = GraphNodeBase.GetFloat(parameters, "angle", 270f),
            Spread = GraphNodeBase.GetFloat(parameters, "spread", 30f),
            SizeMin = GraphNodeBase.GetFloat(parameters, "startSize", 0.05f),
            SizeMax = GraphNodeBase.GetFloat(parameters, "startSize", 0.05f),
            EndSizeMultiplier = GraphNodeBase.GetFloat(parameters, "endSize", 0.01f) / Math.Max(GraphNodeBase.GetFloat(parameters, "startSize", 0.05f), 0.001f),
            OneShot = GraphNodeBase.GetBool(parameters, "oneShot", false),
            RotationRandom = GraphNodeBase.GetFloat(parameters, "rotationRandom", 0f),
            AngularVelocityRandom = GraphNodeBase.GetFloat(parameters, "angularVelocityRandom", 0f),
        };

        // Colors
        var startColor = GraphNodeBase.GetColor(parameters, "startColor", Colors.White);
        var endColor = GraphNodeBase.GetColor(parameters, "endColor", Color.FromArgb(0, 255, 255, 255));
        emitter.StartR = startColor.R / 255f;
        emitter.StartG = startColor.G / 255f;
        emitter.StartB = startColor.B / 255f;
        emitter.StartA = startColor.A / 255f;
        emitter.EndR = endColor.R / 255f;
        emitter.EndG = endColor.G / 255f;
        emitter.EndB = endColor.B / 255f;
        emitter.EndA = endColor.A / 255f;

        // Emission shape
        var shape = GraphNodeBase.GetChoice(parameters, "emissionShape", "point");
        emitter.Shape = shape switch
        {
            "line" => EmissionShape.Line,
            "rectangle" => EmissionShape.Rectangle,
            "circle" => EmissionShape.Circle,
            "ring" => EmissionShape.Ring,
            _ => EmissionShape.Point
        };
        emitter.ShapeWidth = (float)GraphNodeBase.GetFloat(parameters, "shapeWidth", 0.1f);
        emitter.ShapeHeight = (float)GraphNodeBase.GetFloat(parameters, "shapeHeight", 0.1f);

        var simulator = new ParticleSimulator
        {
            GravityX = 0,
            GravityY = GraphNodeBase.GetFloat(parameters, "gravity", 0.2f),
            Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.98f),
            TilingMode = GraphNodeBase.GetBool(parameters, "tiling", true),
            WindX = GraphNodeBase.GetFloat(parameters, "wind", 0f),
        };

        // Emit initial burst
        emitter.OneShot = false;
        emitter.BurstEmit(buffer);

        es = new EmitterState(emitter, simulator, buffer, true);
        PersistentState = es;
        return es;
    }

    /// <summary>
    /// Processes one frame of particle simulation.
    /// </summary>
    public void SimulateFrame(IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        if (PersistentState is not EmitterState es || !es.Initialized)
            return;

        var deltaTime = context.DeltaTime;

        // Update emitter parameters (they may change between frames)
        var emitter = es.Emitter;
        emitter.EmissionRate = GraphNodeBase.GetFloat(parameters, "emissionRate", 50);
        var speed = GraphNodeBase.GetFloat(parameters, "speed", 0.3f);
        var speedRandom = GraphNodeBase.GetFloat(parameters, "speedRandom", 0.5f);
        emitter.SpeedMin = speed * (1f - speedRandom * 0.5f);
        emitter.SpeedMax = speed * (1f + speedRandom * 0.5f);
        emitter.Angle = GraphNodeBase.GetFloat(parameters, "angle", 270f);
        emitter.Spread = GraphNodeBase.GetFloat(parameters, "spread", 30f);

        // Update simulator parameters
        var sim = es.Simulator;
        sim.GravityY = GraphNodeBase.GetFloat(parameters, "gravity", 0.2f);
        sim.Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.98f);
        sim.TilingMode = GraphNodeBase.GetBool(parameters, "tiling", true);
        sim.WindX = GraphNodeBase.GetFloat(parameters, "wind", 0f);

        // Emit new particles
        var emitFromMask = GraphNodeBase.GetBool(parameters, "emitFromMask", false);
        if (emitFromMask && LastMaskInput != null)
        {
            // Build or rebuild position table from mask
            BuildMaskPositions(LastMaskInput, context.Seed);
            // Emit using mask positions
            if (_maskPositions != null && _maskPositions.Count > 0)
            {
                var emitCount = Math.Max(1, (int)(deltaTime * emitter.EmissionRate));
                var count = Math.Min(emitCount, _maskPositions.Count);
                var maskSlice = _maskPositions.GetRange(0, count);
                emitter.EmitFromPositions(es.Buffer, maskSlice);
            }
        }
        else
        {
            emitter.Emit(deltaTime, es.Buffer);
        }

        // Simulate
        sim.Update(deltaTime, es.Buffer);
    }

    /// <summary>
    /// Builds a position lookup table from a mask PixelBuffer.
    /// Pixels with luminance > threshold are candidates. One entry per opaque pixel,
    /// normalized to [0, 1] UV space.
    /// </summary>
    private void BuildMaskPositions(PixelBuffer mask, int seed)
    {
        var w = mask.Width;
        var h = mask.Height;
        var span = mask.AsReadOnlySpan();

        // Quick hash to detect mask changes
        var hash = seed;
        var stride = Math.Max(1, w * h / 256);
        for (var i = 0; i < span.Length; i += stride * 4)
            hash = unchecked(hash * 31 + (int)(span[i] * 255));

        if (_maskPositions != null && hash == _lastMaskHash)
            return;

        _lastMaskHash = hash;
        _maskPositions = new List<(float X, float Y)>();

        // Collect all pixels above luminance threshold
        const float threshold = 0.1f;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var idx = (y * w + x) * 4;
                // Luminance from BGRA
                var lum = span[idx] * 0.114f + span[idx + 1] * 0.587f + span[idx + 2] * 0.299f;
                if (lum > threshold)
                {
                    _maskPositions.Add(((x + 0.5f) / w, (y + 0.5f) / h));
                }
            }
        }

        // Shuffle with deterministic seed
        var rng = new Random(seed);
        for (var i = _maskPositions.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_maskPositions[i], _maskPositions[j]) = (_maskPositions[j], _maskPositions[i]);
        }
    }
}
