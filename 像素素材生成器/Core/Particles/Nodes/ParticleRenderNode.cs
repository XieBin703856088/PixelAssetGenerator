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
        new GraphNodePort("Background", GraphPortType.Image),
        new GraphNodePort("Mask", GraphPortType.Mask)
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Image", GraphPortType.Image)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("texture", "softCircle",
            new[] { "circle", "softCircle", "square", "diamond", "star", "glow" },
            new[] { "圆形", "柔和圆形", "方形", "菱形", "星形", "发光" }, "粒子纹理"),
        NodeParameterDefinition.Choice("blendMode", "alpha",
            new[] { "alpha", "additive", "screen" },
            new[] { "Alpha混合", "叠加", "滤色" }, "混合模式"),
        NodeParameterDefinition.Boolean("softEdges", true, "柔边"),
        NodeParameterDefinition.Number("scale", 1.0, 0.1, 5.0, 0.1, "缩放"),
        NodeParameterDefinition.Number("globalAlpha", 1.0, 0, 1.0, 0.01, "全局透明度"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

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
            SoftEdges = GraphNodeBase.GetBool(parameters, "softEdges", true),
            GlobalAlpha = GraphNodeBase.GetFloat(parameters, "globalAlpha", 1f),
            Scale = GraphNodeBase.GetFloat(parameters, "scale", 1f),
        };
        PersistentState = r;
        return r;
    }

    /// <summary>Re-applies parameters to the renderer. Called each frame.</summary>
    public void ApplyParameters(IReadOnlyDictionary<string, object> parameters)
    {
        if (PersistentState is not ParticleRenderer r) return;
        r.TextureType = GetTextureType(parameters);
        r.BlendMode = GetBlendMode(parameters);
        r.SoftEdges = GraphNodeBase.GetBool(parameters, "softEdges", true);
        r.GlobalAlpha = GraphNodeBase.GetFloat(parameters, "globalAlpha", 1f);
        r.Scale = GraphNodeBase.GetFloat(parameters, "scale", 1f);
    }

    private static ParticleTextureType GetTextureType(IReadOnlyDictionary<string, object> p)
    {
        var t = GraphNodeBase.GetChoice(p, "texture", "softCircle");
        return t switch
        {
            "circle" => ParticleTextureType.Circle,
            "square" => ParticleTextureType.Square,
            "diamond" => ParticleTextureType.Diamond,
            "star" => ParticleTextureType.Star,
            "glow" => ParticleTextureType.Glow,
            _ => ParticleTextureType.SoftCircle,
        };
    }

    private static ParticleBlendMode GetBlendMode(IReadOnlyDictionary<string, object> p)
    {
        var m = GraphNodeBase.GetChoice(p, "blendMode", "alpha");
        return m switch
        {
            "additive" => ParticleBlendMode.Additive,
            "screen" => ParticleBlendMode.Screen,
            _ => ParticleBlendMode.Alpha,
        };
    }
}
