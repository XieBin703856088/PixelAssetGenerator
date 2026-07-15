using System;

namespace PixelAssetGenerator.Core.Particles;

/// <summary>
/// Renders particles into a PixelBuffer with configurable texture,
/// blend mode, and soft edges.
/// </summary>
public sealed class ParticleRenderer
{
    /// <summary>Particle sprite shape.</summary>
    public ParticleTextureType TextureType { get; set; } = ParticleTextureType.PixelCircle;

    /// <summary>How particles are blended with the background.</summary>
    public ParticleBlendMode BlendMode { get; set; } = ParticleBlendMode.Alpha;

    /// <summary>Whether to use soft (anti-aliased) edges.</summary>
    public bool SoftEdges { get; set; }

    /// <summary>Snaps particle centers/sizes and opacity bands to the pixel grid.</summary>
    public bool PixelSnap { get; set; } = true;

    /// <summary>Number of discrete opacity levels used by pixel particle textures.</summary>
    public int AlphaSteps { get; set; } = 4;

    /// <summary>Global alpha multiplier applied to all particles.</summary>
    public float GlobalAlpha { get; set; } = 1f;

    /// <summary>Scale multiplier for all particle sizes.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// Renders active particles from the buffer onto a PixelBuffer.
    /// </summary>
    /// <param name="particles">Particle data to render.</param>
    /// <param name="output">Output pixel buffer (modified in place).</param>
    /// <param name="count">Number of active particles to render.</param>
    public void Render(ReadOnlySpan<ParticleData> particles, PixelBuffer output, int count)
    {
        var w = output.Width;
        var h = output.Height;
        var data = output.AsSpan();
        var globalA = GlobalAlpha;

        for (var i = 0; i < count; i++)
        {
            ref readonly var p = ref particles[i];
            if (!p.Active) continue;

            var pixelSize = Math.Max(p.Size * Scale * w, 1f);
            if (PixelSnap)
                pixelSize = Math.Max(1f, MathF.Round(pixelSize));
            var halfSize = pixelSize * 0.5f;
            var centerX = p.X * w;
            var centerY = p.Y * h;
            if (PixelSnap)
            {
                // Pixel samples live at n + 0.5. Snapping to an integer placed a
                // one-pixel particle between four samples and could make it vanish.
                centerX = MathF.Floor(centerX) + 0.5f;
                centerY = MathF.Floor(centerY) + 0.5f;
            }
            var rot = p.Rotation;

            // Compute bounding box
            var minX = (int)Math.Floor(centerX - halfSize);
            var maxX = (int)Math.Ceiling(centerX + halfSize);
            var minY = (int)Math.Floor(centerY - halfSize);
            var maxY = (int)Math.Ceiling(centerY + halfSize);

            // Clamp to buffer
            minX = Math.Clamp(minX, 0, w - 1);
            maxX = Math.Clamp(maxX, 0, w - 1);
            minY = Math.Clamp(minY, 0, h - 1);
            maxY = Math.Clamp(maxY, 0, h - 1);

            // Precompute rotation trig
            var cosR = MathF.Cos(rot);
            var sinR = MathF.Sin(rot);

            for (var py = minY; py <= maxY; py++)
            {
                for (var px = minX; px <= maxX; px++)
                {
                    // Local coords relative to particle center [-1, 1]
                    var lx = (px - centerX + 0.5f) / halfSize;
                    var ly = (py - centerY + 0.5f) / halfSize;

                    // Rotate local coords
                    var rlx = lx * cosR + ly * sinR;
                    var rly = -lx * sinR + ly * cosR;

                    // Sample particle texture
                    var alpha = SampleTexture(rlx, rly, out var texR, out var texG, out var texB);
                    if (alpha <= 0f) continue;

                    // Apply particle color
                    var srcR = texR * p.R;
                    var srcG = texG * p.G;
                    var srcB = texB * p.B;
                    if (PixelSnap && alpha > 0f)
                    {
                        var steps = Math.Clamp(AlphaSteps, 1, 8);
                        alpha = MathF.Round(alpha * steps) / steps;
                    }
                    var srcA = Math.Clamp(alpha * p.A * globalA, 0f, 1f);

                    if (srcA <= 0f) continue;

                    var idx = (py * w + px) * 4;

                    switch (BlendMode)
                    {
                        case ParticleBlendMode.Alpha:
                            var invA = 1f - srcA;
                            data[idx] = data[idx] * invA + srcR * srcA;
                            data[idx + 1] = data[idx + 1] * invA + srcG * srcA;
                            data[idx + 2] = data[idx + 2] * invA + srcB * srcA;
                            data[idx + 3] = Math.Min(1f, data[idx + 3] + srcA);
                            break;

                        case ParticleBlendMode.Additive:
                            data[idx] = Math.Min(1f, data[idx] + srcR * srcA);
                            data[idx + 1] = Math.Min(1f, data[idx + 1] + srcG * srcA);
                            data[idx + 2] = Math.Min(1f, data[idx + 2] + srcB * srcA);
                            data[idx + 3] = Math.Min(1f, data[idx + 3] + srcA);
                            break;

                        case ParticleBlendMode.Screen:
                            data[idx] = 1f - (1f - data[idx]) * (1f - srcR * srcA);
                            data[idx + 1] = 1f - (1f - data[idx + 1]) * (1f - srcG * srcA);
                            data[idx + 2] = 1f - (1f - data[idx + 2]) * (1f - srcB * srcA);
                            data[idx + 3] = Math.Min(1f, data[idx + 3] + srcA);
                            break;
                    }
                }
            }
        }
    }

    private float SampleTexture(float x, float y, out float r, out float g, out float b)
    {
        var distSq = x * x + y * y;

        switch (TextureType)
        {
            case ParticleTextureType.PixelCircle:
            {
                var ax = MathF.Abs(x);
                var ay = MathF.Abs(y);
                var octagon = MathF.Max(ax, ay) + MathF.Min(ax, ay) * 0.42f;
                if (octagon > 1f) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return octagon < 0.58f ? 1f : 0.72f;
            }

            case ParticleTextureType.SmokePuff:
            {
                var d1 = (x + 0.28f) * (x + 0.28f) + (y + 0.02f) * (y + 0.02f);
                var d2 = (x - 0.27f) * (x - 0.27f) + (y + 0.08f) * (y + 0.08f);
                var d3 = x * x + (y - 0.28f) * (y - 0.28f);
                var puff = MathF.Min(d1 / 0.78f, MathF.Min(d2 / 0.74f, d3 / 0.7f));
                if (puff > 1f) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return puff < 0.42f ? 1f : puff < 0.74f ? 0.72f : 0.42f;
            }

            case ParticleTextureType.Flame:
            {
                if (y < -1f || y > 1f) { r = g = b = 0f; return 0f; }
                var width = 0.22f + (y + 1f) * 0.34f;
                var flicker = MathF.Sin((y + 1f) * 8f) * 0.07f;
                if (MathF.Abs(x + flicker) > width) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return MathF.Abs(x) < width * 0.45f && y > -0.25f ? 1f : 0.68f;
            }

            case ParticleTextureType.Spark:
            {
                var cross = MathF.Min(MathF.Abs(x) * 0.42f + MathF.Abs(y),
                    MathF.Abs(x) + MathF.Abs(y) * 0.42f);
                if (cross > 0.72f) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return cross < 0.28f ? 1f : 0.65f;
            }

            case ParticleTextureType.Streak:
            {
                var shape = MathF.Max(MathF.Abs(x) * 2.6f, MathF.Abs(y));
                if (shape > 1f) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return MathF.Abs(x) < 0.16f ? 1f : 0.62f;
            }

            case ParticleTextureType.RainDrop:
            {
                var body = MathF.Abs(x) <= 0.22f && y >= -1f && y <= 0.78f;
                var tip = y > 0.55f && MathF.Abs(x) <= Math.Max(0f, (1f - y) * 0.5f);
                if (!body && !tip) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return MathF.Abs(x) < 0.1f ? 1f : 0.7f;
            }

            case ParticleTextureType.Snowflake:
            {
                var cross = MathF.Min(MathF.Abs(x), MathF.Abs(y));
                var diagonal = MathF.Min(MathF.Abs(x - y), MathF.Abs(x + y));
                var extent = MathF.Max(MathF.Abs(x), MathF.Abs(y));
                if (extent > 1f || MathF.Min(cross, diagonal * 0.72f) > 0.19f)
                { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return extent < 0.28f ? 1f : 0.76f;
            }

            case ParticleTextureType.Leaf:
            {
                var shape = MathF.Abs(y) + MathF.Abs(x) * 0.72f;
                if (shape > 1f) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return MathF.Abs(x) < 0.13f ? 1f : shape < 0.68f ? 0.86f : 0.62f;
            }

            case ParticleTextureType.Bubble:
            {
                var dist = MathF.Sqrt(distSq);
                var highlight = (x + 0.36f) * (x + 0.36f) + (y + 0.36f) * (y + 0.36f) < 0.055f;
                if (!highlight && (dist < 0.62f || dist > 1f)) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return highlight ? 1f : dist > 0.82f ? 0.82f : 0.48f;
            }

            case ParticleTextureType.Rune:
            {
                var dist = MathF.Sqrt(distSq);
                var ring = dist >= 0.58f && dist <= 0.88f;
                var cross = MathF.Min(MathF.Abs(x), MathF.Abs(y)) < 0.13f && dist < 0.68f;
                var diagonal = MathF.Min(MathF.Abs(x - y), MathF.Abs(x + y)) < 0.12f && dist < 0.54f;
                if (!ring && !cross && !diagonal) { r = g = b = 0f; return 0f; }
                r = g = b = 1f;
                return ring ? 0.72f : 1f;
            }

            case ParticleTextureType.Circle:
                if (distSq > 1f) { r = g = b = 0; return 0f; }
                r = g = b = 1f;
                return 1f;

            case ParticleTextureType.SoftCircle:
            {
                var dist = MathF.Sqrt(distSq);
                if (dist >= 1f) { r = g = b = 0; return 0f; }
                var alpha = SoftEdges ? SmoothEdge(dist, 0.7f, 1f) : 1f;
                r = g = b = 1f;
                return alpha;
            }

            case ParticleTextureType.Square:
                if (MathF.Abs(x) > 1f || MathF.Abs(y) > 1f) { r = g = b = 0; return 0f; }
                r = g = b = 1f;
                return SoftEdges
                    ? Math.Min(SmoothEdge(MathF.Abs(x), 0.7f, 1f), SmoothEdge(MathF.Abs(y), 0.7f, 1f))
                    : 1f;

            case ParticleTextureType.Diamond:
            {
                var manhattan = MathF.Abs(x) + MathF.Abs(y);
                if (manhattan >= 1f) { r = g = b = 0; return 0f; }
                var alpha = SoftEdges ? SmoothEdge(manhattan, 0.7f, 1f) : 1f;
                r = g = b = 1f;
                return alpha;
            }

            case ParticleTextureType.Star:
            {
                var dist = MathF.Sqrt(distSq);
                if (dist >= 1f) { r = g = b = 0; return 0f; }
                var angle = MathF.Atan2(y, x);
                // 5-pointed star: cos(5*angle/2) with absolute value creates 5 points
                var starPattern = MathF.Abs(MathF.Cos(angle * 5f * 0.5f));
                var starAlpha = (0.3f + 0.7f * starPattern) * SmoothEdge(dist, 0.3f, 1f);
                r = g = b = 1f;
                return SoftEdges ? starAlpha : Math.Min(1f, starAlpha * 2f);
            }

            case ParticleTextureType.Glow:
            {
                var glowAlpha = MathF.Exp(-distSq * 3f);
                r = g = b = 1f;
                return glowAlpha;
            }

            default:
                r = g = b = 1f;
                return 1f;
        }
    }

    private static float SmoothEdge(float value, float edge0, float edge1)
    {
        if (value <= edge0) return 1f;
        if (value >= edge1) return 0f;
        var t = (value - edge0) / (edge1 - edge0);
        return t * t * (3f - 2f * t); // smoothstep
    }
}

/// <summary>Particle sprite texture shapes.</summary>
public enum ParticleTextureType
{
    PixelCircle,
    SmokePuff,
    Flame,
    Spark,
    Streak,
    RainDrop,
    Snowflake,
    Leaf,
    Bubble,
    Rune,
    Circle,
    SoftCircle,
    Square,
    Diamond,
    Star,
    Glow
}

/// <summary>Particle blend modes for compositing onto the background.</summary>
public enum ParticleBlendMode
{
    Alpha,
    Additive,
    Screen
}
