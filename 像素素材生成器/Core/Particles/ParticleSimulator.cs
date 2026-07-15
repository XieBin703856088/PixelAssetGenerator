using System;

namespace PixelAssetGenerator.Core.Particles;

/// <summary>
/// Updates particle positions, velocities, colors, sizes, and lifetimes
/// each frame. Applies global forces and per-particle interpolation.
/// </summary>
public sealed class ParticleSimulator
{
    private bool _firstUpdate = true;

    /// <summary>Gravity force in X and Y (normalized units/sec^2).</summary>
    public float GravityX { get; set; }
    public float GravityY { get; set; } = 0.5f;

    /// <summary>Velocity damping multiplier per second (0-1). Applied exponentially.</summary>
    public float Damping { get; set; } = 0.98f;

    /// <summary>If true, particles wrap around tile edges.</summary>
    public bool TilingMode { get; set; } = true;

    /// <summary>Wind force applied to X axis.</summary>
    public float WindX { get; set; }
    public float WindY { get; set; }

    /// <summary>Whether to interpolate color from StartColor to EndColor over lifetime.</summary>
    public bool ColorOverLifetime { get; set; } = true;

    /// <summary>Whether to interpolate size from StartSize to EndSize over lifetime.</summary>
    public bool SizeOverLifetime { get; set; } = true;

    /// <summary>Custom force fields applied each frame.</summary>
    private readonly System.Collections.Generic.List<IParticleForce> _forces = new();

    public void AddForce(IParticleForce force) => _forces.Add(force);
    public void ClearForces() => _forces.Clear();
    public System.Collections.Generic.IReadOnlyList<IParticleForce> Forces => _forces;

    /// <summary>
    /// Updates all active particles by deltaTime seconds.
    /// </summary>
    public void Update(float deltaTime, ParticleBuffer buffer)
    {
        if (deltaTime <= 0f) return;

        // On first update, initialize first-frame interpolation for burst particles
        if (_firstUpdate)
        {
            _firstUpdate = false;
            // No special action needed — burst particles get full Life on creation
        }

        var count = buffer.ActiveCount;
        if (count == 0) return;

        var span = buffer.AsSpan();

        // Apply damping factor per second
        var dampingPerFrame = MathF.Pow(Damping, deltaTime);

        for (var i = 0; i < count; i++)
        {
            ref var p = ref span[i];
            if (!p.Active) continue;

            // Apply gravity
            p.VX += GravityX * deltaTime;
            p.VY += GravityY * deltaTime;

            // Apply wind
            p.VX += WindX * deltaTime;
            p.VY += WindY * deltaTime;

            // Apply custom forces
            foreach (var force in _forces)
                force.Apply(deltaTime, ref p);

            // Apply damping
            p.VX *= dampingPerFrame;
            p.VY *= dampingPerFrame;

            // Update position
            p.X += p.VX * deltaTime;
            p.Y += p.VY * deltaTime;

            // Tiling or kill
            if (TilingMode)
            {
                if (p.X < 0f) p.X += 1f;
                else if (p.X >= 1f) p.X -= 1f;
                if (p.Y < 0f) p.Y += 1f;
                else if (p.Y >= 1f) p.Y -= 1f;
            }
            else
            {
                if (p.X < 0f || p.X >= 1f || p.Y < 0f || p.Y >= 1f)
                {
                    p.Active = false;
                    continue;
                }
            }

            // Rotation
            p.Rotation += p.AngularVelocity * deltaTime;

            // Decrease life
            p.Life -= deltaTime / p.MaxLife;

            if (p.Life <= 0f)
            {
                p.Active = false;
                continue;
            }

            // Interpolate color
            if (ColorOverLifetime)
            {
                var t = 1f - p.Life; // 0 = just spawned, 1 = dying
                p.R = Lerp(p.StartR, p.EndR, t);
                p.G = Lerp(p.StartG, p.EndG, t);
                p.B = Lerp(p.StartB, p.EndB, t);
                p.A = Lerp(p.StartA, p.EndA, t);
            }

            // Interpolate size
            if (SizeOverLifetime)
            {
                var t = 1f - p.Life;
                p.Size = Lerp(p.StartSize, p.EndSize, t);
            }
        }

        // Compact: move dead particles to end of active span
        buffer.ActiveCount = Compact(span, count);

        buffer.Time += deltaTime;
    }

    /// <summary>
    /// Compacts particles: moves dead entries to the end to keep active particles
    /// at the front of the array. This keeps iteration efficient.
    /// </summary>
    private static int Compact(Span<ParticleData> span, int count)
    {
        var writeIdx = 0;
        for (var readIdx = 0; readIdx < count; readIdx++)
        {
            if (span[readIdx].Active)
            {
                if (writeIdx != readIdx)
                    span[writeIdx] = span[readIdx];
                writeIdx++;
            }
        }
        // Fill remaining slots with dead particles
        for (var i = writeIdx; i < count; i++)
            span[i] = ParticleData.Dead();
        return writeIdx;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}

/// <summary>
/// Interface for custom force fields applied to particles.
/// </summary>
public interface IParticleForce
{
    void Apply(float deltaTime, ref ParticleData particle);
}

/// <summary>Uniform acceleration used for gravity and directional wind nodes.</summary>
public sealed class UniformForce : IParticleForce
{
    public float AccelerationX { get; set; }
    public float AccelerationY { get; set; }

    public void Apply(float deltaTime, ref ParticleData particle)
    {
        particle.VX += AccelerationX * deltaTime;
        particle.VY += AccelerationY * deltaTime;
    }
}

/// <summary>
/// Attractor/repeller force at a point.
/// </summary>
public sealed class PointForce : IParticleForce
{
    public float CenterX { get; set; } = 0.5f;
    public float CenterY { get; set; } = 0.5f;
    public float Strength { get; set; } = 1f;
    public float Radius { get; set; } = 0.3f;
    public ForceFalloff Falloff { get; set; } = ForceFalloff.InverseSquare;

    public void Apply(float deltaTime, ref ParticleData particle)
    {
        var dx = CenterX - particle.X;
        var dy = CenterY - particle.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.0001f || dist > Radius) return;

        var normalizedDx = dx / dist;
        var normalizedDy = dy / dist;

        var falloffFactor = Falloff switch
        {
            ForceFalloff.Constant => 1f,
            ForceFalloff.Linear => 1f - dist / Radius,
            ForceFalloff.InverseSquare => 1f / (dist * dist + 0.01f),
            _ => 1f
        };

        var force = Strength * falloffFactor * deltaTime;
        particle.VX += normalizedDx * force;
        particle.VY += normalizedDy * force;
    }
}

/// <summary>
/// Vortex/twirl force around a point.
/// </summary>
public sealed class VortexForce : IParticleForce
{
    public float CenterX { get; set; } = 0.5f;
    public float CenterY { get; set; } = 0.5f;
    public float Strength { get; set; } = 1f;
    public float Radius { get; set; } = 0.3f;

    public void Apply(float deltaTime, ref ParticleData particle)
    {
        var dx = particle.X - CenterX;
        var dy = particle.Y - CenterY;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.0001f || dist > Radius) return;

        // Perpendicular force (tangent to radius)
        var force = Strength * (1f - dist / Radius) * deltaTime;
        particle.VX += -dy / dist * force;
        particle.VY += dx / dist * force;
    }
}

/// <summary>
/// Turbulence/noise-based force for chaotic motion.
/// </summary>
public sealed class TurbulenceForce : IParticleForce
{
    public float Strength { get; set; } = 0.5f;
    public float Frequency { get; set; } = 2f;
    public int Seed { get; set; } = 42;

    public void Apply(float deltaTime, ref ParticleData particle)
    {
        // Simple hash-based pseudo-random force that varies with position
        var hash1 = Hash(particle.X * Frequency * 255, particle.Y * Frequency * 255, Seed);
        var hash2 = Hash(particle.X * Frequency * 255 + 100, particle.Y * Frequency * 255 + 100, Seed + 1);

        var forceX = (hash1 * 2f - 1f) * Strength * deltaTime;
        var forceY = (hash2 * 2f - 1f) * Strength * deltaTime;

        particle.VX += forceX;
        particle.VY += forceY;
    }

    private static float Hash(float x, float y, int seed)
    {
        unchecked
        {
            var h = (int)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue;
        }
    }
}

/// <summary>
/// Noise-based force field for organic, fluid-like particle motion.
/// Uses Perlin-like gradient noise to create smooth, non-repeating force
/// patterns that vary by position and time. Produces smoke, cloud, nebula,
/// and underwater-like motion.
/// </summary>
public sealed class NoiseMotionForce : IParticleForce
{
    /// <summary>Noise type: perlin, value, or cellular.</summary>
    public string NoiseType { get; set; } = "perlin";

    /// <summary>Base frequency of the noise field.</summary>
    public float Frequency { get; set; } = 2f;

    /// <summary>Force strength multiplier.</summary>
    public float Strength { get; set; } = 0.5f;

    /// <summary>How fast the noise field evolves over time.</summary>
    public float TimeScale { get; set; } = 0.3f;

    /// <summary>Number of fractal octaves.</summary>
    public int Octaves { get; set; } = 3;

    /// <summary>Seed for deterministic noise.</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Anisotropy: ratio of Y-to-X force strength.</summary>
    public float Anisotropy { get; set; } = 1f;

    /// <summary>Current global time (set externally each frame).</summary>
    public float GlobalTime { get; set; }

    public void Apply(float deltaTime, ref ParticleData particle)
    {
        var nx = particle.X * Frequency * 4f;
        var ny = particle.Y * Frequency * 4f;
        var nt = GlobalTime * TimeScale;

        // Sample noise at two points slightly offset for gradient approximation
        var eps = 0.01f;
        var v00 = SampleNoise(nx, ny, nt);
        var v10 = SampleNoise(nx + eps, ny, nt);
        var v01 = SampleNoise(nx, ny + eps, nt);

        // Finite difference gradient
        var gx = (v10 - v00) / eps;
        var gy = (v01 - v00) / eps;

        var force = Strength * deltaTime;
        particle.VX += gx * force;
        particle.VY += gy * force * Anisotropy;
    }

    private float SampleNoise(float x, float y, float t)
    {
        // Combine position and time into 3D noise lookup
        // Using simple multi-octave value noise here
        var value = 0f;
        var max = 0f;
        var amp = 1f;
        var freq = 1f;

        for (var o = 0; o < Octaves; o++)
        {
            var px = x * freq;
            var py = y * freq;
            var pt = t * freq * 0.5f;
            value += Hash3D(px, py, pt, Seed + o * 137) * amp;
            max += amp;
            amp *= 0.5f;
            freq *= 2f;
        }

        return max > 0f ? value / max : 0f;
    }

    private static float Hash3D(float x, float y, float t, int seed)
    {
        // Trilinear interpolation of 3D hash values for smooth noise
        var ix = (int)MathF.Floor(x);
        var iy = (int)MathF.Floor(y);
        var it = (int)MathF.Floor(t);
        var fx = x - ix;
        var fy = y - iy;
        var ft = t - it;

        var sx = fx * fx * (3f - 2f * fx);
        var sy = fy * fy * (3f - 2f * fy);
        var st = ft * ft * (3f - 2f * ft);

        // 8 corners of the cube
        var v000 = HashUnit(ix, iy, it, seed);
        var v100 = HashUnit(ix + 1, iy, it, seed);
        var v010 = HashUnit(ix, iy + 1, it, seed);
        var v110 = HashUnit(ix + 1, iy + 1, it, seed);
        var v001 = HashUnit(ix, iy, it + 1, seed);
        var v101 = HashUnit(ix + 1, iy, it + 1, seed);
        var v011 = HashUnit(ix, iy + 1, it + 1, seed);
        var v111 = HashUnit(ix + 1, iy + 1, it + 1, seed);

        // Trilerp
        var l0 = v000 + (v100 - v000) * sx;
        var l1 = v010 + (v110 - v010) * sx;
        var l2 = v001 + (v101 - v001) * sx;
        var l3 = v011 + (v111 - v011) * sx;
        var l4 = l0 + (l1 - l0) * sy;
        var l5 = l2 + (l3 - l2) * sy;

        return l4 + (l5 - l4) * st;
    }

    private static float HashUnit(int x, int y, int t, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + t * 1274126177 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue * 2f - 1f;
        }
    }
}

/// <summary>Falloff function type for force fields.</summary>
public enum ForceFalloff
{
    Constant,
    Linear,
    InverseSquare
}
