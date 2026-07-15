using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>
/// Provides one canonical time source for the graph.  Besides normalized time it
/// exposes seconds, normalized frame and a deterministic pulse signal so ordinary
/// nodes can be driven without each one implementing its own playback rules.
/// </summary>
public sealed class AnimationTimelineNode : IGraphNode, IMultiOutputNode
{
    private static readonly GraphNodePort[] Outputs =
    {
        new("时间", GraphPortType.Float, "time"),
        new("秒", GraphPortType.Float, "seconds"),
        new("帧进度", GraphPortType.Float, "frame"),
        new("节拍", GraphPortType.Float, "pulse"),
        new("方向", GraphPortType.Float, "direction")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Number("duration", 1.0, 0.05, 60, 0.05, "片段时长（秒）"),
        NodeParameterDefinition.Number("speed", 1.0, -8, 8, 0.05, "播放速度"),
        NodeParameterDefinition.Number("offset", 0.0, -4, 4, 0.01, "时间偏移"),
        NodeParameterDefinition.Choice("loopMode", "loop",
            ["loop", "pingPong", "once"], ["循环", "往返", "单次"], "片段模式"),
        NodeParameterDefinition.Integer("pulseEvery", 1, 1, 64, 1, "每几帧触发"),
        NodeParameterDefinition.Integer("pulseWidth", 1, 1, 16, 1, "触发持续帧数")
    };

    public string TypeName => "AnimationTimeline";
    public string Category => "Animation";
    public IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        for (var i = 1; i < outputs.Length; i++) outputs[i].Dispose();
        return outputs[0];
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var sourceTime = context.AnimationTime ?? 0f;
        var speed = GraphNodeBase.GetFloat(parameters, "speed", 1f);
        var offset = GraphNodeBase.GetFloat(parameters, "offset", 0f);
        var raw = sourceTime * speed + offset;
        var mode = GraphNodeBase.GetChoice(parameters, "loopMode", "loop");
        var direction = speed < 0f ? -1f : 1f;

        float time;
        if (mode == "pingPong")
        {
            var wrapped = PositiveModulo(raw, 2f);
            direction *= wrapped <= 1f ? 1f : -1f;
            time = wrapped <= 1f ? wrapped : 2f - wrapped;
        }
        else if (mode == "once")
        {
            time = Math.Clamp(raw, 0f, 1f);
            if (raw <= 0f || raw >= 1f)
                direction = 0f;
        }
        else
        {
            time = PositiveModulo(raw, 1f);
        }

        var frameCount = Math.Max(1, context.AnimationFrameCount);
        var frame = context.AnimationFrame ?? Math.Min(frameCount - 1, (int)(time * frameCount));
        var frameProgress = frameCount <= 1 ? 0f : frame / (float)(frameCount - 1);
        var pulseEvery = Math.Max(1, GraphNodeBase.GetInt(parameters, "pulseEvery", 1));
        var pulseWidth = Math.Clamp(GraphNodeBase.GetInt(parameters, "pulseWidth", 1), 1, pulseEvery);
        var pulse = GraphNodeBase.Mod(frame, pulseEvery) < pulseWidth ? 1f : 0f;
        var duration = Math.Max(0.05f, GraphNodeBase.GetFloat(parameters, "duration", 1f));

        return
        [
            Scalar(time),
            Scalar(time * duration),
            Scalar(frameProgress),
            Scalar(pulse),
            Scalar(direction)
        ];
    }

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    private static PixelBuffer Scalar(float value)
    {
        var buffer = PixelBufferPool.Borrow(1, 1);
        buffer.SetPixel(0, 0, value, 0f, 0f, 1f);
        return buffer;
    }
}

/// <summary>
/// Ready-to-use RPG motion library.  It emits transform channels that can be
/// connected directly to AnimatedTransform, while still remaining deterministic
/// when the timeline is scrubbed or exported frame by frame.
/// </summary>
public sealed class MotionPresetNode : IGraphNode, IMultiOutputNode
{
    private static readonly GraphNodePort[] Outputs =
    {
        new("位置 X", GraphPortType.Float, "positionX"),
        new("位置 Y", GraphPortType.Float, "positionY"),
        new("旋转", GraphPortType.Float, "rotation"),
        new("缩放", GraphPortType.Float, "scale")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("preset", "idle",
            ["idle", "bob", "float", "shake", "hop", "hit", "orbit", "pendulum"],
            ["呼吸待机", "上下浮动", "漂浮", "震动", "跳跃", "受击反馈", "环绕", "摆动"], "动作预设"),
        NodeParameterDefinition.Number("baseX", 0.5, -1, 2, 0.01, "基准 X"),
        NodeParameterDefinition.Number("baseY", 0.5, -1, 2, 0.01, "基准 Y"),
        NodeParameterDefinition.Number("strength", 0.08, 0, 1, 0.005, "动作幅度"),
        NodeParameterDefinition.Number("speed", 1.0, 0.05, 8, 0.05, "动作速度"),
        NodeParameterDefinition.Number("phase", 0.0, 0, 1, 0.01, "相位"),
        NodeParameterDefinition.Boolean("pixelSnap", true, "像素吸附"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public string TypeName => "MotionPreset";
    public string Category => "Animation";
    public IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var outputs = ProcessMulti(inputs, parameters, context);
        for (var i = 1; i < outputs.Length; i++) outputs[i].Dispose();
        return outputs[0];
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var preset = GraphNodeBase.GetChoice(parameters, "preset", "idle");
        var baseX = GraphNodeBase.GetFloat(parameters, "baseX", 0.5f);
        var baseY = GraphNodeBase.GetFloat(parameters, "baseY", 0.5f);
        var strength = GraphNodeBase.GetFloat(parameters, "strength", 0.08f);
        var speed = GraphNodeBase.GetFloat(parameters, "speed", 1f);
        var phase = GraphNodeBase.GetFloat(parameters, "phase", 0f);
        var seed = GraphNodeBase.GetInt(parameters, "seed", 42);
        var t = PositiveModulo((context.AnimationTime ?? 0f) * speed + phase, 1f);
        var wave = MathF.Sin(t * MathF.Tau);
        var wave2 = MathF.Sin(t * MathF.Tau * 2f + 0.7f);
        var x = baseX;
        var y = baseY;
        var rotation = 0f;
        var scale = 1f;

        switch (preset)
        {
            case "bob":
                y -= wave * strength;
                break;
            case "float":
                x += wave2 * strength * 0.35f;
                y -= wave * strength;
                rotation = wave2 * strength * 45f;
                break;
            case "shake":
            {
                var frame = context.AnimationFrame ?? (int)(t * Math.Max(1, context.AnimationFrameCount));
                var jitterX = GraphNodeBase.HashToUnit(frame, seed, 1901) * 2f - 1f;
                var jitterY = GraphNodeBase.HashToUnit(frame, seed, 1907) * 2f - 1f;
                x += jitterX * strength;
                y += jitterY * strength * 0.6f;
                rotation = jitterX * strength * 80f;
                break;
            }
            case "hop":
            {
                var arc = 4f * t * (1f - t);
                y -= arc * strength * 2f;
                scale = 1f + (0.5f - arc) * strength * 0.55f;
                break;
            }
            case "hit":
            {
                var decay = MathF.Exp(-t * 7f);
                x += MathF.Sin(t * MathF.Tau * 4f) * decay * strength;
                rotation = MathF.Sin(t * MathF.Tau * 3f) * decay * strength * 75f;
                scale = 1f + decay * strength * 0.3f;
                break;
            }
            case "orbit":
                x += MathF.Cos(t * MathF.Tau) * strength;
                y += MathF.Sin(t * MathF.Tau) * strength;
                rotation = t * 360f;
                break;
            case "pendulum":
                rotation = wave * strength * 180f;
                break;
            default: // idle / breathing
                y -= MathF.Max(0f, wave) * strength * 0.25f;
                scale = 1f + wave * strength * 0.12f;
                break;
        }

        if (GraphNodeBase.GetBool(parameters, "pixelSnap", true))
        {
            var size = Math.Max(1, context.GetEffectiveSize());
            x = MathF.Round(x * size) / size;
            y = MathF.Round(y * size) / size;
        }

        // AnimatedTransform maps a unipolar scale input of 0.5 to 1.0.
        return [Scalar(x), Scalar(y), Scalar(rotation), Scalar(Math.Clamp(scale * 0.5f, 0.025f, 2f))];
    }

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    private static PixelBuffer Scalar(float value)
    {
        var buffer = PixelBufferPool.Borrow(1, 1);
        buffer.SetPixel(0, 0, value, 0f, 0f, 1f);
        return buffer;
    }
}

/// <summary>
/// Extracts one cell from a sprite sheet using nearest-neighbour sampling.  The
/// optional Frame input permits Timeline/AnimatedParameter driven overrides.
/// </summary>
public sealed class SpriteAnimatorNode : IGraphNode
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("精灵表", GraphPortType.Image, "sheet", true),
        new("帧", GraphPortType.Float, "frame")
    };

    private static readonly GraphNodePort[] Outputs =
    {
        new("图像", GraphPortType.Image, "image")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Integer("columns", 4, 1, 64, 1, "列数"),
        NodeParameterDefinition.Integer("rows", 1, 1, 64, 1, "行数"),
        NodeParameterDefinition.Integer("frameCount", 0, 0, 4096, 1, "有效帧数（0=全部）"),
        NodeParameterDefinition.Integer("startFrame", 0, 0, 4095, 1, "起始帧"),
        NodeParameterDefinition.Number("speed", 1.0, 0.05, 16, 0.05, "播放速度"),
        NodeParameterDefinition.Choice("playback", "loop",
            ["loop", "pingPong", "once"], ["循环", "往返", "单次"], "播放方式"),
        NodeParameterDefinition.Boolean("reverse", false, "反向播放"),
        NodeParameterDefinition.Boolean("fitCanvas", true, "适配图块大小")
    };

    public string TypeName => "SpriteAnimator";
    public string Category => "Animation";
    public IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var sheet = inputs.Length > 0 ? inputs[0] : null;
        if (sheet == null)
            return PixelBuffer.CreateSolid(context.GetEffectiveSize(), context.GetEffectiveSize(), 0f, 0f, 0f, 0f);

        var columns = Math.Clamp(GraphNodeBase.GetInt(parameters, "columns", 4), 1, Math.Max(1, sheet.Width));
        var rows = Math.Clamp(GraphNodeBase.GetInt(parameters, "rows", 1), 1, Math.Max(1, sheet.Height));
        var cellWidth = Math.Max(1, sheet.Width / columns);
        var cellHeight = Math.Max(1, sheet.Height / rows);
        var totalCells = columns * rows;
        var start = Math.Clamp(GraphNodeBase.GetInt(parameters, "startFrame", 0), 0, totalCells - 1);
        var requestedCount = GraphNodeBase.GetInt(parameters, "frameCount", 0);
        var count = requestedCount <= 0
            ? totalCells - start
            : Math.Clamp(requestedCount, 1, totalCells - start);

        float progress;
        if (inputs.Length > 1 && inputs[1] != null)
            progress = Math.Clamp(inputs[1]!.GetPixel(0, 0).R, 0f, 1f);
        else
            progress = context.AnimationTime ?? 0f;

        progress *= Math.Max(0.05f, GraphNodeBase.GetFloat(parameters, "speed", 1f));
        var playback = GraphNodeBase.GetChoice(parameters, "playback", "loop");
        int localFrame;
        if (playback == "pingPong" && count > 1)
        {
            var cycle = (count - 1) * 2;
            var raw = GraphNodeBase.Mod((int)MathF.Floor(progress * count), cycle);
            localFrame = raw < count ? raw : cycle - raw;
        }
        else if (playback == "once")
        {
            localFrame = Math.Min(count - 1, (int)MathF.Floor(Math.Clamp(progress, 0f, 0.999999f) * count));
        }
        else
        {
            localFrame = GraphNodeBase.Mod((int)MathF.Floor(progress * count), count);
        }

        if (GraphNodeBase.GetBool(parameters, "reverse", false))
            localFrame = count - 1 - localFrame;
        var absoluteFrame = start + localFrame;
        var sourceX = absoluteFrame % columns * cellWidth;
        var sourceY = absoluteFrame / columns * cellHeight;
        var fitCanvas = GraphNodeBase.GetBool(parameters, "fitCanvas", true);
        var outputWidth = fitCanvas ? context.GetEffectiveSize() : cellWidth;
        var outputHeight = fitCanvas ? context.GetEffectiveSize() : cellHeight;
        var result = PixelBufferPool.Borrow(Math.Max(1, outputWidth), Math.Max(1, outputHeight));

        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var sx = sourceX + Math.Min(cellWidth - 1, x * cellWidth / result.Width);
            var sy = sourceY + Math.Min(cellHeight - 1, y * cellHeight / result.Height);
            var pixel = sheet.GetPixel(sx, sy);
            result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
        }
        return result;
    }
}
