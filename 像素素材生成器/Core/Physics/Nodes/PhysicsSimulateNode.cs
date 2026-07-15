using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Physics.Nodes;

/// <summary>
/// Physics simulation node — drives a PhysicsWorld that acts on particle positions.
/// Links to ParticleEmitterNode via ParticleBuffer to add physics bodies for each
/// active particle. Supports ground collision, inter-particle collision, and
/// deformation effects.
/// Input: ParticleBuffer (from emitter/force chain).
/// Output: ParticleBuffer with physics-applied positions.
/// </summary>
public sealed class PhysicsSimulateNode : IPersistentStateNode, IExclusiveInputNode
{
    public string TypeName => "PhysicsSimulate";
    public string Category => "Physics";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
        new GraphNodePort("Mask", GraphPortType.Mask),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("gravityX", 0, -1.0, 1.0, 0.01, "重力X"),
        NodeParameterDefinition.Number("gravityY", 0.5, -1.0, 1.0, 0.01, "重力Y"),
        NodeParameterDefinition.Number("damping", 0.99, 0.9, 1.0, 0.001, "阻尼"),
        NodeParameterDefinition.Integer("substeps", 4, 1, 8, 1, "子步数"),
        NodeParameterDefinition.Number("restitution", 0.5, 0, 1.0, 0.05, "弹性"),
        NodeParameterDefinition.Number("friction", 0.3, 0, 1.0, 0.05, "摩擦力"),
        NodeParameterDefinition.Boolean("enableCollisions", true, "粒子间碰撞"),
        NodeParameterDefinition.Boolean("collideWalls", true, "边界碰撞"),
        NodeParameterDefinition.Boolean("wrapEdges", false, "平铺环绕"),
        NodeParameterDefinition.Number("particleRadius", 0.02, 0.001, 0.1, 0.001, "粒子半径"),
        NodeParameterDefinition.Number("groundY", 0.95, 0.5, 1.0, 0.01, "地面Y位置"),
        NodeParameterDefinition.Number("deformStrength", 0.0, 0, 1.0, 0.01, "变形强度"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    /// <summary>
    /// Persistent physics state carried across frames.
    /// </summary>
    public sealed record PhysicsState(
        PhysicsWorld World,
        PhysicsBody[] Bodies,
        bool Initialized);

    /// <summary>Mask input cached from Process(), used for spatial variation in deformation.</summary>
    internal PixelBuffer? LastMaskInput { get; private set; }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Cache mask input (port 1)
        LastMaskInput = inputs.Length > 1 ? inputs[1] : null;

        return PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Gets or creates the persistent physics state.
    /// </summary>
    public PhysicsState GetOrCreateState(IReadOnlyDictionary<string, object> parameters)
    {
        if (PersistentState is PhysicsState ps && ps.Initialized)
            return ps;

        var world = new PhysicsWorld
        {
            GravityX = GraphNodeBase.GetFloat(parameters, "gravityX", 0),
            GravityY = GraphNodeBase.GetFloat(parameters, "gravityY", 0.5f),
            Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.99f),
            Substeps = GraphNodeBase.GetInt(parameters, "substeps", 4),
            WrapEdges = GraphNodeBase.GetBool(parameters, "wrapEdges", false),
            EnableCollisions = GraphNodeBase.GetBool(parameters, "enableCollisions", true),
            EnableBounds = GraphNodeBase.GetBool(parameters, "collideWalls", true),
        };

        var maxBodies = 2000; // enough for most particle systems
        var bodies = new PhysicsBody[maxBodies];
        var groundY = GraphNodeBase.GetFloat(parameters, "groundY", 0.95f);
        var tileWidth = GraphNodeBase.GetFloat(parameters, "particleRadius", 0.02f) * 2f;

        // Ground body
        var ground = new PhysicsBody(0.5f, groundY + 0.005f, 2f, 0.01f)
        {
            IsStatic = true,
            Restitution = GraphNodeBase.GetFloat(parameters, "restitution", 0.5f),
            Friction = GraphNodeBase.GetFloat(parameters, "friction", 0.3f),
        };
        world.AddBody(ground);

        // Pre-allocate body slots
        for (var i = 0; i < maxBodies; i++)
        {
            bodies[i] = new PhysicsBody(0.5f, 0.5f, tileWidth)
            {
                IsEnabled = false,
                Restitution = GraphNodeBase.GetFloat(parameters, "restitution", 0.5f),
                Friction = GraphNodeBase.GetFloat(parameters, "friction", 0.3f),
            };
            world.AddBody(bodies[i]);
        }

        ps = new PhysicsState(world, bodies, true);
        PersistentState = ps;
        return ps;
    }

    /// <summary>
    /// Simulates one frame of physics on the incoming particle buffer.
    /// Called by ParticleEvaluationService after emitter simulation.
    /// </summary>
    public void SimulateFrame(
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context,
        ParticleBuffer particleBuffer)
        => SimulateFrame(parameters, context, particleBuffer, Array.Empty<(PhysicsConstraintNode Node, IReadOnlyDictionary<string, object> Parameters)>());

    /// <summary>
    /// Simulates one frame and installs every constraint connected after this
    /// physics node before the world step. This keeps the visual graph order
    /// intuitive: emitter -> rigid physics -> constraint -> renderer.
    /// </summary>
    public void SimulateFrame(
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context,
        ParticleBuffer particleBuffer,
        IEnumerable<(PhysicsConstraintNode Node, IReadOnlyDictionary<string, object> Parameters)> constraints)
    {
        if (particleBuffer == null) return;

        var state = GetOrCreateState(parameters);
        var world = state.World;
        var bodies = state.Bodies;
        var deltaTime = context.DeltaTime;
        var groundY = GraphNodeBase.GetFloat(parameters, "groundY", 0.95f);
        var partRadius = GraphNodeBase.GetFloat(parameters, "particleRadius", 0.02f);
        var deformStrength = GraphNodeBase.GetFloat(parameters, "deformStrength", 0f);

        // Sync ground position
        if (world.Bodies.Count > 0 && world.Bodies[0].IsStatic)
        {
            world.Bodies[0].Y = groundY + 0.005f;
        }

        // Sync physics world parameters
        world.GravityX = GraphNodeBase.GetFloat(parameters, "gravityX", 0f);
        world.GravityY = GraphNodeBase.GetFloat(parameters, "gravityY", 0.5f);
        world.Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.99f);
        world.Substeps = GraphNodeBase.GetInt(parameters, "substeps", 4);
        world.EnableCollisions = GraphNodeBase.GetBool(parameters, "enableCollisions", true);
        world.EnableBounds = GraphNodeBase.GetBool(parameters, "collideWalls", true);
        world.WrapEdges = GraphNodeBase.GetBool(parameters, "wrapEdges", false);
        if (world.Bodies.Count > 0 && world.Bodies[0].IsStatic)
        {
            world.Bodies[0].Restitution = GraphNodeBase.GetFloat(parameters, "restitution", 0.5f);
            world.Bodies[0].Friction = GraphNodeBase.GetFloat(parameters, "friction", 0.3f);
        }

        // Sync particle bodies from active particles
        var activeCount = particleBuffer.ActiveCount;
        var maxBodies = bodies.Length;
        var particleSpan = particleBuffer.AsSpan();

        // Count bodies to enable (so we have the right active count)
        var bodyCount = Math.Min(activeCount, maxBodies);

        for (var i = 0; i < bodyCount; i++)
        {
            ref readonly var p = ref particleSpan[i];
            if (!p.Active)
            {
                bodies[i].IsEnabled = false;
                bodies[i].IsStatic = true;
                bodies[i].X = -10f; // out of bounds
                continue;
            }

            var body = bodies[i];

            // Optional mask acts as a pixel-authored collision field. When a body
            // enters an occupied texel, move it back to the previous position and
            // reflect the velocity against the local luminance gradient.
            if (LastMaskInput != null && SampleMask(LastMaskInput, body.X, body.Y) > 0.25f)
            {
                var epsilon = 1f / Math.Max(8, Math.Max(LastMaskInput.Width, LastMaskInput.Height));
                var normalX = SampleMask(LastMaskInput, body.X - epsilon, body.Y)
                              - SampleMask(LastMaskInput, body.X + epsilon, body.Y);
                var normalY = SampleMask(LastMaskInput, body.X, body.Y - epsilon)
                              - SampleMask(LastMaskInput, body.X, body.Y + epsilon);
                var length = MathF.Sqrt(normalX * normalX + normalY * normalY);
                if (length < 0.001f)
                {
                    normalX = body.X - body.PrevX;
                    normalY = body.Y - body.PrevY;
                    length = MathF.Max(0.001f, MathF.Sqrt(normalX * normalX + normalY * normalY));
                }
                normalX /= length; normalY /= length;
                var velocityAlongNormal = body.VX * normalX + body.VY * normalY;
                var bounce = 1f + GraphNodeBase.GetFloat(parameters, "restitution", 0.5f);
                body.VX -= velocityAlongNormal * normalX * bounce;
                body.VY -= velocityAlongNormal * normalY * bounce;
                body.X = body.PrevX;
                body.Y = body.PrevY;
            }
            body.IsEnabled = true;
            body.X = p.X;
            body.Y = p.Y;
            body.VX = p.VX;
            body.VY = p.VY;
            body.Radius = partRadius;
            body.IsStatic = false;
            body.Shape = BodyShape.Circle;
            body.Mass = 1f;
            body.Restitution = GraphNodeBase.GetFloat(parameters, "restitution", 0.5f);
            body.Friction = GraphNodeBase.GetFloat(parameters, "friction", 0.3f);
            body.ParticleIndex = i;
        }

        // Disable excess bodies
        for (var i = bodyCount; i < maxBodies; i++)
        {
            bodies[i].IsEnabled = false;
            bodies[i].IsStatic = true;
            bodies[i].X = -10f;
        }

        // Constraints need the freshly synchronized bodies, but must be installed
        // before stepping the world. Previously the constraint node was only a
        // passthrough and therefore had no effect in an evaluated graph.
        foreach (var (constraintNode, constraintParameters) in constraints)
            constraintNode.ApplyConstraint(constraintParameters, world);

        // Step physics world
        world.Step(deltaTime);

        // Write back particle positions and apply deformation
        for (var i = 0; i < bodyCount; i++)
        {
            ref var p = ref particleSpan[i];
            if (!p.Active) continue;

            var body = bodies[i];

            // Update particle velocity from physics
            p.VX = body.VX;
            p.VY = body.VY;

            // Update particle position from physics
            p.X = Math.Clamp(body.X, 0f, 1f);
            p.Y = Math.Clamp(body.Y, 0f, 1f);

            // Deformation: if particle hit a wall/ground, affect size based on speed
            if (deformStrength > 0.001f)
            {
                var speed = MathF.Sqrt(body.VX * body.VX + body.VY * body.VY);
                if (speed > 0.1f)
                {
                    // Squash in Y, stretch in X (or vice versa based on velocity direction)
                    var impact = Math.Clamp(speed * deformStrength * 2f, 0f, 0.5f);
                    var vNormX = Math.Abs(body.VX) / Math.Max(speed, 0.001f);
                    var vNormY = Math.Abs(body.VY) / Math.Max(speed, 0.001f);

                    // Size deformation: squish in movement direction
                    p.Size *= (1f - impact * 0.5f);

                    // Store rotation hint from velocity for the renderer
                    if (speed > 0.05f)
                        p.Rotation = MathF.Atan2(body.VY, body.VX);
                }
            }
        }

        // Re-compact the particle buffer (physics may have moved things)
        CompactParticles(particleSpan, activeCount);
    }

    private static float SampleMask(PixelBuffer mask, float normalizedX, float normalizedY)
    {
        var x = Math.Clamp((int)(normalizedX * mask.Width), 0, mask.Width - 1);
        var y = Math.Clamp((int)(normalizedY * mask.Height), 0, mask.Height - 1);
        return mask.GetPixel(x, y).R;
    }

    /// <summary>
    /// Re-compacts dead particles to the end of the active span.
    /// (PhysicsSimulateNode needs its own because ParticleSimulator.Compact is private.)
    /// </summary>
    private static void CompactParticles(Span<ParticleData> span, int count)
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
        for (var i = writeIdx; i < count; i++)
            span[i] = ParticleData.Dead();
    }
}
