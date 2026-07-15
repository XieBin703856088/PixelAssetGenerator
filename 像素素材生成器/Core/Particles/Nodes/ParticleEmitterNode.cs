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
public sealed class ParticleEmitterNode : IPersistentStateNode
{
    public string TypeName => "ParticleEmitter";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Mask", GraphPortType.Mask, "mask"),
        new GraphNodePort("PositionX", GraphPortType.Float, "positionX"),
        new GraphNodePort("PositionY", GraphPortType.Float, "positionY"),
        new GraphNodePort("EmissionRate", GraphPortType.Float, "emissionRate"),
        new GraphNodePort("Speed", GraphPortType.Float, "speed"),
        new GraphNodePort("Size", GraphPortType.Float, "size")
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("preset", "manual",
            new[] { "manual", "fire", "smoke", "rain", "snow", "sparks", "magic", "dust", "explosion", "bubbles", "leaves" },
            new[] { "手动", "火焰", "烟雾", "雨水", "雪花", "火花", "魔法", "尘土", "爆炸", "气泡", "落叶" }, "效果预设"),
        NodeParameterDefinition.Number("presetIntensity", 1.0, 0.1, 4.0, 0.05, "预设强度"),
        NodeParameterDefinition.Number("presetScale", 1.0, 0.25, 4.0, 0.05, "预设尺寸"),
        NodeParameterDefinition.Number("emissionRate", 50, 1, 500, 1, "发射率"),
        NodeParameterDefinition.Integer("burstCount", 20, 0, 200, 1, "爆发数量"),
        NodeParameterDefinition.Number("lifespan", 2.0, 0.1, 10.0, 0.1, "粒子寿命"),
        NodeParameterDefinition.Number("lifeRandom", 0.15, 0, 1.0, 0.01, "寿命随机"),
        NodeParameterDefinition.Number("speed", 0.3, 0.01, 1.0, 0.01, "速度"),
        NodeParameterDefinition.Number("speedRandom", 0.5, 0, 1.0, 0.01, "速度随机"),
        NodeParameterDefinition.Number("angle", 270, 0, 360, 1, "发射角度"),
        NodeParameterDefinition.Number("spread", 30, 0, 180, 1, "扩散角度"),
        NodeParameterDefinition.Number("startSize", 0.05, 0.005, 0.5, 0.005, "起始大小"),
        NodeParameterDefinition.Number("endSize", 0.01, 0, 0.5, 0.005, "结束大小"),
        NodeParameterDefinition.Number("sizeRandom", 0.15, 0, 1.0, 0.01, "大小随机"),
        NodeParameterDefinition.Color("startColor", Colors.White, "起始颜色"),
        NodeParameterDefinition.Color("endColor", Color.FromArgb(0, 255, 255, 255), "结束颜色"),
        NodeParameterDefinition.Number("gravity", 0.2, -1.0, 1.0, 0.01, "重力"),
        NodeParameterDefinition.Number("gravityX", 0, -2.0, 2.0, 0.01, "横向重力"),
        NodeParameterDefinition.Number("damping", 0.98, 0.0, 1.0, 0.005, "阻尼"),
        NodeParameterDefinition.Boolean("oneShot", false, "一次性爆发"),
        NodeParameterDefinition.Boolean("tiling", true, "平铺模式"),
        NodeParameterDefinition.Number("wind", 0, -1.0, 1.0, 0.01, "风力"),
        NodeParameterDefinition.Number("windY", 0, -1.0, 1.0, 0.01, "纵向风力"),
        NodeParameterDefinition.Boolean("prewarm", true, "预热模拟"),
        NodeParameterDefinition.Number("prewarmSeconds", 0.35, 0, 5.0, 0.05, "预热秒数"),
        NodeParameterDefinition.Boolean("emitFromMask", false, "从遮罩采样发射"),
        NodeParameterDefinition.Integer("maxParticles", 500, 1, 10000, 1, "最大粒子数"),
        NodeParameterDefinition.Choice("emissionShape", "point", new[] { "point", "line", "rectangle", "circle", "ring" },
            new[] { "点", "线", "矩形", "圆形", "环形" }, "发射形状"),
        NodeParameterDefinition.Number("positionX", 0.5, 0, 1.0, 0.01, "发射位置X"),
        NodeParameterDefinition.Number("positionY", 0.5, 0, 1.0, 0.01, "发射位置Y"),
        NodeParameterDefinition.Number("shapeWidth", 0.1, 0, 1.0, 0.01, "形状宽度"),
        NodeParameterDefinition.Number("shapeHeight", 0.1, 0, 1.0, 0.01, "形状高度"),
        NodeParameterDefinition.Number("rotationRandom", 3.14, 0, 6.28, 0.01, "随机旋转"),
        NodeParameterDefinition.Number("angularVelocityRandom", 0, 0, 10, 0.1, "随机角速度"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.TimeDependent;

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
        bool Initialized)
    {
        /// <summary>
        /// Last resolved values are tracked so edits also affect particles that are
        /// already alive in the preview, instead of only newly emitted particles.
        /// </summary>
        public float AppliedSpeed { get; set; }
        public float AppliedLifespan { get; set; }
    }

    // ── Process ──

    /// <summary>
    /// Cache of the latest mask input, used by SimulateFrame for emission sampling.
    /// </summary>
    internal PixelBuffer? LastMaskInput { get; private set; }
    internal PixelBuffer? LastPositionXInput { get; private set; }
    internal PixelBuffer? LastPositionYInput { get; private set; }
    internal PixelBuffer? LastEmissionRateInput { get; private set; }
    internal PixelBuffer? LastSpeedInput { get; private set; }
    internal PixelBuffer? LastSizeInput { get; private set; }

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
        LastPositionXInput = inputs.Length > 1 ? inputs[1] : null;
        LastPositionYInput = inputs.Length > 2 ? inputs[2] : null;
        LastEmissionRateInput = inputs.Length > 3 ? inputs[3] : null;
        LastSpeedInput = inputs.Length > 4 ? inputs[4] : null;
        LastSizeInput = inputs.Length > 5 ? inputs[5] : null;

        // Evaluator results are caller-owned and disposed after each pass, so particle
        // placeholders must never be shared across evaluations.
        return PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Gets or creates the persistent emitter state for this frame.
    /// This is called by the evaluator via IPersistentStateNode.
    /// </summary>
    public EmitterState GetOrCreateState(
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var maxParticles = Math.Clamp(GraphNodeBase.GetInt(parameters, "maxParticles", 500), 1, 10000);
        if (PersistentState is EmitterState es && es.Initialized && es.Buffer.Capacity == maxParticles)
            return es;

        if (PersistentState is EmitterState previousState)
            previousState.Buffer.Dispose();

        var buffer = new ParticleBuffer(maxParticles);
        var settings = ResolveSettings(parameters);

        var emitter = new ParticleEmitter(context.Seed)
        {
            EmissionRate = settings.EmissionRate,
            BurstCount = settings.BurstCount,
            LifeMin = settings.Lifespan * (1f - settings.LifeRandom * 0.5f),
            LifeMax = settings.Lifespan * (1f + settings.LifeRandom * 0.5f),
            SpeedMin = settings.Speed * (1f - settings.SpeedRandom * 0.5f),
            SpeedMax = settings.Speed * (1f + settings.SpeedRandom * 0.5f),
            Angle = settings.Angle,
            Spread = settings.Spread,
            SizeMin = settings.StartSize * (1f - settings.SizeRandom * 0.5f),
            SizeMax = settings.StartSize * (1f + settings.SizeRandom * 0.5f),
            EndSizeMultiplier = settings.EndSize / Math.Max(settings.StartSize, 0.001f),
            OneShot = settings.OneShot,
            RotationRandom = settings.RotationRandom,
            AngularVelocityRandom = settings.AngularVelocityRandom,
            PositionX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
            PositionY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
            Shape = ParseShape(settings.Shape),
            ShapeWidth = settings.ShapeWidth,
            ShapeHeight = settings.ShapeHeight
        };

        ApplyColors(emitter, settings.StartColor, settings.EndColor);

        var simulator = new ParticleSimulator
        {
            GravityX = settings.GravityX,
            GravityY = settings.GravityY,
            Damping = settings.Damping,
            TilingMode = settings.Tiling,
            WindX = settings.WindX,
            WindY = settings.WindY
        };

        // Emit the configured initial burst so the first preview frame is useful.
        emitter.BurstEmit(buffer);
        emitter.OneShot = settings.OneShot;
        emitter.OneShotCompleted = emitter.OneShot;

        if (GraphNodeBase.GetBool(parameters, "prewarm", true))
        {
            var seconds = Math.Clamp(GraphNodeBase.GetFloat(parameters, "prewarmSeconds", 0.35f), 0f, 5f);
            var steps = Math.Min(300, (int)MathF.Ceiling(seconds * 60f));
            for (var i = 0; i < steps; i++)
            {
                emitter.Emit(1f / 60f, buffer);
                simulator.Update(1f / 60f, buffer);
            }
        }

        es = new EmitterState(emitter, simulator, buffer, true)
        {
            AppliedSpeed = settings.Speed,
            AppliedLifespan = settings.Lifespan
        };
        PersistentState = es;
        return es;
    }

    /// <summary>
    /// Processes one frame of particle simulation.
    /// </summary>
    public void SimulateFrame(IReadOnlyDictionary<string, object> parameters, PixelGraphContext context,
        float? positionX = null, float? positionY = null, float? emissionRate = null,
        float? speedInput = null, float? sizeInput = null)
    {
        if (PersistentState is not EmitterState es || !es.Initialized)
            return;

        var deltaTime = context.DeltaTime;
        var settings = ResolveSettings(parameters);

        // Update emitter parameters (they may change between frames)
        var emitter = es.Emitter;
        emitter.EmissionRate = Math.Max(0f, emissionRate ?? settings.EmissionRate);
        var speed = Math.Max(0f, speedInput ?? settings.Speed);
        ApplyLiveParameterChanges(es, speed, settings.Lifespan);
        var speedRandom = settings.SpeedRandom;
        emitter.SpeedMin = speed * (1f - speedRandom * 0.5f);
        emitter.SpeedMax = speed * (1f + speedRandom * 0.5f);
        emitter.Angle = settings.Angle;
        emitter.Spread = settings.Spread;
        emitter.PositionX = Math.Clamp(positionX ?? GraphNodeBase.GetFloat(parameters, "positionX", 0.5f), 0f, 1f);
        emitter.PositionY = Math.Clamp(positionY ?? GraphNodeBase.GetFloat(parameters, "positionY", 0.5f), 0f, 1f);
        emitter.BurstCount = settings.BurstCount;
        emitter.LifeMin = Math.Max(0.01f, settings.Lifespan * (1f - settings.LifeRandom * 0.5f));
        emitter.LifeMax = Math.Max(emitter.LifeMin, settings.Lifespan * (1f + settings.LifeRandom * 0.5f));
        var startSize = Math.Max(0.001f, sizeInput ?? settings.StartSize);
        emitter.SizeMin = startSize * (1f - settings.SizeRandom * 0.5f);
        emitter.SizeMax = startSize * (1f + settings.SizeRandom * 0.5f);
        emitter.EndSizeMultiplier = Math.Max(0f, settings.EndSize) / startSize;
        emitter.RotationRandom = settings.RotationRandom;
        emitter.AngularVelocityRandom = settings.AngularVelocityRandom;
        emitter.ShapeWidth = settings.ShapeWidth;
        emitter.ShapeHeight = settings.ShapeHeight;
        emitter.Shape = ParseShape(settings.Shape);
        var oneShot = settings.OneShot;
        emitter.OneShot = oneShot;
        if (!oneShot)
            emitter.OneShotCompleted = false;

        ApplyColors(emitter, settings.StartColor, settings.EndColor);

        // Update simulator parameters
        var sim = es.Simulator;
        sim.GravityX = settings.GravityX;
        sim.GravityY = settings.GravityY;
        sim.Damping = settings.Damping;
        sim.TilingMode = settings.Tiling;
        sim.WindX = settings.WindX;
        sim.WindY = settings.WindY;

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

    private static void ApplyLiveParameterChanges(EmitterState state, float speed, float lifespan)
    {
        var speedRatio = state.AppliedSpeed > 0.0001f ? speed / state.AppliedSpeed : 1f;
        var lifespanRatio = state.AppliedLifespan > 0.0001f ? lifespan / state.AppliedLifespan : 1f;
        var updateSpeed = MathF.Abs(speed - state.AppliedSpeed) > 0.0001f;
        var updateLifespan = MathF.Abs(lifespan - state.AppliedLifespan) > 0.0001f;

        if (updateSpeed || updateLifespan)
        {
            var particles = state.Buffer.AsSpan();
            for (var i = 0; i < state.Buffer.ActiveCount; i++)
            {
                ref var particle = ref particles[i];
                if (!particle.Active || particle.IsTrailGhost)
                    continue;

                if (updateSpeed)
                {
                    particle.VX *= speedRatio;
                    particle.VY *= speedRatio;
                }

                if (updateLifespan)
                    particle.MaxLife = Math.Max(0.01f, particle.MaxLife * lifespanRatio);
            }
        }

        state.AppliedSpeed = speed;
        state.AppliedLifespan = lifespan;
    }

    private static ResolvedEmitterSettings ResolveSettings(IReadOnlyDictionary<string, object> parameters)
    {
        var preset = GraphNodeBase.GetChoice(parameters, "preset", "manual");
        ResolvedEmitterSettings settings = preset switch
        {
            "fire" => new(90, 18, 0.75f, 0.35f, 0.28f, 0.5f, 90, 28, 0.065f, 0.012f, 0.25f,
                Color.FromArgb(255, 255, 202, 64), Color.FromArgb(0, 210, 45, 8),
                0, -0.08f, 0.94f, 0, 0, false, false, "circle", 0.12f, 0.08f, 1.2f, 1.8f),
            "smoke" => new(24, 8, 2.8f, 0.4f, 0.10f, 0.55f, 90, 38, 0.10f, 0.24f, 0.35f,
                Color.FromArgb(190, 150, 154, 164), Color.FromArgb(0, 55, 60, 72),
                0, -0.025f, 0.92f, 0.018f, 0, false, false, "circle", 0.16f, 0.10f, 2.2f, 0.45f),
            "rain" => new(170, 0, 1.15f, 0.15f, 0.95f, 0.12f, 270, 5, 0.018f, 0.012f, 0.18f,
                Color.FromArgb(230, 155, 205, 255), Color.FromArgb(40, 75, 130, 210),
                0, 0.75f, 0.995f, 0.04f, 0, false, false, "line", 1f, 0.02f, 0.2f, 0),
            "snow" => new(48, 6, 3.2f, 0.45f, 0.13f, 0.5f, 265, 32, 0.034f, 0.02f, 0.35f,
                Color.FromArgb(245, 245, 250, 255), Color.FromArgb(50, 190, 215, 245),
                0, 0.075f, 0.98f, 0.045f, 0, false, false, "line", 1f, 0.02f, 3.14f, 1.5f),
            "sparks" => new(54, 28, 0.65f, 0.4f, 0.62f, 0.65f, 90, 145, 0.024f, 0.002f, 0.3f,
                Color.FromArgb(255, 255, 240, 145), Color.FromArgb(0, 255, 70, 8),
                0, 0.55f, 0.985f, 0, 0, false, false, "point", 0, 0, 3.14f, 1.2f),
            "magic" => new(42, 14, 1.65f, 0.45f, 0.18f, 0.65f, 90, 360, 0.042f, 0.006f, 0.4f,
                Color.FromArgb(255, 105, 235, 255), Color.FromArgb(0, 170, 60, 255),
                0, 0, 0.975f, 0, 0, false, true, "circle", 0.36f, 0.36f, 6.28f, 2.2f),
            "dust" => new(28, 12, 1.8f, 0.5f, 0.09f, 0.7f, 92, 105, 0.042f, 0.075f, 0.5f,
                Color.FromArgb(180, 194, 157, 105), Color.FromArgb(0, 96, 72, 48),
                0, 0.03f, 0.90f, 0.035f, 0, false, false, "rectangle", 0.46f, 0.10f, 6.28f, 0.6f),
            "explosion" => new(0, 92, 0.82f, 0.35f, 0.78f, 0.6f, 0, 360, 0.055f, 0.004f, 0.4f,
                Color.FromArgb(255, 255, 235, 118), Color.FromArgb(0, 180, 25, 5),
                0, 0.45f, 0.96f, 0, 0, true, false, "point", 0, 0, 6.28f, 3.6f),
            "bubbles" => new(22, 6, 2.7f, 0.45f, 0.13f, 0.45f, 90, 26, 0.038f, 0.075f, 0.5f,
                Color.FromArgb(190, 150, 225, 255), Color.FromArgb(0, 85, 150, 240),
                0, -0.035f, 0.96f, 0.015f, 0, false, false, "line", 0.45f, 0.02f, 0, 0),
            "leaves" => new(18, 8, 3.6f, 0.5f, 0.15f, 0.6f, 275, 58, 0.055f, 0.04f, 0.35f,
                Color.FromArgb(255, 105, 154, 62), Color.FromArgb(0, 154, 82, 35),
                0, 0.10f, 0.97f, 0.075f, 0, false, false, "line", 1f, 0.02f, 6.28f, 3.2f),
            _ => new(
                GraphNodeBase.GetFloat(parameters, "emissionRate", 50),
                GraphNodeBase.GetInt(parameters, "burstCount", 20),
                GraphNodeBase.GetFloat(parameters, "lifespan", 2f),
                GraphNodeBase.GetFloat(parameters, "lifeRandom", 0.15f),
                GraphNodeBase.GetFloat(parameters, "speed", 0.3f),
                GraphNodeBase.GetFloat(parameters, "speedRandom", 0.5f),
                GraphNodeBase.GetFloat(parameters, "angle", 270f),
                GraphNodeBase.GetFloat(parameters, "spread", 30f),
                GraphNodeBase.GetFloat(parameters, "startSize", 0.05f),
                GraphNodeBase.GetFloat(parameters, "endSize", 0.01f),
                GraphNodeBase.GetFloat(parameters, "sizeRandom", 0.15f),
                GraphNodeBase.GetColor(parameters, "startColor", Colors.White),
                GraphNodeBase.GetColor(parameters, "endColor", Color.FromArgb(0, 255, 255, 255)),
                GraphNodeBase.GetFloat(parameters, "gravityX", 0f),
                GraphNodeBase.GetFloat(parameters, "gravity", 0.2f),
                GraphNodeBase.GetFloat(parameters, "damping", 0.98f),
                GraphNodeBase.GetFloat(parameters, "wind", 0f),
                GraphNodeBase.GetFloat(parameters, "windY", 0f),
                GraphNodeBase.GetBool(parameters, "oneShot", false),
                GraphNodeBase.GetBool(parameters, "tiling", true),
                GraphNodeBase.GetChoice(parameters, "emissionShape", "point"),
                GraphNodeBase.GetFloat(parameters, "shapeWidth", 0.1f),
                GraphNodeBase.GetFloat(parameters, "shapeHeight", 0.1f),
                GraphNodeBase.GetFloat(parameters, "rotationRandom", 3.14f),
                GraphNodeBase.GetFloat(parameters, "angularVelocityRandom", 0f))
        };

        if (preset == "manual")
            return settings;

        var intensity = Math.Clamp(GraphNodeBase.GetFloat(parameters, "presetIntensity", 1f), 0.1f, 4f);
        var scale = Math.Clamp(GraphNodeBase.GetFloat(parameters, "presetScale", 1f), 0.25f, 4f);
        var emissionRateScale = Ratio(parameters, "emissionRate", 50f, 0f, 10f);
        var burstScale = Ratio(parameters, "burstCount", 20f, 0f, 10f);
        var lifespanScale = Ratio(parameters, "lifespan", 2f, 0.05f, 10f);
        var lifeRandomScale = Ratio(parameters, "lifeRandom", 0.15f, 0f, 6.67f);
        var speedScale = Ratio(parameters, "speed", 0.3f, 0f, 10f);
        var speedRandomScale = Ratio(parameters, "speedRandom", 0.5f, 0f, 2f);
        var spreadScale = Ratio(parameters, "spread", 30f, 0f, 6f);
        var startSizeScale = Ratio(parameters, "startSize", 0.05f, 0.1f, 10f);
        var endSizeScale = Ratio(parameters, "endSize", 0.01f, 0f, 50f);
        var sizeRandomScale = Ratio(parameters, "sizeRandom", 0.15f, 0f, 6.67f);
        var shapeWidthScale = Ratio(parameters, "shapeWidth", 0.1f, 0f, 10f);
        var shapeHeightScale = Ratio(parameters, "shapeHeight", 0.1f, 0f, 10f);
        var rotationScale = Ratio(parameters, "rotationRandom", 3.14f, 0f, 2f);
        var requestedShape = GraphNodeBase.GetChoice(parameters, "emissionShape", "point");
        var requestedStartColor = GraphNodeBase.GetColor(parameters, "startColor", Colors.White);
        var requestedEndColor = GraphNodeBase.GetColor(parameters, "endColor", Color.FromArgb(0, 255, 255, 255));
        return settings with
        {
            EmissionRate = settings.EmissionRate * intensity * emissionRateScale,
            BurstCount = Math.Max(0, (int)MathF.Round(settings.BurstCount * intensity * burstScale)),
            Lifespan = Math.Max(0.01f, settings.Lifespan * lifespanScale),
            LifeRandom = Math.Clamp(settings.LifeRandom * lifeRandomScale, 0f, 1f),
            Speed = Math.Max(0f, settings.Speed * speedScale),
            SpeedRandom = Math.Clamp(settings.SpeedRandom * speedRandomScale, 0f, 1f),
            Angle = NormalizeAngle(settings.Angle + GraphNodeBase.GetFloat(parameters, "angle", 270f) - 270f),
            Spread = Math.Clamp(settings.Spread * spreadScale, 0f, 360f),
            StartSize = Math.Max(0.001f, settings.StartSize * scale * startSizeScale),
            EndSize = Math.Max(0f, settings.EndSize * scale * endSizeScale),
            SizeRandom = Math.Clamp(settings.SizeRandom * sizeRandomScale, 0f, 1f),
            StartColor = requestedStartColor == Colors.White ? settings.StartColor : requestedStartColor,
            EndColor = requestedEndColor == Color.FromArgb(0, 255, 255, 255) ? settings.EndColor : requestedEndColor,
            GravityX = settings.GravityX + GraphNodeBase.GetFloat(parameters, "gravityX", 0f),
            GravityY = settings.GravityY + GraphNodeBase.GetFloat(parameters, "gravity", 0.2f) - 0.2f,
            Damping = Math.Clamp(settings.Damping + GraphNodeBase.GetFloat(parameters, "damping", 0.98f) - 0.98f, 0f, 1f),
            WindX = settings.WindX + GraphNodeBase.GetFloat(parameters, "wind", 0f),
            WindY = settings.WindY + GraphNodeBase.GetFloat(parameters, "windY", 0f),
            OneShot = settings.OneShot || GraphNodeBase.GetBool(parameters, "oneShot", false),
            Tiling = settings.Tiling && GraphNodeBase.GetBool(parameters, "tiling", true),
            Shape = requestedShape == "point" ? settings.Shape : requestedShape,
            ShapeWidth = Math.Clamp(settings.ShapeWidth * scale * shapeWidthScale, 0f, 1f),
            ShapeHeight = Math.Clamp(settings.ShapeHeight * scale * shapeHeightScale, 0f, 1f),
            RotationRandom = Math.Clamp(settings.RotationRandom * rotationScale, 0f, 6.28f),
            AngularVelocityRandom = Math.Clamp(settings.AngularVelocityRandom +
                GraphNodeBase.GetFloat(parameters, "angularVelocityRandom", 0f), 0f, 10f)
        };
    }

    private static float Ratio(IReadOnlyDictionary<string, object> parameters, string name,
        float baseline, float minimum, float maximum)
    {
        if (baseline <= 0f)
            return 1f;
        return Math.Clamp(GraphNodeBase.GetFloat(parameters, name, baseline) / baseline, minimum, maximum);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    private static EmissionShape ParseShape(string shape) => shape switch
    {
        "line" => EmissionShape.Line,
        "rectangle" => EmissionShape.Rectangle,
        "circle" => EmissionShape.Circle,
        "ring" => EmissionShape.Ring,
        _ => EmissionShape.Point
    };

    private static void ApplyColors(ParticleEmitter emitter, Color startColor, Color endColor)
    {
        emitter.StartR = startColor.R / 255f;
        emitter.StartG = startColor.G / 255f;
        emitter.StartB = startColor.B / 255f;
        emitter.StartA = startColor.A / 255f;
        emitter.EndR = endColor.R / 255f;
        emitter.EndG = endColor.G / 255f;
        emitter.EndB = endColor.B / 255f;
        emitter.EndA = endColor.A / 255f;
    }

    private readonly record struct ResolvedEmitterSettings(
        float EmissionRate, int BurstCount, float Lifespan, float LifeRandom,
        float Speed, float SpeedRandom, float Angle, float Spread,
        float StartSize, float EndSize, float SizeRandom,
        Color StartColor, Color EndColor,
        float GravityX, float GravityY, float Damping, float WindX, float WindY,
        bool OneShot, bool Tiling, string Shape, float ShapeWidth, float ShapeHeight,
        float RotationRandom, float AngularVelocityRandom);

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
