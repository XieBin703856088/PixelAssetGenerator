using System;

namespace PixelAssetGenerator.Core.Physics;

/// <summary>
/// Contact information from collision detection.
/// </summary>
public readonly record struct Contact(
    PhysicsBody A,
    PhysicsBody B,
    float Penetration,
    float NormalX,
    float NormalY);

/// <summary>
/// Simple 2D collision detection for circles and rectangles.
/// All positions are in normalized coordinates [0, 1].
/// </summary>
public static class CollisionDetection
{
    /// <summary>Circle vs Circle collision test.</summary>
    public static bool CircleCircle(PhysicsBody a, PhysicsBody b, out Contact contact)
    {
        contact = default;

        if (a.Shape != BodyShape.Circle || b.Shape != BodyShape.Circle)
            return false;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var distSq = dx * dx + dy * dy;
        var combinedRadius = a.Radius + b.Radius;

        if (distSq >= combinedRadius * combinedRadius)
            return false;

        // Handle degenerate case where both circles are at the same position
        if (distSq < 1e-10f)
        {
            contact = new Contact(a, b, combinedRadius, 1f, 0f);
            return true;
        }

        var dist = MathF.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;
        var penetration = combinedRadius - dist;

        contact = new Contact(a, b, penetration, nx, ny);
        return true;
    }

    /// <summary>Circle vs Rectangle collision test. Handles both orderings.</summary>
    public static bool CircleRect(PhysicsBody a, PhysicsBody b, out Contact contact)
    {
        contact = default;

        // Determine which is circle and which is rectangle without swapping references
        PhysicsBody circle, rect;
        if (a.Shape == BodyShape.Circle && b.Shape == BodyShape.Rectangle)
        {
            circle = a;
            rect = b;
        }
        else if (a.Shape == BodyShape.Rectangle && b.Shape == BodyShape.Circle)
        {
            circle = b;
            rect = a;
        }
        else
        {
            return false;
        }

        // Find closest point on rect to circle center
        var halfW = rect.Width * 0.5f;
        var halfH = rect.Height * 0.5f;
        var closestX = Math.Clamp(circle.X, rect.X - halfW, rect.X + halfW);
        var closestY = Math.Clamp(circle.Y, rect.Y - halfH, rect.Y + halfH);

        var dx = circle.X - closestX;
        var dy = circle.Y - closestY;
        var distSq = dx * dx + dy * dy;

        if (distSq >= circle.Radius * circle.Radius)
            return false;

        var dist = MathF.Sqrt(MathF.Max(distSq, 1e-10f));
        var nx = dx / dist;
        var ny = dy / dist;

        // When circle center is inside rect, dist is 0; push out along nearest edge
        if (distSq < 1e-10f)
        {
            var overlapX = halfW - Math.Abs(circle.X - rect.X);
            var overlapY = halfH - Math.Abs(circle.Y - rect.Y);
            if (overlapX < overlapY)
            {
                var signX = circle.X < rect.X ? -1f : 1f;
                contact = new Contact(circle, rect, overlapX + circle.Radius, signX, 0);
            }
            else
            {
                var signY = circle.Y < rect.Y ? -1f : 1f;
                contact = new Contact(circle, rect, overlapY + circle.Radius, 0, signY);
            }
            return true;
        }

        contact = new Contact(circle, rect, circle.Radius - dist, nx, ny);
        return true;
    }

    /// <summary>Rectangle vs Rectangle collision test (simple AABB).</summary>
    public static bool RectRect(PhysicsBody a, PhysicsBody b, out Contact contact)
    {
        contact = default;

        if (a.Shape != BodyShape.Rectangle || b.Shape != BodyShape.Rectangle)
            return false;

        var aHalfW = a.Width * 0.5f;
        var aHalfH = a.Height * 0.5f;
        var bHalfW = b.Width * 0.5f;
        var bHalfH = b.Height * 0.5f;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var overlapX = aHalfW + bHalfW - Math.Abs(dx);
        var overlapY = aHalfH + bHalfH - Math.Abs(dy);

        if (overlapX <= 0 || overlapY <= 0)
            return false;

        if (overlapX < overlapY)
        {
            var nx = dx < 0 ? -1f : 1f;
            contact = new Contact(a, b, overlapX, nx, 0);
        }
        else
        {
            var ny = dy < 0 ? -1f : 1f;
            contact = new Contact(a, b, overlapY, 0, ny);
        }

        return true;
    }

    /// <summary>Quick spatial hash lookup for broad phase (future optimization).</summary>
    public static void BroadPhase(
        System.Collections.Generic.List<PhysicsBody> bodies,
        out System.Collections.Generic.List<(int, int)> pairs)
    {
        pairs = new System.Collections.Generic.List<(int, int)>();
        for (var i = 0; i < bodies.Count; i++)
            for (var j = i + 1; j < bodies.Count; j++)
                pairs.Add((i, j));
    }
}
