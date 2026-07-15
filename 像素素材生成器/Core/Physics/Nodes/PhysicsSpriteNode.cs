using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Physics.Nodes;

/// <summary>
/// Deterministic, pixel-aligned rigid/soft-body motion for a finished sprite.
/// It is intentionally analytical so scrubbing, multi-workflow preview and export
/// always produce the same frame without depending on previously evaluated frames.
/// </summary>
public sealed class PhysicsSpriteNode : GraphNodeBase, IMultiOutputNode
{
    private static readonly GraphNodePort[] Inputs =
    [
        new("精灵图像", GraphPortType.Image, "image", true),
        new("碰撞遮罩", GraphPortType.Mask, "collisionMask")
    ];

    private static readonly GraphNodePort[] Outputs =
    [
        new("物理动画", GraphPortType.Image, "image"),
        new("接触遮罩", GraphPortType.Mask, "contactMask"),
        new("运动进度", GraphPortType.Float, "progress")
    ];

    private static readonly NodeParameterDefinition[] Definitions =
    [
        NodeParameterDefinition.Choice("preset", "bounce",
            ["bounce", "swing", "jelly", "cloth", "tumble", "orbit", "impact"],
            ["弹跳落地", "悬挂摆动", "史莱姆软体", "旗帜布料", "翻滚掉落", "环绕漂浮", "受击回弹"], "物理预设"),
        NodeParameterDefinition.Number("strength", 0.65, 0, 1, 0.01, "运动强度"),
        NodeParameterDefinition.Number("speed", 1.0, 0.1, 6, 0.05, "模拟速度"),
        NodeParameterDefinition.Number("gravity", 1.0, 0, 2, 0.05, "重力"),
        NodeParameterDefinition.Number("restitution", 0.58, 0, 1, 0.01, "回弹"),
        NodeParameterDefinition.Number("damping", 0.28, 0, 1, 0.01, "阻尼"),
        NodeParameterDefinition.Number("floorY", 0.90, 0.35, 1, 0.01, "地面高度"),
        NodeParameterDefinition.Choice("pivot", "center",
            ["center", "top", "bottom", "left", "right"],
            ["中心", "顶部固定", "底部固定", "左侧固定", "右侧固定"], "固定点"),
        NodeParameterDefinition.Boolean("pixelSnap", true, "像素吸附"),
        NodeParameterDefinition.Boolean("loop", true, "循环播放"),
        NodeParameterDefinition.Number("phase", 0.0, 0, 1, 0.01, "相位")
    ];

    public override string TypeName => "PhysicsSprite";
    public override string Category => "Physics";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        outputs[1].Dispose();
        outputs[2].Dispose();
        return outputs[0];
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return
            [
                PixelBuffer.CreateSolid(size, size, 0, 0, 0, 0),
                PixelBuffer.CreateSolid(size, size, 0, 0, 0, 1),
                PixelBuffer.CreateSolid(1, 1, 0, 0, 0, 1)
            ];

        var preset = GetChoice(parameters, "preset", "bounce");
        var strength = Math.Clamp(GetFloat(parameters, "strength", 0.65f), 0f, 1f);
        var speed = Math.Clamp(GetFloat(parameters, "speed", 1f), 0.1f, 6f);
        var gravity = Math.Clamp(GetFloat(parameters, "gravity", 1f), 0f, 2f);
        var restitution = Math.Clamp(GetFloat(parameters, "restitution", 0.58f), 0f, 1f);
        var damping = Math.Clamp(GetFloat(parameters, "damping", 0.28f), 0f, 1f);
        var phase = GetFloat(parameters, "phase", 0f);
        var time = (context.AnimationTime ?? context.GlobalTime) * speed + phase;
        var loop = GetBool(parameters, "loop", true);
        var progress = loop ? PositiveModulo(time, 1f) : Math.Clamp(time, 0f, 1f);
        var wave = MathF.Sin(progress * MathF.Tau);
        var decay = loop ? 1f : MathF.Exp(-damping * progress * 6f);

        var bounds = FindAlphaBounds(source);
        var sourceCenterX = (bounds.Left + bounds.Right + 1) * 0.5f;
        var sourceCenterY = (bounds.Top + bounds.Bottom + 1) * 0.5f;
        var targetCenterX = size * 0.5f;
        var targetCenterY = size * 0.5f;
        var offsetX = 0f;
        var offsetY = 0f;
        var rotation = 0f;
        var scaleX = 1f;
        var scaleY = 1f;
        var clothAmplitude = 0f;

        switch (preset)
        {
            case "swing":
                rotation = wave * (10f + 24f * strength) * (0.55f + restitution * 0.45f) * decay;
                offsetY = MathF.Abs(wave) * strength * size * 0.025f;
                break;
            case "jelly":
                scaleX = 1f + wave * strength * 0.18f * decay;
                scaleY = 1f - wave * strength * 0.16f * decay;
                offsetY = MathF.Abs(wave) * strength * size * 0.025f;
                break;
            case "cloth":
                clothAmplitude = strength * size * (0.04f + restitution * 0.05f);
                rotation = wave * strength * 3f;
                break;
            case "tumble":
                offsetX = (progress - 0.5f) * size * strength * 0.55f;
                offsetY = (-0.38f + progress * progress * Math.Max(0.25f, gravity)) * size * strength;
                rotation = progress * 360f * (0.35f + strength * 0.85f);
                break;
            case "orbit":
                offsetX = MathF.Cos(progress * MathF.Tau) * size * strength * 0.18f;
                offsetY = wave * size * strength * 0.12f;
                rotation = -wave * strength * 8f;
                break;
            case "impact":
                var hit = MathF.Exp(-progress * (5f + damping * 7f));
                offsetX = MathF.Sin(progress * MathF.Tau * 3f) * hit * strength * size * 0.16f;
                rotation = -MathF.Sin(progress * MathF.Tau * 2f) * hit * strength * 14f;
                scaleX = 1f + hit * strength * 0.18f;
                scaleY = 1f - hit * strength * 0.14f;
                break;
            default: // bounce
                var arc = 4f * progress * (1f - progress);
                offsetY = -arc * strength * size * (0.18f + gravity * 0.12f);
                var landing = MathF.Pow(MathF.Abs(MathF.Cos(progress * MathF.PI)), 10f);
                scaleX = 1f + landing * strength * restitution * 0.20f;
                scaleY = 1f - landing * strength * restitution * 0.16f;
                break;
        }

        var floorY = Math.Clamp(GetFloat(parameters, "floorY", 0.90f), 0.35f, 1f) * size;
        if (preset is "bounce" or "tumble")
            targetCenterY = floorY - bounds.Height * scaleY * 0.5f;

        if (GetBool(parameters, "pixelSnap", true))
        {
            targetCenterX = MathF.Round(targetCenterX + offsetX);
            targetCenterY = MathF.Round(targetCenterY + offsetY);
            offsetX = 0f;
            offsetY = 0f;
        }
        else
        {
            targetCenterX += offsetX;
            targetCenterY += offsetY;
        }

        var pivot = GetChoice(parameters, "pivot", preset == "swing" ? "top" : "center");
        (var pivotX, var pivotY) = GetPivot(bounds, pivot);
        using var transformed = RenderTransform(source, size, sourceCenterX, sourceCenterY,
            targetCenterX, targetCenterY, pivotX, pivotY, rotation, scaleX, scaleY,
            clothAmplitude, progress, pivot);

        var image = transformed.Clone();
        var contact = BuildContactMask(image, inputs.Length > 1 ? inputs[1] : null, floorY);
        var progressBuffer = PixelBuffer.CreateSolid(1, 1, progress, progress, progress, 1f);
        return [image, contact, progressBuffer];
    }

    private static PixelBuffer RenderTransform(PixelBuffer source, int size,
        float sourceCenterX, float sourceCenterY, float targetCenterX, float targetCenterY,
        float pivotX, float pivotY, float rotationDegrees, float scaleX, float scaleY,
        float clothAmplitude, float progress, string pivot)
    {
        var result = PixelBufferPool.Borrow(size, size);
        var radians = rotationDegrees * MathF.PI / 180f;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        var outputPivotX = targetCenterX + (pivotX - sourceCenterX) * scaleX;
        var outputPivotY = targetCenterY + (pivotY - sourceCenterY) * scaleY;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x + 0.5f - outputPivotX;
            var dy = y + 0.5f - outputPivotY;
            var localX = (cosine * dx + sine * dy) / Math.Max(0.05f, scaleX);
            var localY = (-sine * dx + cosine * dy) / Math.Max(0.05f, scaleY);
            var sourceY = pivotY + localY;
            var normalized = pivot is "left" or "right"
                ? Math.Clamp(MathF.Abs(localX) / Math.Max(1f, source.Width), 0f, 1f)
                : Math.Clamp(MathF.Abs(localY) / Math.Max(1f, source.Height), 0f, 1f);
            var bend = clothAmplitude * normalized * normalized
                       * MathF.Sin(progress * MathF.Tau + normalized * MathF.PI * 2.5f);
            var sourceX = pivotX + localX - bend;
            var sx = (int)MathF.Floor(sourceX);
            var sy = (int)MathF.Floor(sourceY);
            if ((uint)sx >= (uint)source.Width || (uint)sy >= (uint)source.Height)
            {
                result.SetPixel(x, y, 0, 0, 0, 0);
                continue;
            }
            var pixel = source.GetPixel(sx, sy);
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }

    private static PixelBuffer BuildContactMask(PixelBuffer image, PixelBuffer? obstacle, float floorY)
    {
        var mask = PixelBufferPool.Borrow(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var alpha = image.GetPixel(x, y).A;
            var floorContact = alpha > 0.05f && y >= floorY - 1.5f;
            var obstacleContact = obstacle != null && alpha > 0.05f
                && obstacle.GetPixel(Math.Min(obstacle.Width - 1, x * obstacle.Width / image.Width),
                    Math.Min(obstacle.Height - 1, y * obstacle.Height / image.Height)).R > 0.25f;
            var value = floorContact || obstacleContact ? 1f : 0f;
            mask.SetPixel(x, y, value, value, value, 1f);
        }
        return mask;
    }

    private static (int Left, int Top, int Right, int Bottom, int Width, int Height) FindAlphaBounds(PixelBuffer source)
    {
        var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            if (source.GetPixel(x, y).A > 0.05f)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left || bottom < top) return (0, 0, source.Width - 1, source.Height - 1, source.Width, source.Height);
        return (left, top, right, bottom, right - left + 1, bottom - top + 1);
    }

    private static (float X, float Y) GetPivot((int Left, int Top, int Right, int Bottom, int Width, int Height) b, string pivot)
        => pivot switch
        {
            "top" => ((b.Left + b.Right + 1) * 0.5f, b.Top),
            "bottom" => ((b.Left + b.Right + 1) * 0.5f, b.Bottom + 1),
            "left" => (b.Left, (b.Top + b.Bottom + 1) * 0.5f),
            "right" => (b.Right + 1, (b.Top + b.Bottom + 1) * 0.5f),
            _ => ((b.Left + b.Right + 1) * 0.5f, (b.Top + b.Bottom + 1) * 0.5f)
        };

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
