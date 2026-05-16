using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Physics.Nodes;

/// <summary>
/// Creates a physical force field (gravity well, vortex, wind, attractor/repeller)
/// that affects particles in the connected emitter.
/// </summary>
public sealed class PhysicsFieldNode : IGraphNode
{
    public string TypeName => "PhysicsField";
    public string Category => "Physics";

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
        NodeParameterDefinition.Choice("fieldType", "gravityWell",
            new[] { "gravityWell", "vortex", "wind", "attractor", "repeller" },
            new[] { "引力阱", "漩涡", "风力", "吸引", "排斥" }, "场类型"),
        NodeParameterDefinition.Number("strength", 1.0, -5.0, 5.0, 0.1, "强度"),
        NodeParameterDefinition.Number("positionX", 0.5, 0, 1, 0.01, "位置X"),
        NodeParameterDefinition.Number("positionY", 0.5, 0, 1, 0.01, "位置Y"),
        NodeParameterDefinition.Number("radius", 0.3, 0, 1, 0.01, "作用半径"),
        NodeParameterDefinition.Choice("falloffType", "inverseSquare",
            new[] { "constant", "linear", "inverseSquare" },
            new[] { "恒定", "线性", "平方反比" }, "衰减方式"),
        NodeParameterDefinition.Boolean("oscillate", false, "振荡"),
        NodeParameterDefinition.Number("frequency", 1.0, 0.1, 10, 0.1, "振荡频率"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    private static PixelBuffer? _sharedPlaceholder;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        if (_sharedPlaceholder == null)
        {
            _sharedPlaceholder = PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
        }
        return _sharedPlaceholder;
    }

    /// <summary>
    /// Creates an IParticleForce from this field's parameters.
    /// </summary>
    public IParticleForce CreateForce(IReadOnlyDictionary<string, object> parameters, float globalTime = 0)
    {
        var fieldType = GraphNodeBase.GetChoice(parameters, "fieldType", "gravityWell");
        var strength = GraphNodeBase.GetFloat(parameters, "strength", 1f);
        var oscillate = GraphNodeBase.GetBool(parameters, "oscillate", false);

        if (oscillate)
        {
            var freq = GraphNodeBase.GetFloat(parameters, "frequency", 1f);
            strength *= MathF.Sin(globalTime * freq * MathF.Tau);
        }

        switch (fieldType)
        {
            case "vortex":
                return new VortexForce
                {
                    CenterX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
                    CenterY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
                    Strength = strength,
                    Radius = GraphNodeBase.GetFloat(parameters, "radius", 0.3f),
                };

            case "wind":
                return new PointForce
                {
                    // Wind is applied as a directional force in the simulator
                    Strength = 0, // Handled separately
                };

            case "repeller":
                return new PointForce
                {
                    CenterX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
                    CenterY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
                    Strength = -strength, // Negative for repulsion
                    Radius = GraphNodeBase.GetFloat(parameters, "radius", 0.3f),
                    Falloff = GetFalloff(parameters)
                };

            case "attractor":
            case "gravityWell":
            default:
                return new PointForce
                {
                    CenterX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
                    CenterY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
                    Strength = strength,
                    Radius = GraphNodeBase.GetFloat(parameters, "radius", 0.3f),
                    Falloff = GetFalloff(parameters)
                };
        }
    }

    private static ForceFalloff GetFalloff(IReadOnlyDictionary<string, object> p)
    {
        var f = GraphNodeBase.GetChoice(p, "falloffType", "inverseSquare");
        return f switch
        {
            "constant" => ForceFalloff.Constant,
            "linear" => ForceFalloff.Linear,
            _ => ForceFalloff.InverseSquare
        };
    }
}
