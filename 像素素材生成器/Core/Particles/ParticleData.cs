using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles;

/// <summary>
/// Individual particle data structure. Designed as a struct for cache-friendly
/// iteration in tight loops. Each particle carries position, velocity, color,
/// size, rotation, and lifetime.
/// </summary>
public struct ParticleData
{
    /// <summary>Normalized position X [0, 1).</summary>
    public float X;
    /// <summary>Normalized position Y [0, 1).</summary>
    public float Y;

    /// <summary>Velocity X (in normalized units per second).</summary>
    public float VX;
    /// <summary>Velocity Y (in normalized units per second).</summary>
    public float VY;

    /// <summary>Remaining life [0, 1]. 0 = dead, 1 = just spawned.</summary>
    public float Life;
    /// <summary>Maximum life in seconds.</summary>
    public float MaxLife;

    /// <summary>Particle size in normalized units.</summary>
    public float Size;

    /// <summary>Rotation angle in radians.</summary>
    public float Rotation;
    /// <summary>Angular velocity (radians per second).</summary>
    public float AngularVelocity;

    /// <summary>Current color RGBA [0, 1].</summary>
    public float R, G, B, A;

    /// <summary>Start color (interpolation source).</summary>
    public float StartR, StartG, StartB, StartA;
    /// <summary>End color (interpolation target).</summary>
    public float EndR, EndG, EndB, EndA;

    /// <summary>Start size for size interpolation.</summary>
    public float StartSize;
    /// <summary>End size for size interpolation.</summary>
    public float EndSize;

    /// <summary>Whether this particle is currently active/alive.</summary>
    public bool Active;

    /// <summary>Marks generated trail afterimages so they are not recorded as new trail sources.</summary>
    public bool IsTrailGhost;

    /// <summary>Spawns a particle with default values.</summary>
    public static ParticleData Create(
        float x, float y,
        float vx, float vy,
        float life,
        float size,
        float rotation,
        float angularVelocity,
        float startR, float startG, float startB, float startA,
        float endR, float endG, float endB, float endA,
        float startSize, float endSize)
    {
        return new ParticleData
        {
            X = x, Y = y,
            VX = vx, VY = vy,
            Life = 1f,
            MaxLife = Math.Max(life, 0.001f),
            Size = size,
            Rotation = rotation,
            AngularVelocity = angularVelocity,
            R = startR, G = startG, B = startB, A = startA,
            StartR = startR, StartG = startG, StartB = startB, StartA = startA,
            EndR = endR, EndG = endG, EndB = endB, EndA = endA,
            StartSize = startSize, EndSize = endSize,
            Active = true
        };
    }

    /// <summary>Creates a dead (inactive) particle.</summary>
    public static ParticleData Dead() => new() { Active = false, Life = 0 };
}

/// <summary>
/// Particle buffer holding an array of particles. Supports pooling and reset.
/// Carries accumulated time for deterministic simulation.
/// </summary>
public sealed class ParticleBuffer : IDisposable
{
    private bool _disposed;
    private ParticleData[] _particles;

    /// <summary>Maximum particle capacity.</summary>
    public int Capacity { get; }

    /// <summary>Number of currently active particles.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Accumulated simulation time in seconds.</summary>
    public float Time { get; set; }

    /// <summary>Gets a span over all particles (active + dead slots).</summary>
    public Span<ParticleData> AsSpan() => _particles.AsSpan();

    /// <summary>Gets a span over active particles only.</summary>
    public ReadOnlySpan<ParticleData> ActiveSpan() =>
        _particles.AsSpan(0, ActiveCount);

    public ParticleBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _particles = new ParticleData[capacity];
        // Initialize all as dead
        for (var i = 0; i < capacity; i++)
            _particles[i] = ParticleData.Dead();
    }

    /// <summary>Reset all particles to dead state and reset time.</summary>
    public void Reset()
    {
        for (var i = 0; i < Capacity; i++)
            _particles[i] = ParticleData.Dead();
        ActiveCount = 0;
        Time = 0f;
    }

    /// <summary>Clone this buffer (deep copy of particle array).</summary>
    public ParticleBuffer Clone()
    {
        var clone = new ParticleBuffer(Capacity)
        {
            ActiveCount = ActiveCount,
            Time = Time
        };
        Array.Copy(_particles, clone._particles, Capacity);
        return clone;
    }

    /// <summary>
    /// Copy particle data from another buffer. Only copies active particles.
    /// </summary>
    public void CopyFrom(ParticleBuffer source)
    {
        var count = Math.Min(source.ActiveCount, Capacity);
        source._particles.AsSpan(0, count).CopyTo(_particles.AsSpan());
        ActiveCount = count;
        Time = source.Time;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ParticleBufferPool.Return(this);
    }
}

/// <summary>
/// Simple pool for reusing ParticleBuffer instances.
/// </summary>
public static class ParticleBufferPool
{
    private static readonly Dictionary<int, System.Collections.Concurrent.ConcurrentBag<ParticleBuffer>> _pools = new();

    /// <summary>Borrows a buffer with at least the given capacity from the pool.</summary>
    public static ParticleBuffer Borrow(int capacity)
    {
        // Round capacity up to nearest power of two for better pooling
        var rounded = 1;
        while (rounded < capacity) rounded <<= 1;

        lock (_pools)
        {
            if (_pools.TryGetValue(rounded, out var bag) && bag.TryTake(out var buffer))
                return buffer;
        }
        return new ParticleBuffer(rounded);
    }

    /// <summary>Returns a buffer to the pool.</summary>
    public static void Return(ParticleBuffer buffer)
    {
        var rounded = 1;
        while (rounded < buffer.Capacity) rounded <<= 1;

        buffer.Reset();

        lock (_pools)
        {
            if (!_pools.TryGetValue(rounded, out var bag))
            {
                bag = new System.Collections.Concurrent.ConcurrentBag<ParticleBuffer>();
                _pools[rounded] = bag;
            }
            if (bag.Count < 16)
                bag.Add(buffer);
        }
    }
}
