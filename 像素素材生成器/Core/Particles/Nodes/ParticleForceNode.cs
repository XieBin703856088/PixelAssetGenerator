using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Applies force fields to a particle buffer. Chainable: multiple force nodes
/// can be connected sequentially.
/// Input: ParticleBuffer, Output: ParticleBuffer.
/// </summary>
public sealed class ParticleForceNode : IGraphNode
{
    public string TypeName => "ParticleForce";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
        new GraphNodePort("Mask", GraphPortType.Mask)
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("forceType", "gravity",
            new[] { "gravity", "attract", "vortex", "wind", "turbulence", "noiseMotion" },
            new[] { "重力", "吸引", "漩涡", "风力", "湍流", "噪声运动" }, "力类型"),
        NodeParameterDefinition.Number("strength", 1.0, -5.0, 5.0, 0.1, "强度"),
        NodeParameterDefinition.Number("positionX", 0.5, 0, 1, 0.01, "位置X"),
        NodeParameterDefinition.Number("positionY", 0.5, 0, 1, 0.01, "位置Y"),
        NodeParameterDefinition.Number("radius", 0.3, 0, 1, 0.01, "作用半径"),
        NodeParameterDefinition.Choice("falloff", "inverseSquare",
            new[] { "constant", "linear", "inverseSquare" },
            new[] { "恒定", "线性", "平方反比" }, "衰减方式"),
        NodeParameterDefinition.Number("turbulenceFrequency", 2.0, 0.1, 10, 0.1, "湍流频率"),
        NodeParameterDefinition.Choice("noiseType", "perlin",
            new[] { "perlin", "value" },
            new[] { "Perlin噪声", "值噪声" }, "噪声类型(噪声运动)"),
        NodeParameterDefinition.Number("noiseFrequency", 2.0, 0.1, 10.0, 0.1, "噪声频率(噪声运动)"),
        NodeParameterDefinition.Number("timeScale", 0.3, 0.0, 2.0, 0.05, "时间缩放(噪声运动)"),
        NodeParameterDefinition.Integer("noiseOctaves", 3, 1, 6, 1, "噪声八度(噪声运动)"),
        NodeParameterDefinition.Number("anisotropy", 1.0, 0.0, 2.0, 0.05, "各向异性(噪声运动)"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    private static PixelBuffer? _sharedPlaceholder;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // ParticleForceNode doesn't produce a PixelBuffer directly — it modifies
        // the particle buffer via IPersistentStateNode. Return a shared 1x1 placeholder.
        if (_sharedPlaceholder == null)
        {
            _sharedPlaceholder = PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
        }
        return _sharedPlaceholder;
    }

    /// <summary>
    /// Creates a force field from the node parameters.
    /// </summary>
    public IParticleForce CreateForce(IReadOnlyDictionary<string, object> parameters)
    {
        var forceType = GraphNodeBase.GetChoice(parameters, "forceType", "gravity");
        var strength = GraphNodeBase.GetFloat(parameters, "strength", 1f);

        switch (forceType)
        {
            case "attract":
                return new PointForce
                {
                    CenterX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
                    CenterY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
                    Strength = strength,
                    Radius = GraphNodeBase.GetFloat(parameters, "radius", 0.3f),
                    Falloff = GetFalloff(parameters)
                };

            case "vortex":
                return new VortexForce
                {
                    CenterX = GraphNodeBase.GetFloat(parameters, "positionX", 0.5f),
                    CenterY = GraphNodeBase.GetFloat(parameters, "positionY", 0.5f),
                    Strength = strength,
                    Radius = GraphNodeBase.GetFloat(parameters, "radius", 0.3f)
                };

            case "turbulence":
                return new TurbulenceForce
                {
                    Strength = strength,
                    Frequency = GraphNodeBase.GetFloat(parameters, "turbulenceFrequency", 2f),
                };

            case "noiseMotion":
                return new NoiseMotionForce
                {
                    NoiseType = GraphNodeBase.GetChoice(parameters, "noiseType", "perlin"),
                    Frequency = GraphNodeBase.GetFloat(parameters, "noiseFrequency", 2f),
                    Strength = GraphNodeBase.GetFloat(parameters, "strength", 0.5f),
                    TimeScale = GraphNodeBase.GetFloat(parameters, "timeScale", 0.3f),
                    Octaves = GraphNodeBase.GetInt(parameters, "noiseOctaves", 3),
                    Seed = 42,
                    Anisotropy = GraphNodeBase.GetFloat(parameters, "anisotropy", 1f),
                };

            case "gravity":
            case "wind":
            default:
                // Gravity and wind are handled by the emitter's simulator directly
                return new PointForce
                {
                    Strength = 0, // no-op, handled externally
                };
        }
    }

    private static ForceFalloff GetFalloff(IReadOnlyDictionary<string, object> p)
    {
        var f = GraphNodeBase.GetChoice(p, "falloff", "inverseSquare");
        return f switch
        {
            "constant" => ForceFalloff.Constant,
            "linear" => ForceFalloff.Linear,
            _ => ForceFalloff.InverseSquare
        };
    }
}
