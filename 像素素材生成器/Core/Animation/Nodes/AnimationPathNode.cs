using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Defines a 2D motion path using control points (Catmull-Rom spline or polyline).
/// Outputs the current position on the path as two Float values (X, Y) and the
/// tangent angle. Useful for driving camera movement, object position, or
/// particle emission location along a predefined curve.
/// </summary>
public sealed class AnimationPathNode : IGraphNode
{
    public string TypeName => "AnimationPath";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = Array.Empty<GraphNodePort>();

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("PositionX", GraphPortType.Float),
        new GraphNodePort("PositionY", GraphPortType.Float),
        new GraphNodePort("Angle", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("duration", 2.0, 0.1, 30.0, 0.1, "路径持续时间"),
        NodeParameterDefinition.Boolean("loop", true, "循环"),
        NodeParameterDefinition.Choice("pathType", "catmullRom",
            new[] { "catmullRom", "polyline", "bezier" },
            new[] { "Catmull-Rom样条", "折线", "贝塞尔" }, "路径类型"),
        NodeParameterDefinition.Integer("pointCount", 4, 2, 16, 1, "控制点数量"),
        NodeParameterDefinition.Number("smoothness", 0.5, 0.0, 1.0, 0.05, "平滑度"),
    };

    // Dynamic control point parameters
    private static IReadOnlyList<NodeParameterDefinition>? _runtimeParameters;

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => GetRuntimeParameters();

    private static IReadOnlyList<NodeParameterDefinition> GetRuntimeParameters()
    {
        if (_runtimeParameters != null) return _runtimeParameters;

        var list = new List<NodeParameterDefinition>(_parameters);
        for (var i = 1; i <= 16; i++)
        {
            list.Add(NodeParameterDefinition.Number($"point_{i}_x",
                i <= 4 ? (i - 1) / 3.0 : 0.5, 0, 1, 0.01, $"点{i}_X"));
            list.Add(NodeParameterDefinition.Number($"point_{i}_y",
                i <= 4 ? (i % 2 == 1 ? 0.2 : 0.8) : 0.5, 0, 1, 0.01, $"点{i}_Y"));
        }
        _runtimeParameters = list.AsReadOnly();
        return _runtimeParameters;
    }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var buf = PixelBufferPool.Borrow(1, 1);

        var duration = GraphNodeBase.GetFloat(parameters, "duration", 2f);
        var loop = GraphNodeBase.GetBool(parameters, "loop", true);
        var pointCount = Math.Clamp(GraphNodeBase.GetInt(parameters, "pointCount", 4), 2, 16);

        // Collect control points
        var points = new List<(float X, float Y)>();
        for (var i = 1; i <= pointCount; i++)
        {
            var x = GraphNodeBase.GetFloat(parameters, $"point_{i}_x", (i - 1) / (float)(pointCount - 1));
            var y = GraphNodeBase.GetFloat(parameters, $"point_{i}_y", i % 2 == 1 ? 0.2f : 0.8f);
            points.Add((x, y));
        }

        if (points.Count < 2)
        {
            buf.SetPixel(0, 0, 0.5f, 0.5f, 0, 1);
            return buf;
        }

        // Evaluate time
        var baseTime = context.AnimationTime ?? 0f;
        var t = loop
            ? (baseTime % duration) / duration
            : Math.Min(baseTime / duration, 1f);

        var pathType = GraphNodeBase.GetChoice(parameters, "pathType", "catmullRom");
        var smoothness = GraphNodeBase.GetFloat(parameters, "smoothness", 0.5f);

        (float X, float Y) pos;
        (float X, float Y) tangent;

        switch (pathType)
        {
            case "polyline":
                pos = EvaluatePolyline(points, t, out tangent);
                break;
            case "bezier":
                pos = EvaluateBezier(points, t, out tangent);
                break;
            case "catmullRom":
            default:
                pos = EvaluateCatmullRom(points, t, loop, smoothness, out tangent);
                break;
        }

        var angle = MathF.Atan2(tangent.Y, tangent.X) * 180f / MathF.PI;

        buf.SetPixel(0, 0, (float)Math.Clamp(pos.X, 0f, 1f), (float)Math.Clamp(pos.Y, 0f, 1f), angle, 1);
        return buf;
    }

    private static (float X, float Y) EvaluatePolyline(List<(float X, float Y)> points, float t, out (float X, float Y) tangent)
    {
        if (t >= 1f)
        {
            tangent = (0f, 1f);
            return points[^1];
        }
        if (t <= 0f)
        {
            tangent = (1f, 0f);
            return points[0];
        }

        var segmentCount = points.Count - 1;
        var segT = t * segmentCount;
        var segIdx = (int)segT;
        var localT = segT - segIdx;

        segIdx = Math.Min(segIdx, segmentCount - 1);

        var a = points[segIdx];
        var b = points[segIdx + 1];

        tangent = (b.X - a.X, b.Y - a.Y);
        return (a.X + (b.X - a.X) * localT, a.Y + (b.Y - a.Y) * localT);
    }

    private static (float X, float Y) EvaluateBezier(List<(float X, float Y)> points, float t, out (float X, float Y) tangent)
    {
        // De Casteljau algorithm
        var n = points.Count - 1;
        if (n <= 1) return EvaluatePolyline(points, t, out tangent);

        // Evaluate point
        var px = 0f; var py = 0f;
        for (var i = 0; i <= n; i++)
        {
            var coeff = Binomial(n, i) * MathF.Pow(t, i) * MathF.Pow(1f - t, n - i);
            px += points[i].X * coeff;
            py += points[i].Y * coeff;
        }

        // Evaluate derivative (tangent)
        var dpx = 0f; var dpy = 0f;
        var m = n - 1;
        if (m >= 1)
        {
            for (var i = 0; i <= m; i++)
            {
                var coeff = Binomial(m, i) * MathF.Pow(t, i) * MathF.Pow(1f - t, m - i);
                dpx += (points[i + 1].X - points[i].X) * coeff * n;
                dpy += (points[i + 1].Y - points[i].Y) * coeff * n;
            }
        }
        else
        {
            dpx = points[1].X - points[0].X;
            dpy = points[1].Y - points[0].Y;
        }

        tangent = (dpx, dpy);
        return (px, py);
    }

    private static (float X, float Y) EvaluateCatmullRom(
        List<(float X, float Y)> points, float t, bool loop, float smoothness,
        out (float X, float Y) tangent)
    {
        var count = points.Count;
        if (count < 2)
        {
            tangent = (0f, 1f);
            return (0.5f, 0.5f);
        }
        if (count == 2) return EvaluatePolyline(points, t, out tangent);

        // For Catmull-Rom we need points[segIdx-1] through points[segIdx+2]
        // The curve visits each control point
        float segmentT;
        int segIdx;
        int p0, p1, p2, p3;

        if (loop)
        {
            var segmentCount = count; // looping closes the shape
            var rawT = t * segmentCount;
            segIdx = (int)rawT % count;
            segmentT = rawT - (int)rawT;

            p0 = (segIdx - 1 + count) % count;
            p1 = segIdx;
            p2 = (segIdx + 1) % count;
            p3 = (segIdx + 2) % count;
        }
        else
        {
            var segmentCount = count - 1;
            var rawT = t * segmentCount;
            segIdx = (int)Math.Min(rawT, segmentCount - 1);
            segmentT = rawT - segIdx;

            p0 = Math.Max(0, segIdx - 1);
            p1 = segIdx;
            p2 = Math.Min(segIdx + 1, count - 1);
            p3 = Math.Min(segIdx + 2, count - 1);
        }

        var alpha = smoothness * 0.5f; // tension parameter

        var p = CatmullRomPoint(points[p0], points[p1], points[p2], points[p3], segmentT, alpha);
        var d = CatmullRomTangent(points[p0], points[p1], points[p2], points[p3], segmentT, alpha);

        tangent = (d.dx, d.dy);
        return (p.x, p.y);
    }

    private static (float x, float y) CatmullRomPoint(
        (float X, float Y) p0, (float X, float Y) p1,
        (float X, float Y) p2, (float X, float Y) p3,
        float t, float alpha)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        var m0x = (p2.X - p0.X) * alpha;
        var m0y = (p2.Y - p0.Y) * alpha;
        var m1x = (p3.X - p1.X) * alpha;
        var m1y = (p3.Y - p1.Y) * alpha;

        var x = (2f * t3 - 3f * t2 + 1f) * p1.X
              + (t3 - 2f * t2 + t) * m0x
              + (-2f * t3 + 3f * t2) * p2.X
              + (t3 - t2) * m1x;

        var y = (2f * t3 - 3f * t2 + 1f) * p1.Y
              + (t3 - 2f * t2 + t) * m0y
              + (-2f * t3 + 3f * t2) * p2.Y
              + (t3 - t2) * m1y;

        return (x, y);
    }

    private static (float dx, float dy) CatmullRomTangent(
        (float X, float Y) p0, (float X, float Y) p1,
        (float X, float Y) p2, (float X, float Y) p3,
        float t, float alpha)
    {
        var t2 = t * t;

        var m0x = (p2.X - p0.X) * alpha;
        var m0y = (p2.Y - p0.Y) * alpha;
        var m1x = (p3.X - p1.X) * alpha;
        var m1y = (p3.Y - p1.Y) * alpha;

        var dx = (6f * t2 - 6f * t) * p1.X
               + (3f * t2 - 4f * t + 1f) * m0x
               + (-6f * t2 + 6f * t) * p2.X
               + (3f * t2 - 2f * t) * m1x;

        var dy = (6f * t2 - 6f * t) * p1.Y
               + (3f * t2 - 4f * t + 1f) * m0y
               + (-6f * t2 + 6f * t) * p2.Y
               + (3f * t2 - 2f * t) * m1y;

        return (dx, dy);
    }

    private static int Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        k = Math.Min(k, n - k);
        var result = 1;
        for (var i = 1; i <= k; i++)
            result = result * (n - k + i) / i;
        return result;
    }
}
