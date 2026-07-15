using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Outputs an animated float value based on keyframes.
/// Connect the Float output port to other nodes' parameter inputs.
/// The output value changes over time based on the animation timeline.
/// </summary>
public sealed class AnimatedParameterNode : IGraphNode
{
    public string TypeName => "AnimatedParameter";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Value", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("duration", 1.0, 0.1, 30, 0.1, "持续时间"),
        NodeParameterDefinition.Boolean("loop", true, "循环"),
        NodeParameterDefinition.Integer("keyframeCount", 2, 2, 10, 1, "关键帧数"),

        // Dynamic keyframe params (generated at runtime based on keyframeCount)
        // These are added in code below — they're not static because the count varies
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => GetRuntimeParameters();
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    private static IReadOnlyList<NodeParameterDefinition>? _runtimeParameters;

    private static IReadOnlyList<NodeParameterDefinition> GetRuntimeParameters()
    {
        if (_runtimeParameters != null) return _runtimeParameters;

        // Generate maximum possible keyframes for UI display
        var list = new List<NodeParameterDefinition>(_parameters);
        for (var i = 1; i <= 10; i++)
        {
            list.Add(NodeParameterDefinition.Number($"keyframe_{i}_time",
                (i - 1) / 9.0, 0, 1, 0.01, $"关键帧{i}时间"));
            list.Add(NodeParameterDefinition.Number($"keyframe_{i}_value",
                i <= 2 ? (i - 1f) : 0f, -10, 10, 0.01, $"关键帧{i}值"));
            list.Add(NodeParameterDefinition.Choice($"keyframe_{i}_easing", "linear",
                new[] { "linear", "easeIn", "easeOut", "easeInOut", "bounceOut", "elasticOut", "step" },
                new[] { "线性", "缓入", "缓出", "缓入缓出", "弹跳", "弹性", "阶跃" },
                $"关键帧{i}缓动"));
        }
        _runtimeParameters = list.AsReadOnly();
        return _runtimeParameters;
    }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Return a small 1x1 buffer encoding the animated value in R channel
        // This is a hack — ideally Float ports would carry scalar values directly
        var buf = PixelBufferPool.Borrow(1, 1);
        var value = EvaluateAtTime(parameters, context.AnimationTime ?? 0f);
        buf.SetPixel(0, 0, value, 0, 0, 1);
        return buf;
    }

    /// <summary>
    /// Evaluates the animated value at a given normalized time [0, 1].
    /// </summary>
    public float EvaluateAtTime(IReadOnlyDictionary<string, object> parameters, float normalizedTime)
    {
        var clip = AnimationEvaluator.CreateClipFromParameters("value", parameters);
        return clip.Evaluate(normalizedTime * clip.Duration);
    }
}
