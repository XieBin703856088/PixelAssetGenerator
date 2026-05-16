using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Blends between two frame input buffers using a blend factor.
/// Supports multiple blend modes for animation transitions.
/// </summary>
public sealed class FrameBlendNode : IGraphNode
{
    public string TypeName => "FrameBlend";
    public string Category => "Animation";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("FrameA", GraphPortType.Image),
        new GraphNodePort("FrameB", GraphPortType.Image),
        new GraphNodePort("Blend", GraphPortType.Float),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Image", GraphPortType.Image),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Choice("blendMode", "crossFade",
            new[] { "crossFade", "add", "difference", "multiply", "screen" },
            new[] { "交叉淡入淡出", "相加", "差值", "正片叠底", "滤色" }, "混合模式"),
        NodeParameterDefinition.Number("defaultBlend", 0.5, 0, 1, 0.01, "默认混合值"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var frameA = inputs[0];
        var frameB = inputs[1];
        var blendInput = inputs.Length > 2 ? inputs[2] : null;

        if (frameA == null || frameB == null)
        {
            // Return whichever is available
            var fallback = frameA ?? frameB;
            if (fallback != null) return fallback.Clone();
            return PixelBufferPool.Borrow(context.TileSize, context.TileSize);
        }

        // Determine blend factor
        float blendFactor;
        if (blendInput != null)
        {
            // Read from float buffer's R channel as normalized value
            var (r, _, _, _) = blendInput.GetPixel(0, 0);
            blendFactor = r;
        }
        else
        {
            blendFactor = GraphNodeBase.GetFloat(parameters, "defaultBlend", 0.5f);
        }

        blendFactor = Math.Clamp(blendFactor, 0f, 1f);

        var mode = GraphNodeBase.GetChoice(parameters, "blendMode", "crossFade");

        return mode switch
        {
            "crossFade" => PixelBuffer.Lerp(frameA, frameB, blendFactor),
            "add" => BlendAdd(frameA, frameB),
            "difference" => BlendDifference(frameA, frameB),
            "multiply" => BlendMultiply(frameA, frameB),
            "screen" => BlendScreen(frameA, frameB),
            _ => PixelBuffer.Lerp(frameA, frameB, blendFactor),
        };
    }

    private static PixelBuffer BlendAdd(PixelBuffer a, PixelBuffer b)
    {
        var result = a.Clone();
        var span = result.AsSpan();
        var bSpan = b.AsReadOnlySpan();
        for (var i = 0; i < span.Length; i++)
            span[i] = Math.Min(1f, span[i] + bSpan[i]);
        return result;
    }

    private static PixelBuffer BlendDifference(PixelBuffer a, PixelBuffer b)
    {
        var result = a.Clone();
        var span = result.AsSpan();
        var bSpan = b.AsReadOnlySpan();
        for (var i = 0; i < span.Length; i++)
            span[i] = Math.Abs(span[i] - bSpan[i]);
        return result;
    }

    private static PixelBuffer BlendMultiply(PixelBuffer a, PixelBuffer b)
    {
        var result = a.Clone();
        var span = result.AsSpan();
        var bSpan = b.AsReadOnlySpan();
        for (var i = 0; i < span.Length; i++)
            span[i] = span[i] * bSpan[i];
        return result;
    }

    private static PixelBuffer BlendScreen(PixelBuffer a, PixelBuffer b)
    {
        var result = a.Clone();
        var span = result.AsSpan();
        var bSpan = b.AsReadOnlySpan();
        for (var i = 0; i < span.Length; i++)
            span[i] = 1f - (1f - span[i]) * (1f - bSpan[i]);
        return result;
    }
}
