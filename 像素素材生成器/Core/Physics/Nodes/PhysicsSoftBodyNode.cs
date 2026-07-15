using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Physics.Nodes;

/// <summary>
/// Creates a soft body simulation using a spring-mass network.
/// The soft body is built as a grid of mass points connected by springs.
/// External forces (gravity, wind) and collisions affect the soft body shape.
/// The resulting deformation can drive texture displacement or particle positions.
/// Outputs a list of PhysicsBody points that represent the soft body surface.
/// </summary>
public sealed class PhysicsSoftBodyNode : IPersistentStateNode
{
    public string TypeName => "PhysicsSoftBody";
    public string Category => "Physics";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Image", GraphPortType.Image, "image"),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("shape", "rectangle",
            new[] { "rectangle", "circle", "blob" },
            new[] { "矩形", "圆形", "团块" }, "软体形状"),
        NodeParameterDefinition.Integer("gridX", 5, 2, 16, 1, "网格宽度"),
        NodeParameterDefinition.Integer("gridY", 5, 2, 16, 1, "网格高度"),
        NodeParameterDefinition.Number("stiffness", 0.5, 0.01, 2.0, 0.01, "弹簧刚度"),
        NodeParameterDefinition.Number("damping", 0.5, 0.0, 1.0, 0.01, "内部阻尼"),
        NodeParameterDefinition.Number("gravity", 0.3, -1.0, 1.0, 0.01, "重力"),
        NodeParameterDefinition.Number("wind", 0.0, -1.0, 1.0, 0.01, "风力"),
        NodeParameterDefinition.Number("pressure", 0.1, 0.0, 1.0, 0.01, "内部压力"),
        NodeParameterDefinition.Number("centerX", 0.5, 0, 1, 0.01, "中心X"),
        NodeParameterDefinition.Number("centerY", 0.5, 0, 1, 0.01, "中心Y"),
        NodeParameterDefinition.Number("size", 0.3, 0.05, 0.8, 0.01, "软体大小"),
        NodeParameterDefinition.Boolean("pinTop", false, "固定顶部"),
        NodeParameterDefinition.Boolean("pinBottom", false, "固定底部"),
        NodeParameterDefinition.Boolean("pinLeft", false, "固定左侧"),
        NodeParameterDefinition.Boolean("pinRight", false, "固定右侧"),
        NodeParameterDefinition.Color("bodyColor", Color.FromRgb(106, 214, 154), "软体颜色"),
        NodeParameterDefinition.Color("springColor", Color.FromRgb(39, 92, 72), "弹簧颜色"),
        NodeParameterDefinition.Number("pointSize", 0.022, 0.005, 0.08, 0.001, "质点大小"),
        NodeParameterDefinition.Boolean("drawSprings", true, "显示弹簧"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.TimeDependent;

    // ── Persistent state ──

    public string PersistentStateKey { get; private set; } = string.Empty;
    public object? PersistentState { get; set; }

    /// <summary>
    /// A mass point in the soft body network.
    /// </summary>
    public sealed class MassPoint
    {
        public float X, Y;
        public float VX, VY;
        public float PrevX, PrevY;
        public bool Pinned;
        public float Mass = 1f;
    }

    /// <summary>
    /// Spring connecting two mass points.
    /// </summary>
    public sealed class Spring
    {
        public required MassPoint A;
        public required MassPoint B;
        public float RestLength;
        public float Stiffness;
        public float Damping;
    }

    /// <summary>
    /// Internal state: grid of mass points + springs.
    /// </summary>
    public sealed record SoftBodyState(
        MassPoint[] Points,
        Spring[] Springs,
        bool Initialized,
        int GridX,
        int GridY,
        float CenterX,
        float CenterY,
        float Size,
        string Shape,
        float Stiffness,
        bool PinTop,
        bool PinBottom,
        bool PinLeft,
        bool PinRight);

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var state = GetOrCreateState(parameters, context);
        SimulateFrame(parameters, context);
        return RenderState(state, parameters, context.GetEffectiveSize());
    }

    /// <summary>
    /// Gets or creates the soft body state.
    /// </summary>
    public SoftBodyState GetOrCreateState(IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var gridX = Math.Clamp(GraphNodeBase.GetInt(parameters, "gridX", 5), 2, 16);
        var gridY = Math.Clamp(GraphNodeBase.GetInt(parameters, "gridY", 5), 2, 16);
        var centerX = GraphNodeBase.GetFloat(parameters, "centerX", 0.5f);
        var centerY = GraphNodeBase.GetFloat(parameters, "centerY", 0.5f);
        var size = GraphNodeBase.GetFloat(parameters, "size", 0.3f);
        var shape = GraphNodeBase.GetChoice(parameters, "shape", "rectangle");
        var stiffness = GraphNodeBase.GetFloat(parameters, "stiffness", 0.5f);
        var pinTop = GraphNodeBase.GetBool(parameters, "pinTop", false);
        var pinBottom = GraphNodeBase.GetBool(parameters, "pinBottom", false);
        var pinLeft = GraphNodeBase.GetBool(parameters, "pinLeft", false);
        var pinRight = GraphNodeBase.GetBool(parameters, "pinRight", false);

        if (PersistentState is SoftBodyState sbs && sbs.Initialized
            && sbs.GridX == gridX && sbs.GridY == gridY
            && NearlyEqual(sbs.CenterX, centerX) && NearlyEqual(sbs.CenterY, centerY)
            && NearlyEqual(sbs.Size, size) && NearlyEqual(sbs.Stiffness, stiffness)
            && string.Equals(sbs.Shape, shape, StringComparison.Ordinal)
            && sbs.PinTop == pinTop && sbs.PinBottom == pinBottom
            && sbs.PinLeft == pinLeft && sbs.PinRight == pinRight)
            return sbs;

        var halfW = size * 0.5f;
        var halfH = size * 0.5f;

        // Create mass points
        var points = new MassPoint[gridX * gridY];
        for (var y = 0; y < gridY; y++)
        {
            for (var x = 0; x < gridX; x++)
            {
                var nx = gridX > 1 ? x / (float)(gridX - 1) : 0.5f;
                var ny = gridY > 1 ? y / (float)(gridY - 1) : 0.5f;

                var px = centerX + (nx - 0.5f) * size;
                var py = centerY + (ny - 0.5f) * size;

                // Circle/blob topology must keep every grid slot populated because
                // the structural spring indices address the complete rectangular grid.
                // Project only the outer points onto a rounded boundary instead of
                // leaving null holes that later crash spring construction.
                if (shape == "circle" || shape == "blob")
                {
                    var dx = (nx - 0.5f) * 2f;
                    var dy = (ny - 0.5f) * 2f;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    var boundary = shape == "circle"
                        ? 1f
                        : 0.82f + GraphNodeBase.HashToUnit(x, y, context.Seed + 9041) * 0.18f;
                    if (dist > boundary)
                    {
                        var scale = boundary / Math.Max(dist, 0.0001f);
                        px = centerX + dx * halfW * scale;
                        py = centerY + dy * halfH * scale;
                    }
                }

                points[y * gridX + x] = new MassPoint
                {
                    X = px,
                    Y = py,
                    PrevX = px,
                    PrevY = py,
                    Mass = 1f
                };
            }
        }

        // Create springs: structural (horizontal, vertical), shear (diagonal), flexion (2-step)
        var springs = new List<Spring>();

        // Horizontal springs
        for (var y = 0; y < gridY; y++)
            for (var x = 0; x < gridX - 1; x++)
                AddSpring(points, y * gridX + x, y * gridX + x + 1, stiffness, springs);

        // Vertical springs
        for (var y = 0; y < gridY - 1; y++)
            for (var x = 0; x < gridX; x++)
                AddSpring(points, y * gridX + x, (y + 1) * gridX + x, stiffness, springs);

        // Shear springs (diagonal)
        for (var y = 0; y < gridY - 1; y++)
            for (var x = 0; x < gridX - 1; x++)
            {
                AddSpring(points, y * gridX + x, (y + 1) * gridX + x + 1, stiffness * 0.5f, springs);
                AddSpring(points, y * gridX + x + 1, (y + 1) * gridX + x, stiffness * 0.5f, springs);
            }

        if (pinTop)
            for (var x = 0; x < gridX; x++) points[x].Pinned = true;
        if (pinBottom)
            for (var x = 0; x < gridX; x++) points[(gridY - 1) * gridX + x].Pinned = true;
        if (pinLeft)
            for (var y = 0; y < gridY; y++) points[y * gridX].Pinned = true;
        if (pinRight)
            for (var y = 0; y < gridY; y++) points[y * gridX + gridX - 1].Pinned = true;

        var state = new SoftBodyState(points, springs.ToArray(), true,
            gridX, gridY, centerX, centerY, size, shape, stiffness,
            pinTop, pinBottom, pinLeft, pinRight);
        PersistentState = state;
        return state;
    }

    private static bool NearlyEqual(float left, float right)
        => MathF.Abs(left - right) <= 0.0001f;

    private static void AddSpring(MassPoint[] points, int idxA, int idxB, float stiffness, List<Spring> springs)
    {
        var a = points[idxA];
        var b = points[idxB];
        if (a == null || b == null) return;

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var restLength = MathF.Sqrt(dx * dx + dy * dy);

        if (restLength < 0.0001f) return;

        springs.Add(new Spring
        {
            A = a,
            B = b,
            RestLength = restLength,
            Stiffness = stiffness,
            Damping = 0.5f
        });
    }

    /// <summary>
    /// Simulates one frame of the soft body physics.
    /// </summary>
    public void SimulateFrame(IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        if (PersistentState is not SoftBodyState sbs || !sbs.Initialized)
            return;

        var deltaTime = context.DeltaTime;
        var gravity = GraphNodeBase.GetFloat(parameters, "gravity", 0.3f);
        var wind = GraphNodeBase.GetFloat(parameters, "wind", 0f);
        var damping = GraphNodeBase.GetFloat(parameters, "damping", 0.5f);
        var pressure = GraphNodeBase.GetFloat(parameters, "pressure", 0.1f);

        // Sub-stepping for stability
        var substeps = 4;
        var subDt = deltaTime / substeps;

        for (var step = 0; step < substeps; step++)
        {
            // Apply spring forces
            foreach (var spring in sbs.Springs)
            {
                var dx = spring.B.X - spring.A.X;
                var dy = spring.B.Y - spring.A.Y;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < 0.0001f) continue;

                var nx = dx / dist;
                var ny = dy / dist;

                // Spring force
                var displacement = dist - spring.RestLength;
                var springForce = displacement * spring.Stiffness;

                // Damping
                var relVx = spring.B.VX - spring.A.VX;
                var relVy = spring.B.VY - spring.A.VY;
                var relVn = relVx * nx + relVy * ny;
                var dampingForce = relVn * spring.Damping;

                // A stretched spring pulls A toward B and B toward A. The old
                // negative sign inverted Hooke's law and made cloth explode.
                var totalForce = (springForce + dampingForce) * subDt;

                if (!spring.A.Pinned)
                {
                    spring.A.VX += totalForce * nx / spring.A.Mass;
                    spring.A.VY += totalForce * ny / spring.A.Mass;
                }
                if (!spring.B.Pinned)
                {
                    spring.B.VX -= totalForce * nx / spring.B.Mass;
                    spring.B.VY -= totalForce * ny / spring.B.Mass;
                }
            }

            // Internal pressure (expand outward from center)
            if (pressure > 0.001f)
            {
                var cx = 0f; var cy = 0f; var count = 0;
                foreach (var p in sbs.Points)
                { if (p != null && !p.Pinned) { cx += p.X; cy += p.Y; count++; } }
                if (count > 0) { cx /= count; cy /= count; }

                foreach (var p in sbs.Points)
                {
                    if (p == null || p.Pinned) continue;
                    var dx = p.X - cx;
                    var dy = p.Y - cy;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.0001f) continue;
                    p.VX += (dx / dist) * pressure * subDt;
                    p.VY += (dy / dist) * pressure * subDt;
                }
            }

            // Integrate
            foreach (var p in sbs.Points)
            {
                if (p == null || p.Pinned) continue;

                p.PrevX = p.X;
                p.PrevY = p.Y;

                p.VX += wind * subDt;
                p.VY += gravity * subDt;
                p.VX *= (1f - damping * subDt);
                p.VY *= (1f - damping * subDt);

                p.X += p.VX * subDt;
                p.Y += p.VY * subDt;

                // Clamp to world bounds
                p.X = Math.Clamp(p.X, 0f, 1f);
                p.Y = Math.Clamp(p.Y, 0f, 1f);
            }
        }
    }

    private static PixelBuffer RenderState(SoftBodyState state,
        IReadOnlyDictionary<string, object> parameters, int size)
    {
        var output = PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
        var bodyColor = GraphNodeBase.GetColor(parameters, "bodyColor", Color.FromRgb(106, 214, 154));
        var springColor = GraphNodeBase.GetColor(parameters, "springColor", Color.FromRgb(39, 92, 72));
        if (GraphNodeBase.GetBool(parameters, "drawSprings", true))
            foreach (var spring in state.Springs)
                DrawLine(output,
                    (int)MathF.Round(spring.A.X * (size - 1)), (int)MathF.Round(spring.A.Y * (size - 1)),
                    (int)MathF.Round(spring.B.X * (size - 1)), (int)MathF.Round(spring.B.Y * (size - 1)),
                    springColor);

        var radius = Math.Max(1, (int)MathF.Round(
            GraphNodeBase.GetFloat(parameters, "pointSize", 0.022f) * size));
        foreach (var point in state.Points)
        {
            if (point == null) continue;
            var centerX = (int)MathF.Round(point.X * (size - 1));
            var centerY = (int)MathF.Round(point.Y * (size - 1));
            for (var y = centerY - radius; y <= centerY + radius; y++)
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                if ((uint)x >= (uint)size || (uint)y >= (uint)size) continue;
                if (Math.Abs(x - centerX) + Math.Abs(y - centerY) > radius + 1) continue;
                output.SetPixel(x, y, bodyColor.R / 255f, bodyColor.G / 255f, bodyColor.B / 255f, 1f);
            }
        }
        return output;
    }

    private static void DrawLine(PixelBuffer image, int x0, int y0, int x1, int y1, Color color)
    {
        var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)image.Width && (uint)y0 < (uint)image.Height)
                image.SetPixel(x0, y0, color.R / 255f, color.G / 255f, color.B / 255f, 1f);
            if (x0 == x1 && y0 == y1) break;
            var twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }
}
