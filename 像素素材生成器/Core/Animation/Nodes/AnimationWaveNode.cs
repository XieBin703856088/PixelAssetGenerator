using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Generates periodic wave values (sine, triangle, sawtooth, square, noise)
/// over time. Outputs the current wave value as a Float port.
/// Useful for driving animation parameters, particle properties, or texture
/// blending with rhythmic motion.
/// </summary>
public sealed class AnimationWaveNode : IGraphNode
{
    public string TypeName => "Wave";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Value", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("waveType", "sine",
            new[] { "sine", "triangle", "sawtooth", "square", "pulse", "random" },
            new[] { "正弦波", "三角波", "锯齿波", "方波", "脉冲波", "随机" }, "波形类型"),
        NodeParameterDefinition.Number("frequency", 1.0, 0.01, 20.0, 0.1, "频率 (Hz)"),
        NodeParameterDefinition.Number("amplitude", 1.0, 0.0, 10.0, 0.1, "振幅"),
        NodeParameterDefinition.Number("offset", 0.0, -5.0, 5.0, 0.01, "偏移"),
        NodeParameterDefinition.Number("phase", 0.0, 0.0, 360.0, 1.0, "相位偏移 (°)"),
        NodeParameterDefinition.Number("dutyCycle", 0.5, 0.01, 0.99, 0.01, "占空比 (脉冲波)"),
        NodeParameterDefinition.Boolean("bipolar", false, "双极性输出"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var buf = PixelBufferPool.Borrow(1, 1);

        var waveType = GraphNodeBase.GetChoice(parameters, "waveType", "sine");
        var frequency = GraphNodeBase.GetFloat(parameters, "frequency", 1f);
        var amplitude = GraphNodeBase.GetFloat(parameters, "amplitude", 1f);
        var offset = GraphNodeBase.GetFloat(parameters, "offset", 0f);
        var phase = GraphNodeBase.GetFloat(parameters, "phase", 0f);
        var dutyCycle = GraphNodeBase.GetFloat(parameters, "dutyCycle", 0.5f);
        var bipolar = GraphNodeBase.GetBool(parameters, "bipolar", false);

        var globalTime = context.GlobalTime > 0f ? context.GlobalTime : context.AnimationTime ?? 0f;
        var phaseRad = phase * MathF.PI / 180f;
        var t = globalTime * frequency + phaseRad / MathF.Tau;

        var raw = waveType switch
        {
            "sine" => MathF.Sin(t * MathF.Tau),
            "triangle" => Triangle(t),
            "sawtooth" => Sawtooth(t),
            "square" => Square(t, 0.5f),
            "pulse" => Square(t, dutyCycle),
            "random" => SampleNoise(t),
            _ => MathF.Sin(t * MathF.Tau),
        };

        var value = bipolar ? raw : raw * 0.5f + 0.5f;
        value = value * amplitude + offset;

        buf.SetPixel(0, 0, value, 0, 0, 1);
        return buf;
    }

    private static float Triangle(float t)
    {
        var phase = t - MathF.Floor(t); // [0, 1)
        return phase < 0.5f
            ? 4f * phase - 1f      // rise: -1 to 1
            : 3f - 4f * phase;     // fall: 1 to -1
    }

    private static float Sawtooth(float t)
    {
        var phase = t - MathF.Floor(t); // [0, 1)
        return 2f * phase - 1f;
    }

    private static float Square(float t, float dutyCycle)
    {
        var phase = t - MathF.Floor(t); // [0, 1)
        return phase < dutyCycle ? 1f : -1f;
    }

    private static float SampleNoise(float t)
    {
        // Simple hash-based noise that varies smoothly with time
        var frac = t - MathF.Floor(t);
        var floor = (int)MathF.Floor(t);
        var ceil = floor + 1;

        var h1 = Hash(floor);
        var h2 = Hash(ceil);

        // Smooth interpolation between adjacent hash values
        var s = frac * frac * (3f - 2f * frac); // smoothstep
        return h1 + (h2 - h1) * s;
    }

    private static float Hash(int v)
    {
        unchecked
        {
            var h = v * 374761393;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue * 2f - 1f;
        }
    }
}
