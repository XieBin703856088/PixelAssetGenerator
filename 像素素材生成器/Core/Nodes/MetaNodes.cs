using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using PixelAssetGenerator.Core.Animation.Nodes;
using PixelAssetGenerator.Core.Particles;
using PixelAssetGenerator.Core.Particles.Nodes;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>Explicit terminal and metadata anchor for an independent animation workflow.</summary>
public sealed class AnimationWorkflowOutputNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs = { new("动画图像", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Text("workflowName", "动画工作流", "工作流名称"),
        NodeParameterDefinition.Number("duration", 1.0, 0.05, 60, 0.05, "时长（秒）"),
        NodeParameterDefinition.Integer("frameRate", 12, 1, 60, 1, "建议帧率"),
        NodeParameterDefinition.Choice("loopMode", "loop",
            ["loop", "pingPong", "once"], ["循环", "往返", "单次"], "循环模式")
    };

    public override string TypeName => "AnimationWorkflowOutput";
    public override string Category => "Meta";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        return source?.Clone() ?? PixelBuffer.CreateSolid(
            context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);
    }
}

/// <summary>Combines reusable motion and visible sprite effects without losing pixel alignment.</summary>
public sealed class SpriteMotionMetaNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("精灵图像", GraphPortType.Image, "image", true),
        new("动作强度", GraphPortType.Float, "strength")
    };
    private static readonly GraphNodePort[] Outputs = { new("动画图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("motion", "idle",
            ["idle", "float", "hop", "hit", "shake", "orbit", "pendulum"],
            ["呼吸待机", "漂浮", "跳跃", "受击", "震动", "环绕", "摆动"], "动作"),
        NodeParameterDefinition.Choice("effect", "none",
            ["none", "pulseGlow", "flash", "shimmer", "ghost", "outlinePulse"],
            ["无", "呼吸发光", "闪白", "流光", "幽灵闪烁", "描边脉冲"], "附加特效"),
        NodeParameterDefinition.Number("motionStrength", 0.10, 0, 1, 0.005, "动作幅度"),
        NodeParameterDefinition.Number("effectStrength", 0.70, 0, 2, 0.01, "特效强度"),
        NodeParameterDefinition.Number("speed", 1.0, 0.05, 8, 0.05, "速度"),
        NodeParameterDefinition.Number("phase", 0.0, 0, 1, 0.01, "相位"),
        NodeParameterDefinition.Color("effectColor", Color.FromRgb(118, 224, 255), "特效颜色"),
        NodeParameterDefinition.Boolean("pixelSnap", true, "像素吸附"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "SpriteMotionMeta";
    public override string Category => "Meta";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);

        var connectedStrength = inputs.Length > 1 && inputs[1] != null ? inputs[1]!.GetPixel(0, 0).R : 1f;
        var motionParameters = new Dictionary<string, object>
        {
            ["preset"] = GetChoice(parameters, "motion", "idle"),
            ["strength"] = Math.Clamp(GetFloat(parameters, "motionStrength", 0.1f) * connectedStrength, 0f, 1f),
            ["speed"] = GetFloat(parameters, "speed", 1f),
            ["phase"] = GetFloat(parameters, "phase", 0f),
            ["pixelSnap"] = GetBool(parameters, "pixelSnap", true),
            ["seed"] = GetInt(parameters, "seed", context.Seed)
        };

        var motion = new MotionPresetNode();
        var channels = motion.ProcessMulti([], motionParameters, context);
        try
        {
            var transform = new AnimatedTransformNode();
            using var transformed = transform.Process(
                [source, channels[0], channels[1], channels[2], channels[3]],
                new Dictionary<string, object>(), context);
            var effect = GetChoice(parameters, "effect", "none");
            if (effect == "none") return transformed.Clone();

            var animator = new SpriteEffectAnimatorNode();
            return animator.Process([transformed, null], new Dictionary<string, object>
            {
                ["effect"] = effect,
                ["speed"] = GetFloat(parameters, "speed", 1f),
                ["strength"] = GetFloat(parameters, "effectStrength", 0.7f),
                ["phase"] = GetFloat(parameters, "phase", 0f),
                ["effectColor"] = GetColor(parameters, "effectColor", Color.FromRgb(118, 224, 255)),
                ["pixelStep"] = 1,
                ["pingPong"] = true,
                ["seed"] = GetInt(parameters, "seed", context.Seed)
            }, context);
        }
        finally
        {
            foreach (var channel in channels) channel.Dispose();
        }
    }
}

/// <summary>A complete, stateful particle effect that remains usable as one graph node.</summary>
public sealed class ParticleEffectMetaNode : GraphNodeBase, IPersistentStateNode
{
    private static readonly GraphNodePort[] Inputs = { new("背景", GraphPortType.Image, "background") };
    private static readonly GraphNodePort[] Outputs = { new("效果图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("effect", "fire",
            ["fire", "smoke", "sparks", "magic", "rain", "snow", "bubbles", "leaves", "dust", "embers", "portal", "poison", "waterSplash", "impact"],
            ["火焰", "烟雾", "火花", "魔法能量", "雨水", "雪花", "气泡", "落叶", "扬尘", "余烬", "传送门", "毒雾", "水花", "撞击碎屑"], "粒子效果"),
        NodeParameterDefinition.Number("intensity", 1.0, 0.1, 4, 0.05, "强度"),
        NodeParameterDefinition.Number("scale", 1.0, 0.25, 4, 0.05, "尺寸"),
        NodeParameterDefinition.Number("speedMultiplier", 1.0, 0.05, 4, 0.05, "速度倍率"),
        NodeParameterDefinition.Number("lifespanMultiplier", 1.0, 0.1, 5, 0.05, "寿命倍率"),
        NodeParameterDefinition.Number("gravityMultiplier", 1.0, -3, 3, 0.05, "重力倍率"),
        NodeParameterDefinition.Number("positionX", 0.5, 0, 1, 0.01, "位置 X"),
        NodeParameterDefinition.Number("positionY", 0.72, 0, 1, 0.01, "位置 Y"),
        NodeParameterDefinition.Number("wind", 0.0, -1, 1, 0.01, "风力"),
        NodeParameterDefinition.Boolean("seamless", true, "平铺循环"),
        NodeParameterDefinition.Boolean("prewarm", true, "预热"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "ParticleEffectMeta";
    public override string Category => "Meta";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.TimeDependent;
    public string PersistentStateKey => "ParticleEffectMeta";
    public object? PersistentState { get; set; }

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var effect = GetChoice(parameters, "effect", "fire");
        var intensity = Math.Clamp(GetFloat(parameters, "intensity", 1f), 0.1f, 4f);
        var scale = Math.Clamp(GetFloat(parameters, "scale", 1f), 0.25f, 4f);
        var seed = GetInt(parameters, "seed", context.Seed);
        var state = PersistentState as MetaParticleState;
        if (state == null || state.Effect != effect || state.Seed != seed)
        {
            state?.Dispose();
            state = CreateParticleState(effect, intensity, scale, seed,
                GetBool(parameters, "seamless", true));
            PersistentState = state;
            if (GetBool(parameters, "prewarm", true))
                for (var i = 0; i < 18; i++) Step(state, effect, 1f / 60f, context.GlobalTime + i / 60f);
        }

        ApplyDynamicSettings(state, effect, intensity, scale, parameters);
        Step(state, effect, Math.Clamp(context.DeltaTime, 1f / 120f, 1f / 12f), context.GlobalTime);
        var output = inputs.Length > 0 && inputs[0] != null
            ? inputs[0]!.Clone()
            : PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);
        state.Renderer.Render(state.Buffer.ActiveSpan(), output, state.Buffer.ActiveCount);
        return output;
    }

    private static MetaParticleState CreateParticleState(string effect, float intensity,
        float scale, int seed, bool seamless)
    {
        var buffer = new ParticleBuffer(1024);
        var emitter = new ParticleEmitter(seed);
        var simulator = new ParticleSimulator { TilingMode = seamless };
        var renderer = new ParticleRenderer { PixelSnap = true, AlphaSteps = 4 };
        var state = new MetaParticleState(effect, seed, emitter, simulator, buffer, renderer);
        ConfigurePreset(state, effect, intensity, scale);
        emitter.BurstEmit(buffer);
        return state;
    }

    private static void ApplyDynamicSettings(MetaParticleState state, string effect, float intensity,
        float scale, IReadOnlyDictionary<string, object> parameters)
    {
        ConfigurePreset(state, effect, intensity, scale);
        var speedMultiplier = Math.Clamp(GetFloat(parameters, "speedMultiplier", 1f), 0.05f, 4f);
        var lifespanMultiplier = Math.Clamp(GetFloat(parameters, "lifespanMultiplier", 1f), 0.1f, 5f);
        ApplyLiveMultipliers(state, speedMultiplier, lifespanMultiplier);
        state.Emitter.SpeedMin *= speedMultiplier;
        state.Emitter.SpeedMax *= speedMultiplier;
        state.Emitter.LifeMin *= lifespanMultiplier;
        state.Emitter.LifeMax *= lifespanMultiplier;
        var gravityMultiplier = Math.Clamp(GetFloat(parameters, "gravityMultiplier", 1f), -3f, 3f);
        state.Simulator.GravityX *= gravityMultiplier;
        state.Simulator.GravityY *= gravityMultiplier;
        state.Emitter.PositionX = Math.Clamp(GetFloat(parameters, "positionX", 0.5f), 0f, 1f);
        state.Emitter.PositionY = Math.Clamp(GetFloat(parameters, "positionY", 0.72f), 0f, 1f);
        state.Simulator.WindX = GetFloat(parameters, "wind", 0f);
        state.Simulator.TilingMode = GetBool(parameters, "seamless", true);
    }

    private static void ApplyLiveMultipliers(MetaParticleState state, float speedMultiplier,
        float lifespanMultiplier)
    {
        var speedChanged = MathF.Abs(speedMultiplier - state.AppliedSpeedMultiplier) > 0.0001f;
        var lifespanChanged = MathF.Abs(lifespanMultiplier - state.AppliedLifespanMultiplier) > 0.0001f;
        if (speedChanged || lifespanChanged)
        {
            var speedRatio = speedMultiplier / Math.Max(0.0001f, state.AppliedSpeedMultiplier);
            var lifespanRatio = lifespanMultiplier / Math.Max(0.0001f, state.AppliedLifespanMultiplier);
            var particles = state.Buffer.AsSpan();
            for (var i = 0; i < state.Buffer.ActiveCount; i++)
            {
                ref var particle = ref particles[i];
                if (!particle.Active || particle.IsTrailGhost)
                    continue;
                if (speedChanged)
                {
                    particle.VX *= speedRatio;
                    particle.VY *= speedRatio;
                }
                if (lifespanChanged)
                    particle.MaxLife = Math.Max(0.01f, particle.MaxLife * lifespanRatio);
            }
        }

        state.AppliedSpeedMultiplier = speedMultiplier;
        state.AppliedLifespanMultiplier = lifespanMultiplier;
    }

    private static void ConfigurePreset(MetaParticleState state, string effect, float intensity, float scale)
    {
        var e = state.Emitter;
        var s = state.Simulator;
        e.EmissionRate = 42f * intensity; e.BurstCount = Math.Max(3, (int)(12 * intensity));
        e.LifeMin = 0.7f; e.LifeMax = 1.5f; e.SpeedMin = 0.08f; e.SpeedMax = 0.28f;
        e.Angle = 90f; e.Spread = 35f; e.SizeMin = 0.025f * scale; e.SizeMax = 0.06f * scale;
        e.EndSizeMultiplier = 0.2f; e.Shape = EmissionShape.Circle; e.ShapeWidth = 0.16f * scale; e.ShapeHeight = 0.10f * scale;
        e.OneShot = false; e.OneShotCompleted = false; s.GravityX = 0f; s.GravityY = -0.03f; s.Damping = 0.95f;
        state.Renderer.BlendMode = ParticleBlendMode.Alpha; state.Renderer.TextureType = ParticleTextureType.PixelCircle;
        SetColors(e, (1f, 0.82f, 0.25f, 1f), (0.75f, 0.08f, 0.01f, 0f));

        switch (effect)
        {
            case "smoke":
                e.EmissionRate = 20 * intensity; e.LifeMin = 1.7f; e.LifeMax = 3.2f; e.SpeedMin = 0.035f; e.SpeedMax = 0.11f;
                e.SizeMin = 0.07f * scale; e.SizeMax = 0.13f * scale; e.EndSizeMultiplier = 2.1f; e.Spread = 48f;
                s.GravityY = -0.018f; s.Damping = 0.91f; state.Renderer.TextureType = ParticleTextureType.SmokePuff;
                SetColors(e, (0.58f, 0.60f, 0.65f, 0.72f), (0.18f, 0.20f, 0.25f, 0f)); break;
            case "sparks":
                e.EmissionRate = 34 * intensity; e.LifeMin = 0.35f; e.LifeMax = 0.82f; e.SpeedMin = 0.35f; e.SpeedMax = 0.82f;
                e.SizeMin = 0.012f * scale; e.SizeMax = 0.032f * scale; e.EndSizeMultiplier = 0.05f; e.Spread = 145f;
                s.GravityY = 0.48f; state.Renderer.TextureType = ParticleTextureType.Spark; state.Renderer.BlendMode = ParticleBlendMode.Additive; break;
            case "magic":
                e.EmissionRate = 30 * intensity; e.LifeMin = 1.0f; e.LifeMax = 2.0f; e.SpeedMin = 0.05f; e.SpeedMax = 0.18f;
                e.Shape = EmissionShape.Ring; e.ShapeWidth = e.ShapeHeight = 0.32f * scale; e.Spread = 360f; s.GravityY = 0f;
                state.Renderer.TextureType = ParticleTextureType.Rune; state.Renderer.BlendMode = ParticleBlendMode.Additive;
                SetColors(e, (0.38f, 0.92f, 1f, 1f), (0.72f, 0.18f, 1f, 0f)); break;
            case "rain":
                e.EmissionRate = 130 * intensity; e.LifeMin = 0.8f; e.LifeMax = 1.3f; e.SpeedMin = 0.7f; e.SpeedMax = 1.05f;
                e.Angle = 270f; e.Spread = 4f; e.Shape = EmissionShape.Line; e.ShapeWidth = 1f; e.SizeMin = e.SizeMax = 0.018f * scale;
                s.GravityY = 0.45f; state.Renderer.TextureType = ParticleTextureType.RainDrop;
                SetColors(e, (0.55f, 0.78f, 1f, 0.9f), (0.24f, 0.45f, 0.82f, 0.12f)); break;
            case "snow":
                e.EmissionRate = 38 * intensity; e.LifeMin = 2.2f; e.LifeMax = 3.8f; e.SpeedMin = 0.06f; e.SpeedMax = 0.14f;
                e.Angle = 270f; e.Spread = 28f; e.Shape = EmissionShape.Line; e.ShapeWidth = 1f; s.GravityY = 0.055f;
                state.Renderer.TextureType = ParticleTextureType.Snowflake; SetColors(e, (0.94f, 0.98f, 1f, 1f), (0.65f, 0.82f, 1f, 0f)); break;
            case "bubbles":
                e.EmissionRate = 18 * intensity; e.LifeMin = 1.8f; e.LifeMax = 3.2f; e.SpeedMin = 0.045f; e.SpeedMax = 0.13f;
                e.Shape = EmissionShape.Line; e.ShapeWidth = 0.5f; s.GravityY = -0.04f; state.Renderer.TextureType = ParticleTextureType.Bubble;
                SetColors(e, (0.48f, 0.86f, 1f, 0.82f), (0.28f, 0.58f, 0.95f, 0f)); break;
            case "leaves":
                e.EmissionRate = 14 * intensity; e.LifeMin = 2.4f; e.LifeMax = 4.2f; e.SpeedMin = 0.08f; e.SpeedMax = 0.18f;
                e.Angle = 275f; e.Spread = 48f; e.Shape = EmissionShape.Line; e.ShapeWidth = 1f; s.GravityY = 0.075f;
                state.Renderer.TextureType = ParticleTextureType.Leaf; SetColors(e, (0.32f, 0.62f, 0.20f, 1f), (0.72f, 0.28f, 0.08f, 0f)); break;
            case "dust":
                e.EmissionRate = 22 * intensity; e.LifeMin = 1.1f; e.LifeMax = 2.1f; e.SpeedMin = 0.04f; e.SpeedMax = 0.12f;
                e.Angle = 90f; e.Spread = 110f; e.Shape = EmissionShape.Rectangle; e.ShapeWidth = 0.46f; e.ShapeHeight = 0.08f;
                s.GravityY = 0.025f; state.Renderer.TextureType = ParticleTextureType.SmokePuff;
                SetColors(e, (0.72f, 0.55f, 0.34f, 0.68f), (0.30f, 0.22f, 0.16f, 0f)); break;
            case "embers":
                e.EmissionRate = 18 * intensity; e.LifeMin = 1.2f; e.LifeMax = 2.8f; e.SpeedMin = 0.04f; e.SpeedMax = 0.16f;
                e.Angle = 90f; e.Spread = 52f; e.SizeMin = 0.010f * scale; e.SizeMax = 0.026f * scale; e.EndSizeMultiplier = 0.05f;
                s.GravityY = -0.028f; state.Renderer.TextureType = ParticleTextureType.Spark; state.Renderer.BlendMode = ParticleBlendMode.Additive;
                SetColors(e, (1f, 0.72f, 0.12f, 1f), (0.82f, 0.10f, 0.01f, 0f)); break;
            case "portal":
                e.EmissionRate = 44 * intensity; e.LifeMin = 0.8f; e.LifeMax = 1.7f; e.SpeedMin = 0.02f; e.SpeedMax = 0.10f;
                e.Shape = EmissionShape.Ring; e.ShapeWidth = e.ShapeHeight = 0.42f * scale; e.Spread = 360f; s.GravityY = 0f;
                state.Renderer.TextureType = ParticleTextureType.Rune; state.Renderer.BlendMode = ParticleBlendMode.Additive;
                SetColors(e, (0.72f, 0.30f, 1f, 1f), (0.18f, 0.82f, 1f, 0f)); break;
            case "poison":
                e.EmissionRate = 17 * intensity; e.LifeMin = 1.5f; e.LifeMax = 3.0f; e.SpeedMin = 0.025f; e.SpeedMax = 0.09f;
                e.SizeMin = 0.06f * scale; e.SizeMax = 0.12f * scale; e.EndSizeMultiplier = 1.8f; e.Spread = 70f;
                s.GravityY = -0.012f; s.Damping = 0.90f; state.Renderer.TextureType = ParticleTextureType.SmokePuff;
                SetColors(e, (0.42f, 0.78f, 0.16f, 0.72f), (0.16f, 0.25f, 0.08f, 0f)); break;
            case "waterSplash":
                e.EmissionRate = 14 * intensity; e.BurstCount = Math.Max(8, (int)(24 * intensity)); e.LifeMin = 0.35f; e.LifeMax = 0.85f;
                e.SpeedMin = 0.28f; e.SpeedMax = 0.72f; e.Angle = 90f; e.Spread = 125f; e.Shape = EmissionShape.Line; e.ShapeWidth = 0.18f * scale;
                e.SizeMin = 0.012f * scale; e.SizeMax = 0.032f * scale; s.GravityY = 0.62f; state.Renderer.TextureType = ParticleTextureType.RainDrop;
                SetColors(e, (0.58f, 0.88f, 1f, 0.95f), (0.18f, 0.46f, 0.88f, 0f)); break;
            case "impact":
                e.EmissionRate = 10 * intensity; e.BurstCount = Math.Max(10, (int)(28 * intensity)); e.LifeMin = 0.25f; e.LifeMax = 0.72f;
                e.SpeedMin = 0.30f; e.SpeedMax = 0.88f; e.Angle = 90f; e.Spread = 165f; e.SizeMin = 0.012f * scale; e.SizeMax = 0.036f * scale;
                s.GravityY = 0.72f; state.Renderer.TextureType = ParticleTextureType.Square;
                SetColors(e, (0.86f, 0.72f, 0.48f, 1f), (0.30f, 0.22f, 0.15f, 0f)); break;
            default:
                state.Renderer.TextureType = ParticleTextureType.Flame; state.Renderer.BlendMode = ParticleBlendMode.Additive; break;
        }
    }

    private static void Step(MetaParticleState state, string effect, float deltaTime, float globalTime)
    {
        state.Simulator.ClearForces();
        if (effect is "smoke" or "dust" or "poison") state.Simulator.AddForce(new TurbulenceForce { Strength = 0.18f, Frequency = 2.3f, Seed = state.Seed });
        if (effect is "magic" or "portal") state.Simulator.AddForce(new VortexForce { CenterX = state.Emitter.PositionX, CenterY = state.Emitter.PositionY, Strength = effect == "portal" ? 0.62f : 0.34f, Radius = effect == "portal" ? 0.36f : 0.28f });
        state.Emitter.Emit(deltaTime, state.Buffer);
        state.Simulator.Update(deltaTime, state.Buffer);
        if (effect is "snow" or "leaves")
            new ParticleBehaviorNode().ApplyBehavior(state.Buffer,
                new Dictionary<string, object> { ["behavior"] = "zigzag", ["strength"] = 0.18f, ["frequency"] = 2.5f, ["seed"] = state.Seed },
                new PixelGraphContext { TileSize = 32, DeltaTime = deltaTime, GlobalTime = globalTime, Seed = state.Seed });
    }

    private static void SetColors(ParticleEmitter emitter,
        (float R, float G, float B, float A) start, (float R, float G, float B, float A) end)
    {
        emitter.StartR = start.R; emitter.StartG = start.G; emitter.StartB = start.B; emitter.StartA = start.A;
        emitter.EndR = end.R; emitter.EndG = end.G; emitter.EndB = end.B; emitter.EndA = end.A;
    }

    public sealed class MetaParticleState : IDisposable
    {
        public MetaParticleState(string effect, int seed, ParticleEmitter emitter, ParticleSimulator simulator,
            ParticleBuffer buffer, ParticleRenderer renderer)
        { Effect = effect; Seed = seed; Emitter = emitter; Simulator = simulator; Buffer = buffer; Renderer = renderer; }
        public string Effect { get; }
        public int Seed { get; }
        public ParticleEmitter Emitter { get; }
        public ParticleSimulator Simulator { get; }
        public ParticleBuffer Buffer { get; }
        public ParticleRenderer Renderer { get; }
        public float AppliedSpeedMultiplier { get; set; } = 1f;
        public float AppliedLifespanMultiplier { get; set; } = 1f;
        public void Dispose() => Buffer.Dispose();
    }
}

/// <summary>High-level material stack combining damage and weathering with an effect mask.</summary>
public sealed class MaterialEffectStackMetaNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs = { new("基础材质", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs =
    {
        new("材质图像", GraphPortType.Image, "image"), new("综合遮罩", GraphPortType.Mask, "mask")
    };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("preset", "mossyRuins",
            ["mossyRuins", "corrodedBrick", "dampDungeon", "frozenStone", "burntSurface", "dustyTemple"],
            ["苔藓遗迹", "腐蚀砖墙", "潮湿地牢", "霜冻石材", "火烧表面", "积尘神殿"], "效果栈预设"),
        NodeParameterDefinition.Number("effectAmount", 0.62, 0, 1, 0.01, "效果覆盖"),
        NodeParameterDefinition.Number("damageAmount", 0.38, 0, 1, 0.01, "破损程度"),
        NodeParameterDefinition.Number("edgeAffinity", 0.75, 0, 1, 0.01, "边缘附着"),
        NodeParameterDefinition.Boolean("seamless", true, "无缝平铺"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "MaterialEffectStackMeta";
    public override string Category => "Meta";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    { var outputs = ProcessMulti(inputs, parameters, context); outputs[1].Dispose(); return outputs[0]; }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
        {
            var size = context.GetEffectiveSize();
            return [PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f), PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 1f)];
        }
        var preset = GetChoice(parameters, "preset", "mossyRuins");
        var effect = preset switch
        { "corrodedBrick" => "corrosion", "dampDungeon" => "damp", "frozenStone" => "frost", "burntSurface" => "soot", "dustyTemple" => "dust", _ => "moss" };
        var damageAmount = Math.Clamp(GetFloat(parameters, "damageAmount", 0.38f), 0f, 1f);
        var seed = GetInt(parameters, "seed", context.Seed);
        PixelBuffer working = source.Clone();
        PixelBuffer? damageMask = null;
        try
        {
            if (damageAmount > 0.01f)
            {
                var crack = new SmartCrackDamageNode();
                var cracked = crack.ProcessMulti([working], new Dictionary<string, object>
                {
                    ["material"] = preset == "corrodedBrick" ? "brick" : "stone",
                    ["damage"] = damageAmount, ["crackWidth"] = 0.32f,
                    ["chips"] = damageAmount * 0.75f, ["depth"] = 0.70f,
                    ["networkScale"] = 6, ["breakThrough"] = false, ["seamless"] = GetBool(parameters, "seamless", true), ["seed"] = seed
                }, context);
                working.Dispose(); working = cracked[0]; damageMask = cracked[1];
            }
            var weather = new SmartMaterialWeatheringNode();
            var weathered = weather.ProcessMulti([working, null], new Dictionary<string, object>
            {
                ["effect"] = effect, ["amount"] = GetFloat(parameters, "effectAmount", 0.62f),
                ["clusterScale"] = 0.44f, ["edgeAffinity"] = GetFloat(parameters, "edgeAffinity", 0.75f),
                ["directionBias"] = effect is "damp" or "moss" ? 0.30f : 0f,
                ["colorStrength"] = 0.86f, ["preserveShading"] = 0.74f,
                ["palette"] = "natural", ["pixelClusters"] = true,
                ["seamless"] = GetBool(parameters, "seamless", true), ["seed"] = seed + 31
            }, context);
            var combinedMask = weathered[1];
            if (damageMask != null)
            {
                for (var y = 0; y < combinedMask.Height; y++)
                for (var x = 0; x < combinedMask.Width; x++)
                {
                    var weatherValue = combinedMask.GetPixel(x, y).R;
                    var damageValue = damageMask.GetPixel(x, y).R;
                    var value = MathF.Max(weatherValue, damageValue);
                    combinedMask.SetPixel(x, y, value, value, value, 1f);
                }
            }
            return weathered;
        }
        finally
        {
            working.Dispose(); damageMask?.Dispose();
        }
    }
}

/// <summary>Produces four deterministic, pixel-clean variations from one finished tile.</summary>
public sealed class TileVariationMetaNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs = { new("图块", GraphPortType.Image, "image", true) };
    private static readonly GraphNodePort[] Outputs =
    {
        new("变体 A", GraphPortType.Image, "variantA"), new("变体 B", GraphPortType.Image, "variantB"),
        new("变体 C", GraphPortType.Image, "variantC"), new("变体 D", GraphPortType.Image, "variantD")
    };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("style", "natural",
            ["natural", "dungeon", "organic", "metal"], ["自然", "地牢", "有机", "金属"], "变化风格"),
        NodeParameterDefinition.Number("variation", 0.22, 0, 1, 0.01, "变化幅度"),
        NodeParameterDefinition.Boolean("allowMirror", true, "允许镜像"),
        NodeParameterDefinition.Boolean("wrapOffset", true, "无缝偏移"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "TileVariationMeta";
    public override string Category => "Meta";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    { var outputs = ProcessMulti(inputs, parameters, context); for (var i = 1; i < outputs.Length; i++) outputs[i].Dispose(); return outputs[0]; }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
        {
            var size = context.GetEffectiveSize();
            return Enumerable.Range(0, 4)
                .Select(_ => PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f))
                .ToArray();
        }
        var amount = Math.Clamp(GetFloat(parameters, "variation", 0.22f), 0f, 1f);
        var seed = GetInt(parameters, "seed", context.Seed);
        var style = GetChoice(parameters, "style", "natural");
        return
        [
            BuildVariant(source, 0, amount, style, seed, parameters),
            BuildVariant(source, 1, amount, style, seed, parameters),
            BuildVariant(source, 2, amount, style, seed, parameters),
            BuildVariant(source, 3, amount, style, seed, parameters)
        ];
    }

    private static PixelBuffer BuildVariant(PixelBuffer source, int variant, float amount, string style,
        int seed, IReadOnlyDictionary<string, object> parameters)
    {
        var result = PixelBufferPool.Borrow(source.Width, source.Height);
        var mirror = GetBool(parameters, "allowMirror", true) && variant is 1 or 3;
        var wrap = GetBool(parameters, "wrapOffset", true);
        var offsetX = wrap ? (int)(HashToUnit(variant, seed, 5011) * source.Width) : 0;
        var offsetY = wrap ? (int)(HashToUnit(variant, seed, 5017) * source.Height) : 0;
        var hueShift = (HashToUnit(variant, seed, 5021) * 2f - 1f) * amount * 0.12f;
        var brightness = (HashToUnit(variant, seed, 5023) * 2f - 1f) * amount * 0.14f;
        var saturationScale = style switch { "dungeon" => 0.86f, "organic" => 1.10f, "metal" => 0.72f, _ => 1f };
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var sx = Mod((mirror ? source.Width - 1 - x : x) + offsetX, source.Width);
            var sy = Mod(y + offsetY, source.Height);
            var p = source.GetPixel(sx, sy);
            RgbToHsv(p.R, p.G, p.B, out var h, out var s, out var v);
            HsvToRgb(PositiveModulo(h + hueShift, 1f), Math.Clamp(s * saturationScale, 0f, 1f),
                Math.Clamp(v + brightness, 0f, 1f), out var r, out var g, out var b);
            result.SetPixel(x, y, r, g, b, p.A);
        }
        return result;
    }

    private static float PositiveModulo(float value, float modulus) { var r = value % modulus; return r < 0 ? r + modulus : r; }
    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        var max = MathF.Max(r, MathF.Max(g, b)); var min = MathF.Min(r, MathF.Min(g, b)); var d = max - min;
        v = max; s = max <= 0.0001f ? 0f : d / max; if (d <= 0.0001f) { h = 0f; return; }
        h = max == r ? ((g - b) / d) % 6f : max == g ? (b - r) / d + 2f : (r - g) / d + 4f; h /= 6f; if (h < 0f) h += 1f;
    }
    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        var c = v * s; var x = c * (1 - MathF.Abs((h * 6) % 2 - 1)); var m = v - c;
        (r, g, b) = h switch
        {
            < 1f / 6 => (c, x, 0f),
            < 2f / 6 => (x, c, 0f),
            < 3f / 6 => (0f, c, x),
            < 4f / 6 => (0f, x, c),
            < 5f / 6 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        r += m; g += m; b += m;
    }
}
