using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Outputs animation time (normalized 0-1), frame index, and playback speed.
/// Useful for driving other animation nodes or as a time reference.
/// </summary>
public sealed class TimeNode : IGraphNode, IMultiOutputNode
{
    public string TypeName => "Time";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Time", GraphPortType.Float),
        new GraphNodePort("Frame", GraphPortType.Float),
        new GraphNodePort("Speed", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("speedMultiplier", 1.0, 0, 10, 0.1, "速度倍率"),
        NodeParameterDefinition.Number("offset", 0, 0, 1, 0.01, "时间偏移"),
        NodeParameterDefinition.Boolean("loop", true, "循环"),
        NodeParameterDefinition.Integer("frameCount", 8, 1, 256, 1, "帧数"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Return a 1x1 buffer encoding time and frame in RGBA channels
        // R = normalized time, G = frame index normalized, B = speed
        var buf = PixelBufferPool.Borrow(1, 1);

        var speed = (float)GraphNodeBase.GetFloat(parameters, "speedMultiplier", 1f);
        var offset = (float)GraphNodeBase.GetFloat(parameters, "offset", 0f);
        var frameCount = GraphNodeBase.GetInt(parameters, "frameCount", 8);
        var loop = GraphNodeBase.GetBool(parameters, "loop", true);

        var baseTime = context.AnimationTime ?? 0f;
        var t = loop ? (baseTime * speed + offset) % 1f : Math.Min(baseTime * speed + offset, 1f);
        var frame = (int)(t * frameCount) % frameCount;

        buf.SetPixel(0, 0, t, frame / (float)Math.Max(frameCount - 1, 1), speed, 1);
        return buf;
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        using var packed = Process(inputs, parameters, context);
        var value = packed.GetPixel(0, 0);
        return [Scalar(value.R), Scalar(value.G), Scalar(value.B)];
    }

    private static PixelBuffer Scalar(float value)
    {
        var buffer = PixelBufferPool.Borrow(1, 1);
        buffer.SetPixel(0, 0, value, 0f, 0f, 1f);
        return buffer;
    }
}
