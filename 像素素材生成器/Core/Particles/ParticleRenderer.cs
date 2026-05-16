using System;

namespace PixelAssetGenerator.Core.Particles;

/// <summary>
/// Renders particles into a PixelBuffer with configurable texture,
/// blend mode, and soft edges.
/// </summary>
public sealed class ParticleRenderer
{
    /// <summary>Particle sprite shape.</summary>
    public ParticleTextureType TextureType { get; set; } = ParticleTextureType.SoftCircle;

    /// <summary>How particles are blended with the background.</summary>
    public ParticleBlendMode BlendMode { get; set; } = ParticleBlendMode.Alpha;

    /// <summary>Whether to use soft (anti-aliased) edges.</summary>
    public bool SoftEdges { get; set; } = true;

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
            var halfSize = pixelSize * 0.5f;
            var centerX = p.X * w;
            var centerY = p.Y * h;
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
                    var srcA = alpha * p.A * globalA;

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
                            break;

                        case ParticleBlendMode.Screen:
                            data[idx] = 1f - (1f - data[idx]) * (1f - srcR * srcA);
                            data[idx + 1] = 1f - (1f - data[idx + 1]) * (1f - srcG * srcA);
                            data[idx + 2] = 1f - (1f - data[idx + 2]) * (1f - srcB * srcA);
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
