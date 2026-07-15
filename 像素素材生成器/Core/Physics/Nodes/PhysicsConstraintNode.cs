using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Physics.Nodes;

/// <summary>
/// Adds constraints (distance, hinge, rope) between physics bodies in a PhysicsWorld.
/// Links to PhysicsSimulateNode to apply constraints during the physics step.
/// Input: Particles (Particle).
/// Output: Particles (Particle, passthrough).
/// </summary>
public sealed class PhysicsConstraintNode : IPersistentStateNode
{
    public string TypeName => "PhysicsConstraint";
    public string Category => "Physics";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("constraintType", "distance",
            new[] { "distance", "hinge", "rope" },
            new[] { "距离约束", "铰链约束", "绳索约束" }, "约束类型"),
        NodeParameterDefinition.Number("stiffness", 1.0, 0.1, 10.0, 0.1, "刚度"),
        NodeParameterDefinition.Number("damping", 0.5, 0, 1.0, 0.05, "阻尼"),
        NodeParameterDefinition.Number("restLength", 0.1, 0.01, 1.0, 0.01, "静止长度"),
        NodeParameterDefinition.Number("maxLength", 0.3, 0.01, 1.0, 0.01, "最大长度"),
        NodeParameterDefinition.Number("anchorX", 0.5, 0, 1.0, 0.01, "锚点X"),
        NodeParameterDefinition.Number("anchorY", 0.5, 0, 1.0, 0.01, "锚点Y"),
        NodeParameterDefinition.Number("minAngle", -1.0, -6.28, 6.28, 0.01, "最小角度"),
        NodeParameterDefinition.Number("maxAngle", 1.0, -6.28, 6.28, 0.01, "最大角度"),
        NodeParameterDefinition.Integer("bodyIndexA", 0, 0, 100, 1, "刚体索引A"),
        NodeParameterDefinition.Integer("bodyIndexB", 1, 0, 100, 1, "刚体索引B"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    /// <summary>
    /// Tracks the last constraint created so we can reuse/update it across frames.
    /// </summary>
    public sealed record ConstraintState(
        IConstraint Constraint,
        bool Initialized);

    private static PixelBuffer? _sharedPlaceholder;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        if (_sharedPlaceholder == null)
        {
            _sharedPlaceholder = PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);
        }
        return _sharedPlaceholder;
    }

    /// <summary>
    /// Creates or updates a constraint in the given PhysicsWorld based on current parameters.
    /// Called by ParticleEvaluationService after PhysicsSimulateNode has initialized its world.
    /// </summary>
    public void ApplyConstraint(
        IReadOnlyDictionary<string, object> parameters,
        PhysicsWorld world)
    {
        if (world == null) return;

        var constraintType = GraphNodeBase.GetChoice(parameters, "constraintType", "distance");
        var bodyIndexA = GraphNodeBase.GetInt(parameters, "bodyIndexA", 0);
        var bodyIndexB = GraphNodeBase.GetInt(parameters, "bodyIndexB", 1);

        var bodies = world.Bodies;
        if (bodyIndexA >= bodies.Count || bodyIndexB >= bodies.Count)
            return;

        var bodyA = bodies[bodyIndexA];
        var bodyB = bodies[bodyIndexB];
        if (!bodyA.IsEnabled || !bodyB.IsEnabled)
            return;

        // If we have an existing constraint of the same type, update it in-place
        if (PersistentState is ConstraintState cs && cs.Initialized)
        {
            if (TryUpdateConstraint(cs.Constraint, constraintType, parameters, bodyA, bodyB))
                return;

            // Type changed — remove old and create new
            world.Constraints.Remove(cs.Constraint);
        }

        // Create new constraint
        var constraint = CreateConstraint(constraintType, parameters, bodyA, bodyB);
        if (constraint != null)
        {
            world.Constraints.Add(constraint);
            PersistentState = new ConstraintState(constraint, true);
        }
    }

    private static IConstraint? CreateConstraint(
        string type,
        IReadOnlyDictionary<string, object> parameters,
        PhysicsBody bodyA, PhysicsBody bodyB)
    {
        switch (type)
        {
            case "distance":
                return new DistanceConstraint(bodyA, bodyB,
                    GraphNodeBase.GetFloat(parameters, "restLength", 0.1f))
                {
                    Stiffness = GraphNodeBase.GetFloat(parameters, "stiffness", 1f),
                    Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.5f),
                };

            case "hinge":
                return new HingeConstraint(bodyA,
                    GraphNodeBase.GetFloat(parameters, "anchorX", 0.5f),
                    GraphNodeBase.GetFloat(parameters, "anchorY", 0.5f))
                {
                    MinAngle = GraphNodeBase.GetFloat(parameters, "minAngle", -1f),
                    MaxAngle = GraphNodeBase.GetFloat(parameters, "maxAngle", 1f),
                };

            case "rope":
                return new RopeConstraint(bodyA, bodyB,
                    GraphNodeBase.GetFloat(parameters, "maxLength", 0.3f));

            default:
                return null;
        }
    }

    private static bool TryUpdateConstraint(
        IConstraint existing,
        string type,
        IReadOnlyDictionary<string, object> parameters,
        PhysicsBody bodyA, PhysicsBody bodyB)
    {
        switch (existing)
        {
            case DistanceConstraint dc when type == "distance":
                dc.RestLength = GraphNodeBase.GetFloat(parameters, "restLength", 0.1f);
                dc.Stiffness = GraphNodeBase.GetFloat(parameters, "stiffness", 1f);
                dc.Damping = GraphNodeBase.GetFloat(parameters, "damping", 0.5f);
                return true;

            case HingeConstraint hc when type == "hinge":
                hc.AnchorX = GraphNodeBase.GetFloat(parameters, "anchorX", 0.5f);
                hc.AnchorY = GraphNodeBase.GetFloat(parameters, "anchorY", 0.5f);
                hc.MinAngle = GraphNodeBase.GetFloat(parameters, "minAngle", -1f);
                hc.MaxAngle = GraphNodeBase.GetFloat(parameters, "maxAngle", 1f);
                return true;

            case RopeConstraint rc when type == "rope":
                rc.MaxLength = GraphNodeBase.GetFloat(parameters, "maxLength", 0.3f);
                return true;

            default:
                return false;
        }
    }
}
