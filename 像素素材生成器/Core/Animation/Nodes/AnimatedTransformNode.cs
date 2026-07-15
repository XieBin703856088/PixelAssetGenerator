using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Pixel-perfect animated transform. Float inputs can be driven by Time, Wave,
/// AnimatedParameter, NoiseAnimation, or AnimationPath nodes.
/// </summary>
public sealed class AnimatedTransformNode : IGraphNode
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("Image", GraphPortType.Image, "image", true),
        new("PositionX", GraphPortType.Float, "positionX"),
        new("PositionY", GraphPortType.Float, "positionY"),
        new("Rotation", GraphPortType.Float, "rotation"),
        new("Scale", GraphPortType.Float, "scale")
    };

    private static readonly GraphNodePort[] Outputs =
    {
        new("Image", GraphPortType.Image, "image")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Number("positionX", 0.5, 0, 1, 0.01, "位置X"),
        NodeParameterDefinition.Number("positionY", 0.5, 0, 1, 0.01, "位置Y"),
        NodeParameterDefinition.Number("rotation", 0, -360, 360, 1, "旋转角度"),
        NodeParameterDefinition.Number("scale", 1, 0.05, 4, 0.01, "缩放"),
        NodeParameterDefinition.Boolean("wrap", false, "平铺回绕"),
        NodeParameterDefinition.Boolean("scaleInputIsBipolar", false, "缩放输入为双极性")
    };

    public string TypeName => "AnimatedTransform";
    public string Category => "Animation";
    public IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);

        var positionX = ReadScalar(inputs, 1) ?? GraphNodeBase.GetFloat(parameters, "positionX", 0.5f);
        var positionY = ReadScalar(inputs, 2) ?? GraphNodeBase.GetFloat(parameters, "positionY", 0.5f);
        var rotation = ReadScalar(inputs, 3) ?? GraphNodeBase.GetFloat(parameters, "rotation", 0f);
        var scaleInput = ReadScalar(inputs, 4);
        var scale = scaleInput ?? GraphNodeBase.GetFloat(parameters, "scale", 1f);
        if (scaleInput.HasValue)
        {
            scale = GraphNodeBase.GetBool(parameters, "scaleInputIsBipolar", false)
                ? 1f + scaleInput.Value
                : scaleInput.Value * 2f;
        }
        scale = Math.Clamp(scale, 0.05f, 4f);
        positionX = Math.Clamp(positionX, -1f, 2f);
        positionY = Math.Clamp(positionY, -1f, 2f);
        var wrap = GraphNodeBase.GetBool(parameters, "wrap", false);
        var radians = rotation * MathF.PI / 180f;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        var targetCenterX = positionX * size;
        var targetCenterY = positionY * size;
        var sourceCenterX = source.Width * 0.5f;
        var sourceCenterY = source.Height * 0.5f;
        var result = PixelBufferPool.Borrow(size, size);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x + 0.5f - targetCenterX) / scale;
            var dy = (y + 0.5f - targetCenterY) / scale;
            var sourceX = cosine * dx + sine * dy + sourceCenterX;
            var sourceY = -sine * dx + cosine * dy + sourceCenterY;
            var sx = (int)MathF.Floor(sourceX);
            var sy = (int)MathF.Floor(sourceY);
            if (wrap)
            {
                sx = GraphNodeBase.Mod(sx, source.Width);
                sy = GraphNodeBase.Mod(sy, source.Height);
            }
            else if ((uint)sx >= (uint)source.Width || (uint)sy >= (uint)source.Height)
            {
                result.SetPixel(x, y, 0f, 0f, 0f, 0f);
                continue;
            }
            var pixel = source.GetPixel(sx, sy);
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }

    private static float? ReadScalar(PixelBuffer?[] inputs, int index)
        => index < inputs.Length && inputs[index] != null ? inputs[index]!.GetPixel(0, 0).R : null;
}
