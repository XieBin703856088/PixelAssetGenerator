using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Generates noise-driven animated values over time.
/// Uses Perlin-like gradient noise (1D) or value noise to produce organic,
/// drifting motion suitable for natural-looking animation of parameters,
/// camera movement, particle properties, etc.
/// </summary>
public sealed class AnimationNoiseNode : IGraphNode, IMultiOutputNode
{
    public string TypeName => "NoiseAnimation";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Value", GraphPortType.Float),
        new GraphNodePort("Value2", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("noiseType", "perlin",
            new[] { "perlin", "value", "random" },
            new[] { "Perlin噪声", "值噪声", "纯随机" }, "噪声类型"),
        NodeParameterDefinition.Number("frequency", 0.5, 0.01, 10.0, 0.1, "频率"),
        NodeParameterDefinition.Number("amplitude", 1.0, 0.0, 10.0, 0.1, "振幅"),
        NodeParameterDefinition.Number("offset", 0.0, -5.0, 5.0, 0.01, "偏移"),
        NodeParameterDefinition.Number("lacunarity", 2.0, 1.0, 4.0, 0.1, "频率倍增"),
        NodeParameterDefinition.Number("persistence", 0.5, 0.0, 1.0, 0.05, "振幅衰减"),
        NodeParameterDefinition.Integer("octaves", 3, 1, 6, 1, "八度"),
        NodeParameterDefinition.Integer("seed", 42, 0, 9999, 1, "种子"),
        NodeParameterDefinition.Boolean("bipolar", true, "双极性输出"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var buf = PixelBufferPool.Borrow(1, 1);

        var noiseType = GraphNodeBase.GetChoice(parameters, "noiseType", "perlin");
        var frequency = GraphNodeBase.GetFloat(parameters, "frequency", 0.5f);
        var amplitude = GraphNodeBase.GetFloat(parameters, "amplitude", 1f);
        var offset = GraphNodeBase.GetFloat(parameters, "offset", 0f);
        var lacunarity = GraphNodeBase.GetFloat(parameters, "lacunarity", 2f);
        var persistence = GraphNodeBase.GetFloat(parameters, "persistence", 0.5f);
        var octaves = GraphNodeBase.GetInt(parameters, "octaves", 3);
        var seed = GraphNodeBase.GetInt(parameters, "seed", 42);
        var bipolar = GraphNodeBase.GetBool(parameters, "bipolar", true);

        var globalTime = context.GlobalTime > 0f ? context.GlobalTime : context.AnimationTime ?? 0f;
        var tx = globalTime * frequency;

        // Two independent noise values for Value and Value2 outputs
        var raw1 = EvaluateNoise(noiseType, tx, seed, octaves, persistence, lacunarity);
        var raw2 = EvaluateNoise(noiseType, tx + 100f, seed + 37, octaves, persistence, lacunarity);

        var val1 = bipolar ? raw1 : raw1 * 0.5f + 0.5f;
        val1 = val1 * amplitude + offset;

        var val2 = bipolar ? raw2 : raw2 * 0.5f + 0.5f;
        val2 = val2 * amplitude + offset;

        buf.SetPixel(0, 0, (float)Math.Clamp(val1, -100f, 100f), (float)Math.Clamp(val2, -100f, 100f), 0, 1);
        return buf;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        using var packed = Process(inputs, parameters, context);
        var value = packed.GetPixel(0, 0);
        return [Scalar(value.R), Scalar(value.G)];
    }

    private static PixelBuffer Scalar(float value)
    {
        var buffer = PixelBufferPool.Borrow(1, 1);
        buffer.SetPixel(0, 0, value, 0f, 0f, 1f);
        return buffer;
    }

    private static float EvaluateNoise(string noiseType, float t, int seed, int octaves, float persistence, float lacunarity)
    {
        return noiseType switch
        {
            "perlin" => FractalNoise1D(t, octaves, persistence, lacunarity, seed),
            "value" => FractalValue1D(t, octaves, persistence, lacunarity, seed),
            "random" => HashNoise1D(t, seed),
            _ => FractalNoise1D(t, octaves, persistence, lacunarity, seed),
        };
    }

    /// <summary>Fractal Perlin-like gradient noise (1D). Returns roughly [-1, 1].</summary>
    private static float FractalNoise1D(float t, int octaves, float persistence, float lacunarity, int seed)
    {
        var value = 0f;
        var max = 0f;
        var amp = 1f;
        var freq = 1f;

        for (var o = 0; o < octaves; o++)
        {
            value += GradientNoise1D(t * freq, seed + o * 137) * amp;
            max += amp;
            amp *= persistence;
            freq *= lacunarity;
        }

        return max > 0f ? value / max : 0f;
    }

    private static float GradientNoise1D(float t, int seed)
    {
        var i0 = (int)MathF.Floor(t);
        var i1 = i0 + 1;
        var ft = t - i0;

        // Smoothstep
        var s = ft * ft * (3f - 2f * ft);

        var g0 = Grad1D(Hash1D(i0, seed));
        var g1 = Grad1D(Hash1D(i1, seed));

        var d0 = g0 * ft;
        var d1 = g1 * (ft - 1f);

        return d0 + s * (d1 - d0);
    }

    /// <summary>Fractal value noise (1D). Returns roughly [-1, 1].</summary>
    private static float FractalValue1D(float t, int octaves, float persistence, float lacunarity, int seed)
    {
        var value = 0f;
        var max = 0f;
        var amp = 1f;
        var freq = 1f;

        for (var o = 0; o < octaves; o++)
        {
            value += ValueNoise1D(t * freq, seed + o * 137) * amp;
            max += amp;
            amp *= persistence;
            freq *= lacunarity;
        }

        return max > 0f ? value / max : 0f;
    }

    private static float ValueNoise1D(float t, int seed)
    {
        var i0 = (int)MathF.Floor(t);
        var i1 = i0 + 1;
        var ft = t - i0;

        var s = ft * ft * (3f - 2f * ft);
        var v0 = Hash1D(i0, seed);
        var v1 = Hash1D(i1, seed);

        return v0 + s * (v1 - v0);
    }

    private static float HashNoise1D(float t, int seed)
    {
        var i = (int)MathF.Floor(t);
        return Grad1D(Hash1D(i, seed));
    }

    private static float Grad1D(float hash)
    {
        return hash * 2f - 1f;
    }

    private static float Hash1D(int v, int seed)
    {
        unchecked
        {
            var h = v * 374761393 + seed * 1013;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue;
        }
    }
}
