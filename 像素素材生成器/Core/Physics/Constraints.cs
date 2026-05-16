using System;

namespace PixelAssetGenerator.Core.Physics;

/// <summary>
/// A constraint that limits the relative motion between two rigid bodies.
/// Applied during the physics substep after collision resolution.
/// </summary>
public interface IConstraint
{
    /// <summary>
    /// Applies corrective impulses/forces for this substep.
    /// </summary>
    void Apply(float subDt);

    /// <summary>
    /// Whether the constraint is currently satisfied (within tolerance).
    /// </summary>
    bool IsSatisfied { get; }
}

/// <summary>
/// Keeps two bodies at a fixed distance from each other, like a spring.
/// </summary>
public sealed class DistanceConstraint : IConstraint
{
    public PhysicsBody BodyA { get; }
    public PhysicsBody BodyB { get; }
    public float RestLength { get; set; }
    public float Stiffness { get; set; } = 1f;
    public float Damping { get; set; } = 0.5f;

    public DistanceConstraint(PhysicsBody bodyA, PhysicsBody bodyB, float restLength)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        RestLength = restLength;
    }

    public bool IsSatisfied
    {
        get
        {
            var dx = BodyB.X - BodyA.X;
            var dy = BodyB.Y - BodyA.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            return Math.Abs(dist - RestLength) < 0.0001f;
        }
    }

    public void Apply(float subDt)
    {
        var dx = BodyB.X - BodyA.X;
        var dy = BodyB.Y - BodyA.Y;
        var distSq = dx * dx + dy * dy;
        if (distSq < 1e-10f) return;

        var dist = MathF.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;

        // Spring force: F = stiffness * (current - rest)
        var displacement = dist - RestLength;

        // Relative velocity along constraint normal
        var relVx = BodyA.VX - BodyB.VX;
        var relVy = BodyA.VY - BodyB.VY;
        var relVelNormal = relVx * nx + relVy * ny;

        // Damping force opposing relative motion
        var dampingForce = relVelNormal * Damping;

        // Total correction (hooke's law with damping)
        var force = -(displacement * Stiffness + dampingForce) * subDt;

        var totalInvMass = BodyA.InvMass + BodyB.InvMass;
        if (totalInvMass <= 0) return;

        BodyA.X += nx * force * BodyA.InvMass;
        BodyA.Y += ny * force * BodyA.InvMass;
        BodyB.X -= nx * force * BodyB.InvMass;
        BodyB.Y -= ny * force * BodyB.InvMass;

        // Velocity correction
        BodyA.VX += nx * force * BodyA.InvMass;
        BodyA.VY += ny * force * BodyA.InvMass;
        BodyB.VX -= nx * force * BodyB.InvMass;
        BodyB.VY -= ny * force * BodyB.InvMass;
    }
}

/// <summary>
/// Restricts a body to angular motion around a fixed anchor point,
/// clamped to a range [MinAngle, MaxAngle].
/// </summary>
public sealed class HingeConstraint : IConstraint
{
    public PhysicsBody Body { get; }
    public float AnchorX { get; set; }
    public float AnchorY { get; set; }
    public float MinAngle { get; set; }
    public float MaxAngle { get; set; }

    public HingeConstraint(PhysicsBody body, float anchorX, float anchorY)
    {
        Body = body;
        AnchorX = anchorX;
        AnchorY = anchorY;
    }

    public bool IsSatisfied
    {
        get
        {
            var angle = GetAngle();
            return angle >= MinAngle && angle <= MaxAngle;
        }
    }

    public void Apply(float subDt)
    {
        var angle = GetAngle();
        if (angle >= MinAngle && angle <= MaxAngle)
            return;

        // Clamp to nearest valid angle
        var target = angle < MinAngle ? MinAngle : MaxAngle;
        var correction = target - angle;

        var dx = Body.X - AnchorX;
        var dy = Body.Y - AnchorY;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-10f) return;

        // Rotate position around anchor to target angle
        var cos = MathF.Cos(correction);
        var sin = MathF.Sin(correction);
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;

        Body.X = AnchorX + rx;
        Body.Y = AnchorY + ry;

        // Zero velocity component perpendicular to the arm
        var armNx = -dy / dist;
        var armNy = dx / dist;
        var velAlongArm = Body.VX * armNx + Body.VY * armNy;
        Body.VX = armNx * velAlongArm;
        Body.VY = armNy * velAlongArm;
    }

    private float GetAngle()
    {
        return MathF.Atan2(Body.Y - AnchorY, Body.X - AnchorX);
    }
}

/// <summary>
/// Prevents two bodies from moving farther apart than MaxLength.
/// The constraint only acts when the distance exceeds MaxLength (pulls but does not push).
/// </summary>
public sealed class RopeConstraint : IConstraint
{
    public PhysicsBody BodyA { get; }
    public PhysicsBody BodyB { get; }
    public float MaxLength { get; set; }

    public RopeConstraint(PhysicsBody bodyA, PhysicsBody bodyB, float maxLength)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        MaxLength = maxLength;
    }

    public bool IsSatisfied
    {
        get
        {
            var dx = BodyB.X - BodyA.X;
            var dy = BodyB.Y - BodyA.Y;
            var distSq = dx * dx + dy * dy;
            return distSq <= MaxLength * MaxLength;
        }
    }

    public void Apply(float subDt)
    {
        var dx = BodyB.X - BodyA.X;
        var dy = BodyB.Y - BodyA.Y;
        var distSq = dx * dx + dy * dy;

        if (distSq <= MaxLength * MaxLength)
            return; // Within limit, no action

        var dist = MathF.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;

        var totalInvMass = BodyA.InvMass + BodyB.InvMass;
        if (totalInvMass <= 0) return;

        // Pull both bodies back to satisfy max length
        var correction = (dist - MaxLength) / totalInvMass;

        BodyA.X += nx * correction * BodyA.InvMass;
        BodyA.Y += ny * correction * BodyA.InvMass;
        BodyB.X -= nx * correction * BodyB.InvMass;
        BodyB.Y -= ny * correction * BodyB.InvMass;

        // Clamp velocity along rope direction
        var relVx = BodyA.VX - BodyB.VX;
        var relVy = BodyA.VY - BodyB.VY;
        var relVelNormal = relVx * nx + relVy * ny;
        if (relVelNormal > 0)
        {
            // Bodies moving apart — dampen the separating velocity
            var impulse = relVelNormal / totalInvMass;
            BodyA.VX -= nx * impulse * BodyA.InvMass;
            BodyA.VY -= ny * impulse * BodyA.InvMass;
            BodyB.VX += nx * impulse * BodyB.InvMass;
            BodyB.VY += ny * impulse * BodyB.InvMass;
        }
    }
}
