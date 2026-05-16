using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Plays multiple timed animation segments in sequence, each outputting
/// a different value curve. Functions like a DAW-style sequencer:
/// segments play one after another, with configurable duration, start/end
/// values, easing, and optional cross-fade blending between segments.
/// </summary>
public sealed class AnimationSequencerNode : IGraphNode
{
    public string TypeName => "AnimationSequencer";
    public string Category => "Animation";

    private const int MaxSegments = 16;
    private const int DefaultSegments = 3;

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Value", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _staticParameters = new[]
    {
        NodeParameterDefinition.Integer("segments", DefaultSegments, 1, MaxSegments, 1, "段数"),
        NodeParameterDefinition.Boolean("loop", true, "循环"),
        NodeParameterDefinition.Number("transitionBlend", 0.1, 0, 1, 0.01, "过渡混合"),
    };

    private static readonly string[] _easingChoices = new[]
    {
        "linear", "easeIn", "easeOut", "easeInOut", "bounceOut", "elasticOut"
    };

    private static readonly string[] _easingDisplayChoices = new[]
    {
        "线性", "缓入", "缓出", "缓入缓出", "弹跳", "弹性"
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => GetRuntimeParameters();

    private static IReadOnlyList<NodeParameterDefinition>? _runtimeParameters;

    private static IReadOnlyList<NodeParameterDefinition> GetRuntimeParameters()
    {
        if (_runtimeParameters != null) return _runtimeParameters;

        var list = new List<NodeParameterDefinition>(_staticParameters);
        for (var i = 1; i <= MaxSegments; i++)
        {
            list.Add(NodeParameterDefinition.Number($"segment_{i}_duration",
                1.0, 0.1, 30, 0.1, $"段{i}持续时间"));
            list.Add(NodeParameterDefinition.Number($"segment_{i}_startValue",
                0.0, -10, 10, 0.01, $"段{i}起始值"));
            list.Add(NodeParameterDefinition.Number($"segment_{i}_endValue",
                1.0, -10, 10, 0.01, $"段{i}结束值"));
            list.Add(NodeParameterDefinition.Choice($"segment_{i}_easing", "linear",
                _easingChoices, _easingDisplayChoices, $"段{i}缓动"));
        }
        _runtimeParameters = list.AsReadOnly();
        return _runtimeParameters;
    }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var buf = PixelBufferPool.Borrow(1, 1);
        var value = EvaluateAtTime(parameters, context.AnimationTime ?? 0f);
        buf.SetPixel(0, 0, value, 0, 0, 1);
        return buf;
    }

    /// <summary>
    /// Evaluates the sequencer output at a given time (in seconds).
    /// </summary>
    public float EvaluateAtTime(IReadOnlyDictionary<string, object> parameters, float time)
    {
        var segments = GraphNodeBase.GetInt(parameters, "segments", DefaultSegments);
        segments = Math.Clamp(segments, 1, MaxSegments);

        var loop = GraphNodeBase.GetBool(parameters, "loop", true);
        var transitionBlend = GraphNodeBase.GetFloat(parameters, "transitionBlend", 0.1f);
        transitionBlend = Math.Clamp(transitionBlend, 0f, 1f);

        // Gather per-segment data
        var durations = new float[segments];
        var startValues = new float[segments];
        var endValues = new float[segments];
        var easings = new string[segments];

        for (var i = 0; i < segments; i++)
        {
            var idx = i + 1;
            durations[i] = GraphNodeBase.GetFloat(parameters, $"segment_{idx}_duration", 1f);
            startValues[i] = GraphNodeBase.GetFloat(parameters, $"segment_{idx}_startValue", 0f);
            endValues[i] = GraphNodeBase.GetFloat(parameters, $"segment_{idx}_endValue", 1f);
            easings[i] = GraphNodeBase.GetChoice(parameters, $"segment_{idx}_easing", "linear");
            durations[i] = Math.Max(durations[i], 0.1f);
        }

        // Compute total duration
        var totalDuration = durations.Sum();

        if (totalDuration <= 0f)
            return 0f;

        // Wrap time if looping
        if (loop)
            time = time % totalDuration;
        else
            time = Math.Min(time, totalDuration - 0.0001f);

        if (time < 0f)
            time = 0f;

        // Find current segment
        var accumulatedTime = 0f;
        var segmentIndex = segments - 1;

        for (var i = 0; i < segments; i++)
        {
            var nextAccumulated = accumulatedTime + durations[i];
            if (time < nextAccumulated || (i == segments - 1 && time <= nextAccumulated))
            {
                segmentIndex = i;
                break;
            }
            accumulatedTime = nextAccumulated;
        }

        // Compute primary value for this segment
        var localTime = durations[segmentIndex] > 0
            ? Math.Clamp((time - accumulatedTime) / durations[segmentIndex], 0f, 1f)
            : 0f;

        var easedLocal = ApplyEasing(easings[segmentIndex], localTime);
        var primaryValue = Lerp(startValues[segmentIndex], endValues[segmentIndex], easedLocal);

        // Cross-fade blend with previous segment if transitionBlend > 0
        if (transitionBlend > 0f && segmentIndex > 0)
        {
            var prevSegmentIndex = segmentIndex - 1;

            // Blend region: first transitionBlend portion of the current segment
            var blendThreshold = transitionBlend;
            if (localTime < blendThreshold)
            {
                // Value of the previous segment at its endpoint
                var prevEndValue = endValues[prevSegmentIndex];

                // Blend weight: 1 at boundary, 0 at blendThreshold
                var blendT = blendThreshold > 0f ? Math.Clamp(localTime / blendThreshold, 0f, 1f) : 1f;
                var blendWeight = 1f - blendT; // 0 = no previous, 1 = full previous

                // Smooth the blend weight for a nicer transition
                blendWeight = blendWeight * blendWeight * (3f - 2f * blendWeight); // smoothstep

                // Cross-fade: linear blend between previous endpoint and current segment value
                primaryValue = Lerp(primaryValue, prevEndValue, blendWeight);
            }
        }
        // Wrap-around blend: if loop and transitionBlend > 0,
        // blend last segment into first segment
        else if (transitionBlend > 0f && loop && segments > 1 && segmentIndex == 0)
        {
            var lastSegmentIndex = segments - 1;
            var blendThreshold = transitionBlend;
            if (localTime < blendThreshold)
            {
                var prevEndValue = endValues[lastSegmentIndex];

                var blendT = blendThreshold > 0f ? Math.Clamp(localTime / blendThreshold, 0f, 1f) : 1f;
                var blendWeight = 1f - blendT;
                blendWeight = blendWeight * blendWeight * (3f - 2f * blendWeight);

                primaryValue = Lerp(primaryValue, prevEndValue, blendWeight);
            }
        }

        return primaryValue;
    }

    private static float ApplyEasing(string easing, float t)
    {
        var easingType = easing.ToLowerInvariant() switch
        {
            "easein" => EasingType.EaseInQuad,
            "easeout" => EasingType.EaseOutQuad,
            "easeinout" => EasingType.EaseInOutQuad,
            "bounceout" => EasingType.EaseOutBounce,
            "elasticout" => EasingType.EaseOutElastic,
            _ => EasingType.Linear
        };
        return Easing.Evaluate(easingType, t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
