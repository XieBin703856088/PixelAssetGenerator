using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Physics;

/// <summary>
/// 2D physics world holding bodies and applying forces, collisions, and constraints.
/// Uses semi-implicit Euler integration (Verlet-style position update).
/// </summary>
public sealed class PhysicsWorld
{
    /// <summary>Global gravity vector.</summary>
    public float GravityX { get; set; }
    public float GravityY { get; set; } = 0.5f;

    /// <summary>Number of physics substeps per update (higher = more stable).</summary>
    public int Substeps { get; set; } = 4;

    /// <summary>Whether to wrap bodies around tile edges.</summary>
    public bool WrapEdges { get; set; } = true;

    /// <summary>Global velocity damping per second.</summary>
    public float Damping { get; set; } = 0.99f;

    /// <summary>World boundaries (normalized 0-1).</summary>
    public float MinX, MinY, MaxX = 1f, MaxY = 1f;

    /// <summary>Enable collisions between bodies.</summary>
    public bool EnableCollisions { get; set; } = true;
    public bool EnableBounds { get; set; } = true;

    private readonly List<PhysicsBody> _bodies = new();
    private readonly List<IConstraint> _constraints = new();

    public IReadOnlyList<PhysicsBody> Bodies => _bodies;
    public List<IConstraint> Constraints => _constraints;

    public void AddBody(PhysicsBody body) => _bodies.Add(body);
    public void AddBodies(IEnumerable<PhysicsBody> bodies) => _bodies.AddRange(bodies);
    public void RemoveBody(PhysicsBody body) => _bodies.Remove(body);
    public void Clear()
    {
        _bodies.Clear();
        _constraints.Clear();
    }

    /// <summary>
    /// Steps the physics simulation forward by deltaTime seconds.
    /// Uses sub-stepping for stability.
    /// </summary>
    public void Step(float deltaTime)
    {
        if (_bodies.Count == 0) return;

        var subDt = deltaTime / Substeps;
        var dampingPerSub = MathF.Pow(Damping, subDt);

        for (var step = 0; step < Substeps; step++)
        {
            // Integrate
            foreach (var body in _bodies)
            {
                if (!body.IsEnabled || body.IsStatic) continue;

                // Save previous position
                body.PrevX = body.X;
                body.PrevY = body.Y;

                // Apply gravity
                body.VX += GravityX * subDt;
                body.VY += GravityY * subDt;

                // Apply damping
                body.VX *= dampingPerSub;
                body.VY *= dampingPerSub;

                // Update position (semi-implicit Euler)
                body.X += body.VX * subDt;
                body.Y += body.VY * subDt;
            }

            // Collisions
            if (EnableCollisions)
            {
                ResolveCollisions();
            }

            // Boundaries
            if (EnableBounds)
                ConstrainBounds();

            // Constraints
            for (var ci = 0; ci < _constraints.Count; ci++)
            {
                _constraints[ci].Apply(subDt);
            }
        }
    }

    /// <summary>
    /// Resolves collisions between all body pairs using brute-force O(n^2).
    /// Simple and fine for particle counts (< 1000 active bodies).
    /// </summary>
    private void ResolveCollisions()
    {
        for (var i = 0; i < _bodies.Count; i++)
        {
            if (!_bodies[i].IsEnabled) continue;
            for (var j = i + 1; j < _bodies.Count; j++)
            {
                var a = _bodies[i];
                var b = _bodies[j];
                if (!b.IsEnabled) continue;
                if (a.IsStatic && b.IsStatic) continue;

                if (CollisionDetection.CircleCircle(a, b, out var contact))
                {
                    ResolveContact(a, b, contact);
                }
                else if (CollisionDetection.CircleRect(a, b, out contact))
                {
                    ResolveContact(a, b, contact);
                }
                else if (CollisionDetection.RectRect(a, b, out contact))
                {
                    ResolveContact(a, b, contact);
                }
            }
        }
    }

    private static void ResolveContact(PhysicsBody a, PhysicsBody b, Contact contact)
    {
        var totalInvMass = a.InvMass + b.InvMass;
        if (totalInvMass <= 0) return;

        // Position correction with clamping to prevent overshoot
        var correction = Math.Min(contact.Penetration / totalInvMass * 0.8f, contact.Penetration);
        a.X += contact.NormalX * correction * a.InvMass;
        a.Y += contact.NormalY * correction * a.InvMass;
        b.X -= contact.NormalX * correction * b.InvMass;
        b.Y -= contact.NormalY * correction * b.InvMass;

        // Velocity resolution
        var relVx = a.VX - b.VX;
        var relVy = a.VY - b.VY;
        var relVelNormal = relVx * contact.NormalX + relVy * contact.NormalY;
        if (relVelNormal > 0) return; // Moving apart

        var restitution = Math.Min(a.Restitution, b.Restitution);
        var j = -(1f + restitution) * relVelNormal / totalInvMass;

        a.VX += j * contact.NormalX * a.InvMass;
        a.VY += j * contact.NormalY * a.InvMass;
        b.VX -= j * contact.NormalX * b.InvMass;
        b.VY -= j * contact.NormalY * b.InvMass;

        // Friction
        var tangentX = relVx - relVelNormal * contact.NormalX;
        var tangentY = relVy - relVelNormal * contact.NormalY;
        var tangentLen = MathF.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (tangentLen > 0.0001f)
        {
            var friction = Math.Min(a.Friction, b.Friction);
            var jt = -Math.Min(tangentLen / totalInvMass, friction * Math.Abs(j));
            var ftx = tangentX / tangentLen * jt;
            var fty = tangentY / tangentLen * jt;
            a.VX += ftx * a.InvMass;
            a.VY += fty * a.InvMass;
            b.VX -= ftx * b.InvMass;
            b.VY -= fty * b.InvMass;
        }
    }

    /// <summary>
    /// Constrains bodies to world boundaries.
    /// </summary>
    private void ConstrainBounds()
    {
        var worldW = MaxX - MinX;
        var worldH = MaxY - MinY;

        foreach (var body in _bodies)
        {
            if (!body.IsEnabled || body.IsStatic) continue;
            if (WrapEdges)
            {
                if (body.X < MinX) { body.X += worldW; body.PrevX += worldW; }
                else if (body.X > MaxX) { body.X -= worldW; body.PrevX -= worldW; }
                if (body.Y < MinY) { body.Y += worldH; body.PrevY += worldH; }
                else if (body.Y > MaxY) { body.Y -= worldH; body.PrevY -= worldH; }
            }
            else
            {
                float marginX, marginY;
                if (body.Shape == BodyShape.Circle)
                {
                    marginX = body.Radius;
                    marginY = body.Radius;
                }
                else
                {
                    marginX = body.Width * 0.5f;
                    marginY = body.Height * 0.5f;
                }

                if (body.X - marginX < MinX) { body.X = MinX + marginX; body.VX = -body.VX * body.Restitution; }
                else if (body.X + marginX > MaxX) { body.X = MaxX - marginX; body.VX = -body.VX * body.Restitution; }
                if (body.Y - marginY < MinY) { body.Y = MinY + marginY; body.VY = -body.VY * body.Restitution; }
                else if (body.Y + marginY > MaxY) { body.Y = MaxY - marginY; body.VY = -body.VY * body.Restitution; }
            }
        }
    }
}
