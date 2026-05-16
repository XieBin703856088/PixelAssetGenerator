using System;

namespace PixelAssetGenerator.Core.Animation;

/// <summary>
/// Easing function type enum.
/// </summary>
public enum EasingType
{
    Linear,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    EaseInQuart,
    EaseOutQuart,
    EaseInOutQuart,
    EaseInSine,
    EaseOutSine,
    EaseInOutSine,
    EaseInBack,
    EaseOutBack,
    EaseInOutBack,
    EaseInElastic,
    EaseOutElastic,
    EaseInOutElastic,
    EaseInBounce,
    EaseOutBounce,
    EaseInOutBounce,
    Step, // Discrete step at midpoint
}

/// <summary>
/// Library of easing functions. All take normalized time t [0,1] and return eased value.
/// </summary>
public static class Easing
{
    /// <summary>Evaluates an easing function.</summary>
    public static float Evaluate(EasingType type, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return type switch
        {
            EasingType.Linear => Linear(t),
            EasingType.EaseInQuad => QuadIn(t),
            EasingType.EaseOutQuad => QuadOut(t),
            EasingType.EaseInOutQuad => QuadInOut(t),
            EasingType.EaseInCubic => CubicIn(t),
            EasingType.EaseOutCubic => CubicOut(t),
            EasingType.EaseInOutCubic => CubicInOut(t),
            EasingType.EaseInQuart => QuartIn(t),
            EasingType.EaseOutQuart => QuartOut(t),
            EasingType.EaseInOutQuart => QuartInOut(t),
            EasingType.EaseInSine => SineIn(t),
            EasingType.EaseOutSine => SineOut(t),
            EasingType.EaseInOutSine => SineInOut(t),
            EasingType.EaseInBack => BackIn(t),
            EasingType.EaseOutBack => BackOut(t),
            EasingType.EaseInOutBack => BackInOut(t),
            EasingType.EaseInElastic => ElasticIn(t),
            EasingType.EaseOutElastic => ElasticOut(t),
            EasingType.EaseInOutElastic => ElasticInOut(t),
            EasingType.EaseInBounce => BounceIn(t),
            EasingType.EaseOutBounce => BounceOut(t),
            EasingType.EaseInOutBounce => BounceInOut(t),
            EasingType.Step => t < 0.5f ? 0f : 1f,
            _ => Linear(t)
        };
    }

    // ── Linear ──
    public static float Linear(float t) => t;

    // ── Quad ──
    public static float QuadIn(float t) => t * t;
    public static float QuadOut(float t) => t * (2f - t);
    public static float QuadInOut(float t) =>
        t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

    // ── Cubic ──
    public static float CubicIn(float t) => t * t * t;
    public static float CubicOut(float t) => (t - 1f) * (t - 1f) * (t - 1f) + 1f;
    public static float CubicInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f;

    // ── Quart ──
    public static float QuartIn(float t) => t * t * t * t;
    public static float QuartOut(float t) => 1f - (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);
    public static float QuartInOut(float t) =>
        t < 0.5f ? 8f * t * t * t * t : 1f - 8f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

    // ── Sine ──
    public static float SineIn(float t) => 1f - MathF.Cos(t * MathF.PI * 0.5f);
    public static float SineOut(float t) => MathF.Sin(t * MathF.PI * 0.5f);
    public static float SineInOut(float t) => (1f - MathF.Cos(t * MathF.PI)) * 0.5f;

    // ── Back ──
    private const float BackK = 1.70158f;
    private const float BackK2 = BackK * 1.525f;
    public static float BackIn(float t) => (BackK + 1f) * t * t * t - BackK * t * t;
    public static float BackOut(float t) =>
        1f + (BackK + 1f) * (t - 1f) * (t - 1f) * (t - 1f) + BackK * (t - 1f) * (t - 1f);
    public static float BackInOut(float t) =>
        t < 0.5f
            ? ((BackK2 + 1f) * 2f * t * 2f * t * 2f * t - BackK2 * 2f * t * 2f * t) * 0.5f
            : ((BackK2 + 1f) * (2f * t - 2f) * (2f * t - 2f) * (2f * t - 2f) + BackK2 * (2f * t - 2f) * (2f * t - 2f) + 2f) * 0.5f;

    // ── Elastic ──
    public static float ElasticIn(float t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * MathF.Tau / 3f);
    }

    public static float ElasticOut(float t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * MathF.Tau / 3f) + 1f;
    }

    public static float ElasticInOut(float t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t < 0.5f
            ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * MathF.Tau / 4.5f)) * 0.5f
            : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * MathF.Tau / 4.5f) * 0.5f + 1f;
    }

    // ── Bounce ──
    public static float BounceIn(float t) => 1f - BounceOut(1f - t);

    public static float BounceOut(float t)
    {
        if (t < 1f / 2.75f) return 7.5625f * t * t;
        if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
        if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
        t -= 2.625f / 2.75f; return 7.5625f * t * t + 0.984375f;
    }

    public static float BounceInOut(float t) =>
        t < 0.5f ? BounceIn(t * 2f) * 0.5f : BounceOut(t * 2f - 1f) * 0.5f + 0.5f;
}
