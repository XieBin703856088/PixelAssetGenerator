using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation;

/// <summary>
/// Manages a set of AnimationClips and evaluates them at a given global time.
/// Returns a dictionary of parameter name → value for all clips.
/// </summary>
public sealed class AnimationEvaluator
{
    private readonly List<AnimationClip> _clips = new();

    public IReadOnlyList<AnimationClip> Clips => _clips;

    public void AddClip(AnimationClip clip) => _clips.Add(clip);
    public void RemoveClip(AnimationClip clip) => _clips.Remove(clip);
    public void Clear() => _clips.Clear();

    /// <summary>
    /// Evaluates all clips at the given global time.
    /// Returns a map of property name → interpolated value.
    /// </summary>
    public Dictionary<string, float> Evaluate(float globalTime)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (var clip in _clips)
        {
            result[clip.PropertyName] = clip.Evaluate(globalTime);
        }

        return result;
    }

    /// <summary>
    /// Evaluates all clips at a normalized time [0, 1].
    /// </summary>
    public Dictionary<string, float> EvaluateNormalized(float normalizedTime)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (var clip in _clips)
        {
            var clipTime = normalizedTime * clip.Duration;
            result[clip.PropertyName] = clip.Evaluate(clipTime);
        }

        return result;
    }

    /// <summary>
    /// Creates a clip from multi-keyframe parameters (used by AnimatedParameterNode).
    /// </summary>
    public static AnimationClip CreateClipFromParameters(
        string propertyName,
        IReadOnlyDictionary<string, object> parameters)
    {
        var keyframeCount = parameters.TryGetValue("keyframeCount", out var kc)
            ? Convert.ToInt32(kc) : 2;
        var duration = parameters.TryGetValue("duration", out var dur)
            ? (float)Convert.ToDouble(dur) : 1f;
        var loop = parameters.TryGetValue("loop", out var lp)
            ? Convert.ToBoolean(lp) : true;

        var keyframes = new List<Keyframe>();

        for (var i = 1; i <= keyframeCount; i++)
        {
            var time = parameters.TryGetValue($"keyframe_{i}_time", out var kt)
                ? (float)Convert.ToDouble(kt) : (i - 1f) / Math.Max(keyframeCount - 1, 1);
            var value = parameters.TryGetValue($"keyframe_{i}_value", out var kv)
                ? (float)Convert.ToDouble(kv) : 0f;
            var easing = parameters.TryGetValue($"keyframe_{i}_easing", out var ke)
                ? ke?.ToString() ?? "linear" : "linear";

            var easingType = ParseEasing(easing);
            keyframes.Add(new Keyframe(time, value, easingType));
        }

        return new AnimationClip
        {
            PropertyName = propertyName,
            Keyframes = keyframes.ToArray(),
            Duration = duration,
            Loop = loop
        };
    }

    private static EasingType ParseEasing(string name) => name.ToLowerInvariant() switch
    {
        "linear" => EasingType.Linear,
        "easein" or "ease_in" or "ease-in" or "quadin" => EasingType.EaseInQuad,
        "easeout" or "ease_out" or "ease-out" or "quadout" => EasingType.EaseOutQuad,
        "easeinout" or "ease_in_out" or "ease-in-out" => EasingType.EaseInOutQuad,
        "cubicin" => EasingType.EaseInCubic,
        "cubicout" => EasingType.EaseOutCubic,
        "bouncein" => EasingType.EaseInBounce,
        "bounceout" => EasingType.EaseOutBounce,
        "elasticin" => EasingType.EaseInElastic,
        "elasticout" => EasingType.EaseOutElastic,
        "step" => EasingType.Step,
        _ => EasingType.Linear
    };
}
