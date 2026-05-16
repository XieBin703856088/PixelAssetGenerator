using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// IToolProvider for aesthetic evaluation of output images.
/// Evaluates color harmony, contrast, texture complexity, and other visual metrics.
/// </summary>
public sealed class AestheticToolProvider : IToolProvider
{
    /// <summary>
    /// Delegate to obtain the current preview bitmap for evaluation.
    /// If not set, aesthetic_eval returns an unavailable message.
    /// </summary>
    public Func<BitmapSource?>? GetPreviewBitmap { get; set; }

    public string ProviderName => "aesthetic";

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "aesthetic_eval",
            "Evaluates the aesthetic quality of the current output image. Returns scores for color harmony, contrast, texture complexity, pixel purity, seamless quality, and content density. Use this after generating or adjusting a texture to assess quality.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{},"required":[]}
            """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "aesthetic_eval" => Task.FromResult(Evaluate()),
            _ => Task.FromResult(new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown aesthetic tool: {toolName}\"}}", true)
            { IsUnhandled = true })
        };
    }

    private ToolResult Evaluate()
    {
        try
        {
            var bmp = GetPreviewBitmap?.Invoke();
            var buffer = BitmapToPixelBuffer(bmp);
            if (buffer == null || bmp == null)
            {
                return AiHelpers.ErrorResult("No preview available. Make sure the node graph has an Output node connected and the preview is generated.");
            }

            var score = AestheticEvaluator.Evaluate(buffer);
            if (score.HasError)
            {
                return AiHelpers.ErrorResult(score.ErrorMessage);
            }

            var result = new
            {
                success = true,
                overall = Math.Round(score.Overall, 2),
                colorHarmony = Math.Round(score.ColorHarmony, 2),
                colorRichness = Math.Round(score.ColorRichness, 2),
                contrast = Math.Round(score.Contrast, 2),
                textureComplexity = Math.Round(score.TextureComplexity, 2),
                pixelPurity = Math.Round(score.PixelPurity, 2),
                seamlessQuality = Math.Round(score.SeamlessQuality, 2),
                contentDensity = Math.Round(score.ContentDensity, 2),
                needsImprovement = score.NeedsImprovement,
                suggestion = score.Suggestion
            };

            return AiHelpers.SuccessResult(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            return AiHelpers.ErrorResult($"Aesthetic evaluation failed: {ex.Message}");
        }
    }

    private static PixelBuffer? BitmapToPixelBuffer(BitmapSource? bmp)
    {
        if (bmp == null) return null;
        try
        {
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            var buffer = new PixelBuffer(w, h);
            var stride = w * 4;
            var srcBytes = new byte[stride * h];
            bmp.CopyPixels(srcBytes, stride, 0);
            var dst = buffer.AsSpan();
            for (int i = 0; i < dst.Length && i < srcBytes.Length; i++)
                dst[i] = srcBytes[i] / 255f;
            return buffer;
        }
        catch { return null; }
    }
}
