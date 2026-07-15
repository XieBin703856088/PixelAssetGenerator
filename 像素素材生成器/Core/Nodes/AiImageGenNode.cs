using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.AiImage;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>
/// Explicitly-triggered local AI image generation node. Process never blocks on
/// model download or diffusion; it schedules work and returns the last cached image.
/// </summary>
public sealed class AiImageGenNode : IGraphNode, INodeInstanceAware
{
    private static readonly IReadOnlyList<GraphNodePort> Inputs =
    [
        new("参考图像", GraphPortType.Image, "referenceImage"),
        new("可选蒙版", GraphPortType.Mask, "mask")
    ];

    private static readonly IReadOnlyList<GraphNodePort> Outputs =
    [
        new("输出", GraphPortType.Image, "output")
    ];

    private static readonly IReadOnlyList<NodeParameterDefinition> ParameterDefinitions =
    [
        NodeParameterDefinition.Text("prompt", AiImagePromptTemplates.Get("sprite"), "提示词"),
        NodeParameterDefinition.Text("negativePrompt", "photo, realistic, 3d render, blurry, smooth gradients, text, watermark, multiple objects", "反向提示词"),
        NodeParameterDefinition.Choice("style", "sprite",
            ["sprite", "character", "icon", "tile", "scene"],
            ["物件精灵", "角色", "图标", "无缝地块", "小场景"], "素材类型"),
        NodeParameterDefinition.Choice("visualStyle", "classic32",
            ["classic32", "detailed64", "retro16", "darkFantasy", "cozyRpg", "tacticalSciFi"],
            ["经典 32×32 RPG", "精细 64×64 RPG", "复古 16 位 JRPG", "暗黑奇幻 RPG", "清新生活 RPG", "科幻战术 RPG"], "RPG 风格限制"),
        NodeParameterDefinition.Choice("viewAngle", "auto",
            ["auto", "isometric45", "frontHigh", "topDown", "front", "side"],
            ["自动匹配", "45° 等距斜视", "正面俯视", "正俯视", "正面平视", "侧面"], "构图视角"),
        NodeParameterDefinition.Integer("paletteSize", 16, 4, 32, 1, "调色板色数"),
        NodeParameterDefinition.Integer("steps", 22, 8, 40, 1, "生成步数"),
        NodeParameterDefinition.Number("guidance", 7.5, 1, 12, 0.5, "提示词引导"),
        NodeParameterDefinition.Choice("referenceMode", "structure",
            ["structure", "strict", "repaint", "palette"],
            ["保留构图", "高保真复刻", "自由重绘", "仅参考配色"], "参考图模式"),
        NodeParameterDefinition.Number("referenceStrength", 0.72, 0.1, 0.95, 0.05, "参考图保真度"),
        NodeParameterDefinition.Choice("backgroundMode", "auto",
            ["auto", "transparent", "opaque"], ["自动", "透明", "保留背景"], "背景处理"),
        NodeParameterDefinition.Choice("dithering", "ordered4x4",
            ["none", "ordered4x4"], ["无", "有序 4×4"], "抖动"),
        NodeParameterDefinition.Boolean("addOutline", true, "精灵轮廓"),
        NodeParameterDefinition.Seed("seed", 42, 0, 999999999, "种子"),
        // UI-only explicit trigger. Hidden by the property panel template.
        NodeParameterDefinition.Integer("requestVersion", 0, 0, int.MaxValue, 1, "生成请求")
    ];

    public string TypeName => "AiImageGen";
    public string Category => "Logic";
    public IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.Expensive;
    public int NodeInstanceId { get; set; }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Output resolution is a project-level decision. Keeping it in the graph
        // context prevents an AI node from silently disagreeing with tile size.
        var size = Math.Clamp(context.TileSize, 8, 256);
        var revision = GetInt(parameters, "requestVersion", 0);
        var runtime = AiImageGenerationRuntime.Current;

        if (runtime != null && NodeInstanceId > 0 && revision > 0
            && runtime.GetStatus(NodeInstanceId).ModelInstalled)
        {
            var request = new AiImageGenerationRequest(
                revision,
                GetString(parameters, "prompt", "a pixel art game asset"),
                GetString(parameters, "negativePrompt", string.Empty),
                GetString(parameters, "style", "sprite"),
                GetString(parameters, "visualStyle", "classic32"),
                GetString(parameters, "viewAngle", "auto"),
                size,
                Math.Clamp(GetInt(parameters, "paletteSize", 16), 4, 32),
                Math.Clamp(GetInt(parameters, "steps", 22), 8, 40),
                Math.Clamp(GetFloat(parameters, "guidance", 7.5f), 1f, 12f),
                GetString(parameters, "referenceMode", "structure"),
                Math.Clamp(GetFloat(parameters, "referenceStrength", 0.72f), 0.1f, 0.95f),
                GetString(parameters, "backgroundMode", "auto"),
                GetString(parameters, "dithering", "none"),
                GetBool(parameters, "addOutline", true),
                GetInt(parameters, "seed", context.Seed));

            runtime.TryRequest(NodeInstanceId, request,
                inputs.Length > 0 ? inputs[0] : null,
                inputs.Length > 1 ? inputs[1] : null);
        }

        var cached = runtime?.GetOutputClone(NodeInstanceId);
        if (cached != null)
        {
            if (cached.Width == size && cached.Height == size)
                return cached;

            // A project may change tile size after generation. Preserve the last
            // result for preview, but always honor the current graph dimensions.
            var resized = ResizeNearest(cached, size);
            cached.Dispose();
            return resized;
        }

        var status = runtime?.GetStatus(NodeInstanceId) ?? AiImageGenerationStatus.NotInitialized;
        return AiImageStatusTile.Render(size, status);
    }

    private static string GetString(IReadOnlyDictionary<string, object> values, string key, string fallback)
        => values.TryGetValue(key, out var value) ? Convert.ToString(value) ?? fallback : fallback;

    private static int GetInt(IReadOnlyDictionary<string, object> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var value)) return fallback;
        try { return Convert.ToInt32(value); }
        catch { return fallback; }
    }

    private static float GetFloat(IReadOnlyDictionary<string, object> values, string key, float fallback)
    {
        if (!values.TryGetValue(key, out var value)) return fallback;
        try { return Convert.ToSingle(value); }
        catch { return fallback; }
    }

    private static bool GetBool(IReadOnlyDictionary<string, object> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var value)) return fallback;
        try { return Convert.ToBoolean(value); }
        catch { return fallback; }
    }

    private static PixelBuffer ResizeNearest(PixelBuffer source, int size)
    {
        var result = PixelBufferPool.Borrow(size, size);
        for (var y = 0; y < size; y++)
        {
            var sourceY = Math.Min(source.Height - 1, y * source.Height / size);
            for (var x = 0; x < size; x++)
            {
                var sourceX = Math.Min(source.Width - 1, x * source.Width / size);
                var pixel = source.GetPixel(sourceX, sourceY);
                result.SetPixel(x, y, pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }
        return result;
    }
}

internal static class AiImageStatusTile
{
    public static PixelBuffer Render(int size, AiImageGenerationStatus status)
    {
        var result = PixelBufferPool.Borrow(size, size);
        var (accentR, accentG, accentB) = status.Phase switch
        {
            AiImageGenerationPhase.Completed => (0.22f, 0.78f, 0.52f),
            AiImageGenerationPhase.Failed => (0.88f, 0.25f, 0.30f),
            AiImageGenerationPhase.Cancelled => (0.55f, 0.58f, 0.65f),
            AiImageGenerationPhase.Downloading or AiImageGenerationPhase.Verifying => (0.25f, 0.62f, 0.95f),
            AiImageGenerationPhase.Generating or AiImageGenerationPhase.PixelProcessing => (0.72f, 0.40f, 0.95f),
            _ => (0.42f, 0.48f, 0.66f)
        };

        var cell = Math.Max(2, size / 8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var checker = ((x / cell) + (y / cell)) % 2 == 0;
                var baseValue = checker ? 0.105f : 0.135f;
                result.SetPixel(x, y, baseValue, baseValue + 0.015f, baseValue + 0.035f, 1f);
            }
        }

        var center = size / 2;
        var radius = Math.Max(5, size / 5);
        for (var y = center - radius; y <= center + radius; y++)
        {
            if (y < 0 || y >= size) continue;
            for (var x = center - radius; x <= center + radius; x++)
            {
                if (x < 0 || x >= size) continue;
                var dx = Math.Abs(x - center);
                var dy = Math.Abs(y - center);
                if (dx + dy <= radius)
                    result.SetPixel(x, y, accentR, accentG, accentB, 1f);
            }
        }

        var barX = Math.Max(2, size / 8);
        var barWidth = size - barX * 2;
        var barY = size - Math.Max(4, size / 8);
        var progressWidth = (int)Math.Round(barWidth * Math.Clamp(status.Progress, 0, 1));
        for (var y = barY; y < Math.Min(size - 2, barY + Math.Max(2, size / 16)); y++)
        {
            for (var x = barX; x < barX + barWidth; x++)
            {
                var active = x - barX < progressWidth;
                result.SetPixel(x, y,
                    active ? accentR : 0.24f,
                    active ? accentG : 0.26f,
                    active ? accentB : 0.31f, 1f);
            }
        }

        return result;
    }
}
