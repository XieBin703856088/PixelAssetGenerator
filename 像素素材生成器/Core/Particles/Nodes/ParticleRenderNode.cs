using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Renders a ParticleBuffer onto a background PixelBuffer.
/// Inputs: ParticleBuffer (from emitter/force chain), Background (PixelBuffer).
/// Output: PixelBuffer with particles composited onto the background.
/// </summary>
public sealed class ParticleRenderNode : IGraphNode, IPersistentStateNode
{
    public string TypeName => "ParticleRender";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Background", GraphPortType.Image, "background"),
        new GraphNodePort("Mask", GraphPortType.Mask, "mask"),
        // Kept optional so projects saved before the particle port existed remain loadable.
        // ParticleEvaluationService can migrate an unambiguous single-emitter graph.
        new GraphNodePort("Particles", GraphPortType.Particle, "particles")
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Image", GraphPortType.Image)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("texture", "auto",
            new[] { "auto", "pixelCircle", "smoke", "flame", "spark", "streak", "rainDrop", "snowflake", "leaf", "bubble", "rune", "circle", "softCircle", "square", "diamond", "star", "glow" },
            new[] { "跟随发射器预设", "像素圆", "烟团", "火焰", "火花", "流光", "雨滴", "雪花", "叶片", "气泡", "魔法符文", "圆形", "柔和圆形", "方形", "菱形", "星形", "发光" }, "粒子纹理"),
        NodeParameterDefinition.Choice("blendMode", "auto",
            new[] { "auto", "alpha", "additive", "screen" },
            new[] { "跟随预设", "Alpha混合", "叠加", "滤色" }, "混合模式"),
        NodeParameterDefinition.Boolean("softEdges", false, "柔边"),
        NodeParameterDefinition.Boolean("pixelSnap", true, "像素吸附"),
        NodeParameterDefinition.Integer("alphaSteps", 4, 1, 8, 1, "透明度色阶"),
        NodeParameterDefinition.Number("scale", 1.0, 0.1, 5.0, 0.1, "缩放"),
        NodeParameterDefinition.Number("globalAlpha", 1.0, 0, 1.0, 0.01, "全局透明度"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.TimeDependent;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Ensure renderer is initialized
        GetOrCreateRenderer(parameters);

        // When background is connected, clone it as output base.
        // Particle rendering is coordinated by ParticleEvaluationService
        // which calls RenderParticles() after graph evaluation.
        if (inputs[0] != null)
        {
            return inputs[0]!.Clone();
        }

        // Create a properly-sized transparent buffer for particle rendering.
        // The evaluator uses this as the output; RenderParticles will overlay
        // particles onto it.
        var size = context.GetEffectiveSize();
        return PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
    }

    private ParticleRenderer GetOrCreateRenderer(IReadOnlyDictionary<string, object> parameters)
    {
        if (PersistentState is ParticleRenderer r)
            return r;

        r = new ParticleRenderer
        {
            TextureType = GetTextureType(parameters),
            BlendMode = GetBlendMode(parameters),
            SoftEdges = GraphNodeBase.GetBool(parameters, "softEdges", false),
            PixelSnap = GraphNodeBase.GetBool(parameters, "pixelSnap", true),
            AlphaSteps = GraphNodeBase.GetInt(parameters, "alphaSteps", 4),
            GlobalAlpha = GraphNodeBase.GetFloat(parameters, "globalAlpha", 1f),
            Scale = GraphNodeBase.GetFloat(parameters, "scale", 1f),
        };
        PersistentState = r;
        return r;
    }

    /// <summary>Re-applies parameters to the renderer. Called each frame.</summary>
    public void ApplyParameters(IReadOnlyDictionary<string, object> parameters, string? emitterPreset = null)
    {
        var r = GetOrCreateRenderer(parameters);
        r.TextureType = GetTextureType(parameters, emitterPreset);
        r.BlendMode = GetBlendMode(parameters, emitterPreset);
        r.SoftEdges = GraphNodeBase.GetBool(parameters, "softEdges", false);
        r.PixelSnap = GraphNodeBase.GetBool(parameters, "pixelSnap", true);
        r.AlphaSteps = GraphNodeBase.GetInt(parameters, "alphaSteps", 4);
        r.GlobalAlpha = GraphNodeBase.GetFloat(parameters, "globalAlpha", 1f);
        r.Scale = GraphNodeBase.GetFloat(parameters, "scale", 1f);
    }

    private static ParticleTextureType GetTextureType(IReadOnlyDictionary<string, object> p, string? emitterPreset = null)
    {
        var t = GraphNodeBase.GetChoice(p, "texture", "pixelCircle");
        if (t == "auto")
        {
            t = emitterPreset switch
            {
                "fire" => "flame",
                "smoke" or "dust" => "smoke",
                "rain" => "rainDrop",
                "snow" => "snowflake",
                "sparks" or "explosion" => "spark",
                "magic" => "rune",
                "bubbles" => "bubble",
                "leaves" => "leaf",
                _ => "pixelCircle"
            };
        }
        return t switch
        {
            "pixelCircle" => ParticleTextureType.PixelCircle,
            "smoke" => ParticleTextureType.SmokePuff,
            "flame" => ParticleTextureType.Flame,
            "spark" => ParticleTextureType.Spark,
            "streak" => ParticleTextureType.Streak,
            "rainDrop" => ParticleTextureType.RainDrop,
            "snowflake" => ParticleTextureType.Snowflake,
            "leaf" => ParticleTextureType.Leaf,
            "bubble" => ParticleTextureType.Bubble,
            "rune" => ParticleTextureType.Rune,
            "circle" => ParticleTextureType.Circle,
            "softCircle" => ParticleTextureType.SoftCircle,
            "square" => ParticleTextureType.Square,
            "diamond" => ParticleTextureType.Diamond,
            "star" => ParticleTextureType.Star,
            "glow" => ParticleTextureType.Glow,
            _ => ParticleTextureType.PixelCircle,
        };
    }

    private static ParticleBlendMode GetBlendMode(IReadOnlyDictionary<string, object> p, string? emitterPreset = null)
    {
        var m = GraphNodeBase.GetChoice(p, "blendMode", "alpha");
        if (m == "auto")
            m = emitterPreset is "fire" or "sparks" or "magic" or "explosion" ? "additive" : "alpha";
        return m switch
        {
            "additive" => ParticleBlendMode.Additive,
            "screen" => ParticleBlendMode.Screen,
            _ => ParticleBlendMode.Alpha,
        };
    }
}
