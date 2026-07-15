using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Applies interactive mouse/touch-driven force fields to particles.
/// Uses the global context to read pointer position and applies attract/repel/
/// vortex forces at the cursor location. Enables real-time interactive
/// particle manipulation.
/// </summary>
public sealed class InteractiveForceNode : IGraphNode
{
    public string TypeName => "InteractiveForce";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
        new GraphNodePort("PositionX", GraphPortType.Float),
        new GraphNodePort("PositionY", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("interactionType", "attract",
            new[] { "attract", "repel", "vortex", "gravity" },
            new[] { "吸引", "排斥", "漩涡", "重力" }, "交互类型"),
        NodeParameterDefinition.Number("strength", 2.0, -10.0, 10.0, 0.1, "强度"),
        NodeParameterDefinition.Number("radius", 0.2, 0.01, 0.5, 0.01, "作用半径"),
        NodeParameterDefinition.Choice("falloff", "inverseSquare",
            new[] { "constant", "linear", "inverseSquare" },
            new[] { "恒定", "线性", "平方反比" }, "衰减方式"),
        NodeParameterDefinition.Boolean("usePointerInput", true, "使用鼠标位置"),
        NodeParameterDefinition.Number("defaultX", 0.5, 0, 1, 0.01, "默认位置X"),
        NodeParameterDefinition.Number("defaultY", 0.5, 0, 1, 0.01, "默认位置Y"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    internal PixelBuffer? LastPositionXInput { get; private set; }
    internal PixelBuffer? LastPositionYInput { get; private set; }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        LastPositionXInput = inputs.Length > 1 ? inputs[1] : null;
        LastPositionYInput = inputs.Length > 2 ? inputs[2] : null;
        return PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Creates an IParticleForce from node parameters at the current pointer position.
    /// Called by ParticleEvaluationService.
    /// </summary>
    public IParticleForce CreateForce(IReadOnlyDictionary<string, object> parameters,
        PixelBuffer? posXInput, PixelBuffer? posYInput)
    {
        var interactionType = GraphNodeBase.GetChoice(parameters, "interactionType", "attract");
        var strength = GraphNodeBase.GetFloat(parameters, "strength", 2f);
        var radius = GraphNodeBase.GetFloat(parameters, "radius", 0.2f);
        var usePointer = GraphNodeBase.GetBool(parameters, "usePointerInput", true);

        float posX, posY;

        if (usePointer && posXInput != null && posYInput != null)
        {
            var (rx, _, _, _) = posXInput.GetPixel(0, 0);
            var (ry, _, _, _) = posYInput.GetPixel(0, 0);
            posX = rx;
            posY = ry;
        }
        else
        {
            posX = GraphNodeBase.GetFloat(parameters, "defaultX", 0.5f);
            posY = GraphNodeBase.GetFloat(parameters, "defaultY", 0.5f);
        }

        var falloff = GraphNodeBase.GetChoice(parameters, "falloff", "inverseSquare");
        var falloffType = falloff switch
        {
            "constant" => ForceFalloff.Constant,
            "linear" => ForceFalloff.Linear,
            _ => ForceFalloff.InverseSquare
        };

        switch (interactionType)
        {
            case "repel":
                return new PointForce
                {
                    CenterX = posX,
                    CenterY = posY,
                    Strength = -Math.Abs(strength),
                    Radius = radius,
                    Falloff = falloffType
                };

            case "vortex":
                return new VortexForce
                {
                    CenterX = posX,
                    CenterY = posY,
                    Strength = strength,
                    Radius = radius
                };

            case "gravity":
                return new PointForce
                {
                    CenterX = posX,
                    CenterY = posY,
                    Strength = 0, // Gravity is applied uniformly, not from a point
                    Radius = 0
                };

            case "attract":
            default:
                return new PointForce
                {
                    CenterX = posX,
                    CenterY = posY,
                    Strength = Math.Abs(strength),
                    Radius = radius,
                    Falloff = falloffType
                };
        }
    }
}
