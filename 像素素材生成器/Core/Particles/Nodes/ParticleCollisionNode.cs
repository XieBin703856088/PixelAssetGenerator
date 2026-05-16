using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Particle collision node — applies particle-particle and particle-wall
/// collision detection and response. Stateless: processes the particle buffer
/// every frame.
/// Input: ParticleBuffer (via Particles port), Output: ParticleBuffer.
/// </summary>
public sealed class ParticleCollisionNode : IGraphNode
{
    public string TypeName => "ParticleCollision";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle)
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle)
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Boolean("collisionEnabled", true, "启用粒子碰撞"),
        NodeParameterDefinition.Number("radiusScale", 0.5, 0.1, 2.0, 0.1, "碰撞半径缩放"),
        NodeParameterDefinition.Number("restitution", 0.5, 0, 1.0, 0.05, "弹性系数"),
        NodeParameterDefinition.Choice("wallMode", "bounce",
            new[] { "bounce", "kill", "wrap" },
            new[] { "反弹", "销毁", "平铺" }, "边界碰撞模式"),
        NodeParameterDefinition.Number("wallDamping", 0.8, 0, 1.0, 0.05, "边界速度衰减"),
        NodeParameterDefinition.Number("maxCollisionDistance", 0.1, 0.01, 0.5, 0.01, "最大碰撞距离"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    private static PixelBuffer? _sharedPlaceholder;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // ParticleCollisionNode doesn't produce a PixelBuffer directly — it modifies
        // the particle buffer via IPersistentStateNode. Return a shared 1x1 placeholder.
        if (_sharedPlaceholder == null)
        {
            _sharedPlaceholder = PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
        }
        return _sharedPlaceholder;
    }

    /// <summary>
    /// Applies particle-particle and particle-wall collisions to the buffer.
    /// Call this from the owning node's SimulateFrame or during the simulation pipeline.
    /// </summary>
    public void ApplyCollisions(ParticleBuffer buffer, float deltaTime, IReadOnlyDictionary<string, object> parameters)
    {
        if (buffer == null || buffer.ActiveCount < 2)
            return;

        var collisionEnabled = GraphNodeBase.GetBool(parameters, "collisionEnabled", true);
        var radiusScale = GraphNodeBase.GetFloat(parameters, "radiusScale", 0.5f);
        var restitution = GraphNodeBase.GetFloat(parameters, "restitution", 0.5f);
        var wallMode = GraphNodeBase.GetChoice(parameters, "wallMode", "bounce");
        var wallDamping = GraphNodeBase.GetFloat(parameters, "wallDamping", 0.8f);
        var maxCollisionDistance = GraphNodeBase.GetFloat(parameters, "maxCollisionDistance", 0.1f);

        var span = buffer.AsSpan();
        var count = buffer.ActiveCount;

        // Particle-particle collisions
        if (collisionEnabled)
        {
            for (var i = 0; i < count; i++)
            {
                if (!span[i].Active) continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (!span[j].Active) continue;

                    var dx = span[j].X - span[i].X;
                    var dy = span[j].Y - span[i].Y;
                    var distSq = dx * dx + dy * dy;
                    var maxDistSq = maxCollisionDistance * maxCollisionDistance;

                    if (distSq > maxDistSq)
                        continue;

                    var overlap = (span[i].Size + span[j].Size) * radiusScale;
                    var dist = MathF.Sqrt(distSq);

                    if (dist < overlap && dist > 0.0001f)
                    {
                        // Normal direction
                        var nx = dx / dist;
                        var ny = dy / dist;

                        // Relative velocity along collision normal
                        var dvx = span[i].VX - span[j].VX;
                        var dvy = span[i].VY - span[j].VY;
                        var relVel = dvx * nx + dvy * ny;

                        if (relVel > 0)
                        {
                            // Swap velocity components along normal (simplified elastic)
                            span[i].VX -= relVel * nx * restitution;
                            span[i].VY -= relVel * ny * restitution;
                            span[j].VX += relVel * nx * restitution;
                            span[j].VY += relVel * ny * restitution;

                            // Separate overlapping particles
                            var push = (overlap - dist) * 0.5f;
                            span[i].X -= nx * push;
                            span[i].Y -= ny * push;
                            span[j].X += nx * push;
                            span[j].Y += ny * push;
                        }
                    }
                }
            }
        }

        // Wall collisions
        for (var i = 0; i < count; i++)
        {
            if (!span[i].Active) continue;

            ref var p = ref span[i];
            switch (wallMode)
            {
                case "bounce":
                    if (p.X < 0f)
                    { p.X = -p.X; p.VX = -p.VX * wallDamping; }
                    else if (p.X >= 1f)
                    { p.X = 2f - p.X; p.VX = -p.VX * wallDamping; }

                    if (p.Y < 0f)
                    { p.Y = -p.Y; p.VY = -p.VY * wallDamping; }
                    else if (p.Y >= 1f)
                    { p.Y = 2f - p.Y; p.VY = -p.VY * wallDamping; }
                    break;

                case "kill":
                    if (p.X < 0f || p.X >= 1f || p.Y < 0f || p.Y >= 1f)
                        p.Active = false;
                    break;

                case "wrap":
                    if (p.X < 0f) p.X += 1f;
                    else if (p.X >= 1f) p.X -= 1f;

                    if (p.Y < 0f) p.Y += 1f;
                    else if (p.Y >= 1f) p.Y -= 1f;
                    break;
            }
        }
    }
}
