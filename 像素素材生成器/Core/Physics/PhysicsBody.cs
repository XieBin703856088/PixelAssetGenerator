using System;

namespace PixelAssetGenerator.Core.Physics;

/// <summary>
/// Physics body: position, velocity, shape, and material properties.
/// Designed for simple 2D particle physics.
/// </summary>
public sealed class PhysicsBody
{
    // ── Position & movement ──
    public float X, Y;
    public float PrevX, PrevY;
    public float VX, VY;
    public float Mass = 1f;
    public float InvMass => Mass <= 0 ? 0 : 1f / Mass;
    public bool IsStatic;

    // ── Shape ──
    public BodyShape Shape = BodyShape.Circle;
    public float Radius = 0.02f;
    public float Width = 0.04f;
    public float Height = 0.04f;

    // ── Material ──
    public float Restitution = 0.5f;
    public float Friction = 0.3f;

    // ── User data ──
    public int ParticleIndex = -1; // Link to particle in ParticleBuffer

    public PhysicsBody() { }

    public PhysicsBody(float x, float y, float radius)
    {
        X = x; Y = y;
        Radius = radius;
        Shape = BodyShape.Circle;
    }

    public PhysicsBody(float x, float y, float width, float height)
    {
        X = x; Y = y;
        Width = width; Height = height;
        Shape = BodyShape.Rectangle;
    }

    /// <summary>Apply an impulse (instant velocity change).</summary>
    public void ApplyImpulse(float ix, float iy)
    {
        if (IsStatic) return;
        VX += ix * InvMass;
        VY += iy * InvMass;
    }

    /// <summary>Apply a continuous force (acceleration per frame).</summary>
    public void ApplyForce(float fx, float fy, float dt)
    {
        if (IsStatic) return;
        VX += fx * InvMass * dt;
        VY += fy * InvMass * dt;
    }

    /// <summary>Reset position and velocity.</summary>
    public void Reset()
    {
        X = Y = 0;
        PrevX = PrevY = 0;
        VX = VY = 0;
    }
}

public enum BodyShape
{
    Circle,
    Rectangle
}
