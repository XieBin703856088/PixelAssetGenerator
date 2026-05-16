using System;

namespace PixelAssetGenerator.Core.Animation;

/// <summary>
/// A single keyframe: time [0,1], value, and easing type.
/// </summary>
public sealed record Keyframe(float Time, float Value, EasingType Easing);

/// <summary>
/// An animation clip defining parameter interpolation over time.
/// Supports looping and multiple keyframes with per-keyframe easing.
/// </summary>
public sealed class AnimationClip
{
    /// <summary>Target parameter name (e.g., "scale", "angle").</summary>
    public string PropertyName { get; init; } = string.Empty;

    /// <summary>Keyframe sequence (must be sorted by time ascending).</summary>
    public Keyframe[] Keyframes { get; init; } = Array.Empty<Keyframe>();

    /// <summary>Duration of one loop in seconds.</summary>
    public float Duration { get; init; } = 1f;

    /// <summary>Whether to loop when time exceeds Duration.</summary>
    public bool Loop { get; init; } = true;

    /// <summary>
    /// Evaluates the clip at a given local time (in seconds).
    /// Returns the interpolated value.
    /// </summary>
    public float Evaluate(float time)
    {
        if (Keyframes.Length == 0) return 0f;

        if (Loop)
            time = time % Duration;
        else
            time = Math.Min(time, Duration);

        var t = Duration > 0 ? time / Duration : 0f; // normalized [0,1]

        if (Keyframes.Length == 1)
            return Keyframes[0].Value;

        // Find surrounding keyframes
        var kf = Keyframes;
        if (t <= kf[0].Time) return kf[0].Value;
        if (t >= kf[^1].Time) return kf[^1].Value;

        for (var i = 0; i < kf.Length - 1; i++)
        {
            if (t >= kf[i].Time && t < kf[i + 1].Time)
            {
                var localT = (t - kf[i].Time) / (kf[i + 1].Time - kf[i].Time);
                localT = Easing.Evaluate(kf[i].Easing, localT);
                return Lerp(kf[i].Value, kf[i + 1].Value, localT);
            }
        }

        return kf[^1].Value;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
