using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Simulates audio-reactive values (amplitude, spectrum bands) using time-based
/// pseudo-random signals. Useful for driving animation parameters with music-like
/// rhythmic motion when real audio input is unavailable.
/// Outputs three Float values: total Amplitude, SpectrumLow energy, SpectrumHigh energy.
/// </summary>
public sealed class AudioReactiveNode : IGraphNode, IMultiOutputNode
{
    public string TypeName => "AudioReactive";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Amplitude", GraphPortType.Float),
        new GraphNodePort("SpectrumLow", GraphPortType.Float),
        new GraphNodePort("SpectrumHigh", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("smoothing", 0.5, 0, 1, 0.01, "平滑度"),
        NodeParameterDefinition.Number("threshold", 0.1, 0, 1, 0.01, "阈值"),
        NodeParameterDefinition.Number("sensitivity", 1.0, 0.1, 5.0, 0.1, "灵敏度"),
        NodeParameterDefinition.Choice("channel", "combined",
            new[] { "combined", "low", "high" },
            new[] { "全频段", "低频", "高频" }, "频段选择"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Fallback: return only Amplitude (port 0)
        var (amp, _, _) = ComputeValues(parameters, context);
        var buf = PixelBufferPool.Borrow(1, 1);
        buf.SetPixel(0, 0, amp, 0, 0, 1);
        return buf;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var (amp, low, high) = ComputeValues(parameters, context);
        // Build output buffers: each port gets its own 1x1 buffer
        // Port 0 = Amplitude, Port 1 = SpectrumLow, Port 2 = SpectrumHigh
        var amplitudeBuf = PixelBufferPool.Borrow(1, 1);
        var lowBuf = PixelBufferPool.Borrow(1, 1);
        var highBuf = PixelBufferPool.Borrow(1, 1);

        amplitudeBuf.SetPixel(0, 0, amp, 0, 0, 1);
        lowBuf.SetPixel(0, 0, low, 0, 0, 1);
        highBuf.SetPixel(0, 0, high, 0, 0, 1);

        return new[] { amplitudeBuf, lowBuf, highBuf };
    }

    private static (float Amplitude, float SpectrumLow, float SpectrumHigh) ComputeValues(
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var smoothing = (float)GraphNodeBase.GetFloat(parameters, "smoothing", 0.5f);
        var threshold = (float)GraphNodeBase.GetFloat(parameters, "threshold", 0.1f);
        var sensitivity = (float)GraphNodeBase.GetFloat(parameters, "sensitivity", 1.0f);
        var channel = GraphNodeBase.GetChoice(parameters, "channel", "combined");

        var t = context.GlobalTime > 0f ? context.GlobalTime : context.AnimationTime ?? 0f;
        var seed = context.Seed;

        // Slow bass-like sine (0.15 Hz)
        var bass = MathF.Sin(t * MathF.Tau * 0.15f);
        // Mid-range sine (0.6 Hz)
        var mid = MathF.Sin(t * MathF.Tau * 0.6f + 1.2f);
        // High-frequency shimmer (2.1 Hz)
        var highFreq = MathF.Sin(t * MathF.Tau * 2.1f + 2.8f);

        // Pseudo-Perlin noise component for organic variation
        var noiseVal = GraphNodeBase.TileableValueNoise(t * 0.5f, seed * 0.01f, 64, seed + 42);

        // Combine into amplitude with a beat-like envelope every ~2 seconds
        var beat = MathF.Pow(MathF.Max(0f, MathF.Sin(t * MathF.Tau * 0.5f)), 4f);
        var amplitudeRaw = (bass * 0.4f + mid * 0.3f + noiseVal * 0.3f) * 0.5f + 0.5f;
        amplitudeRaw += beat * 0.3f;
        amplitudeRaw *= sensitivity;

        // Smoothing: low-pass towards center
        amplitudeRaw = MathF.Max(0f, amplitudeRaw - threshold) / MathF.Max(1f - threshold, 0.01f);
        var ampSmoothed = amplitudeRaw * (1f - smoothing) + 0.3f * smoothing;
        var combinedAmplitude = Math.Clamp(ampSmoothed, 0f, 1f);

        // SpectrumLow: slow, bass-heavy signal
        var lowRaw = (bass * 0.6f + noiseVal * 0.2f + 0.2f) * sensitivity * 0.5f + 0.5f;
        var spectrumLow = Math.Clamp(lowRaw - threshold, 0f, 1f);

        // SpectrumHigh: fast, treble-heavy signal
        var highRaw = (highFreq * 0.4f + mid * 0.3f + beat * 0.3f) * sensitivity * 0.5f + 0.5f;
        var spectrumHigh = Math.Clamp(highRaw - threshold, 0f, 1f);

        // The selected band drives the primary Amplitude output while the two
        // dedicated spectrum outputs remain available for explicit wiring.
        var amplitude = channel switch
        {
            "low" => spectrumLow,
            "high" => spectrumHigh,
            _ => combinedAmplitude
        };

        return (amplitude, spectrumLow, spectrumHigh);
    }
}
