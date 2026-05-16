using System;

namespace PixelAssetGenerator.Core.Particles;

/// <summary>
/// Emits new particles into a ParticleBuffer based on emission rate, bursts,
/// and configurable emission shape.
/// </summary>
public sealed class ParticleEmitter
{
    private float _accumulator;
    private readonly Random _rng;

    /// <summary>Emission rate: particles per second.</summary>
    public float EmissionRate { get; set; } = 50f;

    /// <summary>Number of particles to emit in a single burst.</summary>
    public int BurstCount { get; set; } = 20;

    /// <summary>Particle lifespan range in seconds.</summary>
    public float LifeMin { get; set; } = 1f;
    public float LifeMax { get; set; } = 3f;

    /// <summary>Speed range in normalized units per second.</summary>
    public float SpeedMin { get; set; } = 0.1f;
    public float SpeedMax { get; set; } = 0.5f;

    /// <summary>Base emission angle in degrees (0=right, 90=up, 180=left, 270=down).</summary>
    public float Angle { get; set; } = 270f;

    /// <summary>Angular spread in degrees (0 = straight beam, 180 = full hemisphere).</summary>
    public float Spread { get; set; } = 30f;

    /// <summary>Particle size range in normalized units.</summary>
    public float SizeMin { get; set; } = 0.02f;
    public float SizeMax { get; set; } = 0.08f;

    /// <summary>Start color (interpolated to EndColor over particle lifetime).</summary>
    public float StartR { get; set; } = 1f;
    public float StartG { get; set; } = 1f;
    public float StartB { get; set; } = 1f;
    public float StartA { get; set; } = 1f;

    /// <summary>End color at end of particle life.</summary>
    public float EndR { get; set; } = 0f;
    public float EndG { get; set; } = 0f;
    public float EndB { get; set; } = 0f;
    public float EndA { get; set; } = 0f;

    /// <summary>Size at end of particle life (0 = shrink to nothing, > = grow).</summary>
    public float EndSizeMultiplier { get; set; } = 0.1f;

    /// <summary>Emission shape type.</summary>
    public EmissionShape Shape { get; set; } = EmissionShape.Point;

    /// <summary>For non-point shapes: width/height of the emission area.</summary>
    public float ShapeWidth { get; set; } = 0.1f;
    public float ShapeHeight { get; set; } = 0.1f;

    /// <summary>If true, emit all burst particles at once on first update.</summary>
    public bool OneShot { get; set; }

    /// <summary>Prevents further emission after one-shot burst.</summary>
    public bool OneShotCompleted { get; set; }

    /// <summary>Random rotation range in radians (-range/2 to +range/2).</summary>
    public float RotationRandom { get; set; }

    /// <summary>Angular velocity range (-range/2 to +range/2).</summary>
    public float AngularVelocityRandom { get; set; }

    public ParticleEmitter()
    {
        _rng = new Random();
    }

    public ParticleEmitter(int seed)
    {
        _rng = new Random(seed);
    }

    /// <summary>
    /// Emits particles over time. Call once per frame.
    /// </summary>
    /// <param name="deltaTime">Seconds since last frame.</param>
    /// <param name="particles">Particle buffer to emit into.</param>
    /// <returns>Number of particles emitted this frame.</returns>
    public int Emit(float deltaTime, ParticleBuffer particles)
    {
        if (OneShotCompleted) return 0;
        if (OneShot)
        {
            OneShotCompleted = true;
            return BurstEmit(particles);
        }

        _accumulator += deltaTime * EmissionRate;
        var count = (int)_accumulator;
        _accumulator -= count;

        var emitted = 0;
        var span = particles.AsSpan();

        for (var i = 0; i < count; i++)
        {
            if (!TryFindDeadSlot(span, out var slot))
                break;

            span[slot] = CreateParticle();
            emitted++;
            particles.ActiveCount = Math.Max(particles.ActiveCount, slot + 1);
        }

        return emitted;
    }

    /// <summary>
    /// Emits a burst of particles immediately.
    /// </summary>
    public int BurstEmit(ParticleBuffer particles)
    {
        var emitted = 0;
        var span = particles.AsSpan();

        for (var i = 0; i < BurstCount; i++)
        {
            if (!TryFindDeadSlot(span, out var slot))
                break;

            span[slot] = CreateParticle();
            emitted++;
            particles.ActiveCount = Math.Max(particles.ActiveCount, slot + 1);
        }

        return emitted;
    }

    private bool TryFindDeadSlot(Span<ParticleData> span, out int slot)
    {
        // Search from current active count backward (dead particles tend to be at end)
        for (var i = 0; i < span.Length; i++)
        {
            if (!span[i].Active)
            {
                slot = i;
                return true;
            }
        }
        slot = -1;
        return false;
    }

    private ParticleData CreateParticle()
    {
        // Position based on emission shape
        var (px, py) = Shape switch
        {
            EmissionShape.Point => (0.5f, 0.5f),
            EmissionShape.Line => (0.5f + (float)_rng.NextDouble() * ShapeWidth - ShapeWidth * 0.5f, 0.5f),
            EmissionShape.Rectangle => (
                0.5f + (float)_rng.NextDouble() * ShapeWidth - ShapeWidth * 0.5f,
                0.5f + (float)_rng.NextDouble() * ShapeHeight - ShapeHeight * 0.5f),
            EmissionShape.Circle => RandomInCircle(0.5f, 0.5f, Math.Min(ShapeWidth, ShapeHeight) * 0.5f),
            EmissionShape.Ring => RandomOnRing(0.5f, 0.5f, Math.Min(ShapeWidth, ShapeHeight) * 0.5f),
            _ => (0.5f, 0.5f)
        };

        // Speed and direction
        var speed = SpeedMin + (float)_rng.NextDouble() * (SpeedMax - SpeedMin);
        var angleRad = (Angle + ((float)_rng.NextDouble() - 0.5f) * Spread) * (MathF.PI / 180f);
        var vx = MathF.Cos(angleRad) * speed;
        var vy = -MathF.Sin(angleRad) * speed; // Y up in normalized coords

        // Lifespan
        var life = LifeMin + (float)_rng.NextDouble() * (LifeMax - LifeMin);

        // Size
        var startSize = SizeMin + (float)_rng.NextDouble() * (SizeMax - SizeMin);
        var endSize = startSize * EndSizeMultiplier;

        // Rotation
        var rotation = ((float)_rng.NextDouble() - 0.5f) * RotationRandom;
        var angularVelocity = ((float)_rng.NextDouble() - 0.5f) * AngularVelocityRandom;

        return ParticleData.Create(
            px, py, vx, vy, life,
            startSize, rotation, angularVelocity,
            StartR, StartG, StartB, StartA,
            EndR, EndG, EndB, EndA,
            startSize, endSize
        );
    }

    private (float x, float y) RandomInCircle(float cx, float cy, float radius)
    {
        var angle = (float)_rng.NextDouble() * MathF.Tau;
        var r = MathF.Sqrt((float)_rng.NextDouble()) * radius;
        return (cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle));
    }

    private (float x, float y) RandomOnRing(float cx, float cy, float radius)
    {
        var angle = (float)_rng.NextDouble() * MathF.Tau;
        return (cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle));
    }

    /// <summary>
    /// Emits particles at specified positions from a lookup table.
    /// Used when a mask input controls emission positions.
    /// </summary>
    public int EmitFromPositions(ParticleBuffer particles, IReadOnlyList<(float X, float Y)> positions)
    {
        if (positions == null || positions.Count == 0) return 0;

        var emitted = 0;
        var span = particles.AsSpan();

        for (var i = 0; i < positions.Count; i++)
        {
            if (!TryFindDeadSlot(span, out var slot))
                break;

            var (px, py) = positions[i];

            var speed = SpeedMin + (float)_rng.NextDouble() * (SpeedMax - SpeedMin);
            var angleRad = (Angle + ((float)_rng.NextDouble() - 0.5f) * Spread) * (MathF.PI / 180f);
            var vx = MathF.Cos(angleRad) * speed;
            var vy = -MathF.Sin(angleRad) * speed;
            var life = LifeMin + (float)_rng.NextDouble() * (LifeMax - LifeMin);
            var startSize = SizeMin + (float)_rng.NextDouble() * (SizeMax - SizeMin);
            var endSize = startSize * EndSizeMultiplier;
            var rotation = ((float)_rng.NextDouble() - 0.5f) * RotationRandom;
            var angularVelocity = ((float)_rng.NextDouble() - 0.5f) * AngularVelocityRandom;

            span[slot] = ParticleData.Create(
                px, py, vx, vy, life,
                startSize, rotation, angularVelocity,
                StartR, StartG, StartB, StartA,
                EndR, EndG, EndB, EndA,
                startSize, endSize
            );
            emitted++;
            particles.ActiveCount = Math.Max(particles.ActiveCount, slot + 1);
        }

        return emitted;
    }
}

/// <summary>Emission shape geometry.</summary>
public enum EmissionShape
{
    Point,
    Line,
    Rectangle,
    Circle,
    Ring
}
