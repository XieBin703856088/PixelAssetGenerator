using System;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// 美学评估器：对生成的像素图块进行基础的启发式美学评分。
/// 使用颜色直方图、边缘检测、内容密度等指标评估输出质量。
///
/// 工作原理：读取 PixelBuffer 的 float 像素数据（0-1 范围），
/// 转换为 0-255 byte 范围后进行分析。
/// </summary>
public static class AestheticEvaluator
{
    /// <summary>
    /// 评估一个 PixelBuffer 的美学质量。
    /// </summary>
    public static AestheticScore Evaluate(Core.PixelBuffer? buffer)
    {
        var score = new AestheticScore();

        if (buffer == null || buffer.Width == 0 || buffer.Height == 0)
        {
            score.HasError = true;
            score.ErrorMessage = "无有效图像数据，请先创建输出节点并连接好节点图。";
            score.Suggestion = "确保节点图正确连接，Output 节点接到了最终的 BlendMode/Effect 节点上。";
            return score;
        }

        try
        {
            int width = buffer.Width;
            int height = buffer.Height;
            int totalPixels = width * height;

            // 将 float 像素数据转换为 byte 数组
            var floatSpan = buffer.AsReadOnlySpan();
            var pixels = new byte[floatSpan.Length];
            for (int i = 0; i < floatSpan.Length; i++)
                pixels[i] = (byte)Math.Clamp(floatSpan[i] * 255f, 0, 255);

            int pixelCount = totalPixels;

            // --- 各维度分析 ---
            AnalyzeColors(pixels, pixelCount, score);
            AnalyzeContrast(pixels, pixelCount, score);
            AnalyzeContentDensity(pixels, pixelCount, score);
            AnalyzePixelPurity(pixels, width, height, score);
            AnalyzeTextureComplexity(pixels, width, height, score);
            AnalyzeSeamlessQuality(pixels, width, height, score);

            // --- 综合评分 ---
            score.Overall = Math.Round(
                score.ColorHarmony * 0.15 +
                score.ColorRichness * 0.10 +
                score.Contrast * 0.15 +
                score.TextureComplexity * 0.15 +
                score.PixelPurity * 0.20 +
                score.ContentDensity * 0.10 +
                score.SeamlessQuality * 0.15,
                2);

            score.Suggestion = GenerateSuggestion(score);
        }
        catch (Exception ex)
        {
            score.HasError = true;
            score.ErrorMessage = $"评估出错: {ex.Message}";
            score.Suggestion = "检查节点图是否有效。";
        }

        return score;
    }

    /// <summary>评估一个 WPF BitmapSource（从 UI 线程调用时使用）</summary>
    public static AestheticScore Evaluate(System.Windows.Media.Imaging.BitmapSource? bitmap)
    {
        if (bitmap == null)
            return new AestheticScore { HasError = true, ErrorMessage = "无有效图像", Suggestion = "请先输出节点并生成预览。" };

        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;
        var buffer = new Core.PixelBuffer(w, h);
        var stride = w * 4;
        var srcBytes = new byte[stride * h];
        bitmap.CopyPixels(srcBytes, stride, 0);

        // byte[0-255] -> float[0-1]
        var dst = buffer.AsSpan();
        for (int i = 0; i < dst.Length && i < srcBytes.Length; i++)
            dst[i] = srcBytes[i] / 255f;

        return Evaluate(buffer);
    }

    // ==================== 各维度分析 ====================

    private static void AnalyzeColors(byte[] pixels, int count, AestheticScore score)
    {
        if (count == 0) return;

        var seenColors = new System.Collections.Generic.HashSet<int>();
        long totalR = 0, totalG = 0, totalB = 0;
        int coloredPixels = 0;

        for (int i = 0; i < count; i++)
        {
            int idx = i * 4;
            byte r = pixels[idx];
            byte g = pixels[idx + 1];
            byte b = pixels[idx + 2];
            byte a = pixels[idx + 3];

            if (a < 10) continue;

            coloredPixels++;
            totalR += r;
            totalG += g;
            totalB += b;

            int quantized = (r / 32) << 16 | (g / 32) << 8 | (b / 32);
            seenColors.Add(quantized);
        }

        if (coloredPixels == 0)
        {
            score.ColorHarmony = 0;
            score.ColorRichness = 0;
            return;
        }

        double colorCount = seenColors.Count;
        score.ColorRichness = Math.Min(1.0, colorCount / 80.0);

        int avgR = (int)(totalR / coloredPixels);
        int avgG = (int)(totalG / coloredPixels);
        int avgB = (int)(totalB / coloredPixels);

        double variance = 0;
        int sampled = Math.Min(count, 2000);
        for (int i = 0; i < sampled; i++)
        {
            int idx = (i * 97 % count) * 4;
            int dr = pixels[idx] - avgR;
            int dg = pixels[idx + 1] - avgG;
            int db = pixels[idx + 2] - avgB;
            variance += (dr * dr + dg * dg + db * db);
        }
        variance /= sampled;
        variance /= (255.0 * 255.0 * 3);

        score.ColorHarmony = Math.Round(Math.Max(0, 1.0 - Math.Abs(variance - 0.15) * 3), 2);
    }

    private static void AnalyzeContrast(byte[] pixels, int count, AestheticScore score)
    {
        if (count == 0) return;

        int minLuminance = 255;
        int maxLuminance = 0;

        int sampled = Math.Min(count, 2000);
        for (int i = 0; i < sampled; i++)
        {
            int idx = (i * 97 % count) * 4;
            int lum = (pixels[idx] * 299 + pixels[idx + 1] * 587 + pixels[idx + 2] * 114) / 1000;
            if (lum < minLuminance) minLuminance = lum;
            if (lum > maxLuminance) maxLuminance = lum;
        }

        int range = maxLuminance - minLuminance;
        score.Contrast = range switch
        {
            < 30 => 0.1,
            < 60 => 0.3,
            < 100 => 0.5,
            < 150 => 0.7,
            < 200 => 0.85,
            _ => 0.9
        };
    }

    private static void AnalyzeContentDensity(byte[] pixels, int count, AestheticScore score)
    {
        if (count == 0) return;

        int nonEmpty = 0;
        int nonSolid = 0;

        for (int i = 0; i < count; i++)
        {
            int idx = i * 4;
            byte r = pixels[idx];
            byte g = pixels[idx + 1];
            byte b = pixels[idx + 2];
            byte a = pixels[idx + 3];

            if (a > 10) nonEmpty++;
            if (Math.Abs(r - g) + Math.Abs(g - b) + Math.Abs(r - b) > 8)
                nonSolid++;
        }

        double density = (double)nonEmpty / count;
        double variety = (double)nonSolid / count;

        score.ContentDensity = Math.Round(density * 0.5 + variety * 0.5, 2);
    }

    private static void AnalyzePixelPurity(byte[] pixels, int width, int height, AestheticScore score)
    {
        int semiTransparent = 0;
        int total = width * height;

        for (int i = 0; i < total; i++)
        {
            int a = pixels[i * 4 + 3];
            if (a > 10 && a < 245)
                semiTransparent++;
        }

        double semiRatio = (double)semiTransparent / total;
        score.PixelPurity = Math.Round(Math.Max(0, 1.0 - semiRatio * 5), 2);

        if (semiRatio > 0.3)
            score.PixelPurity = Math.Max(0.1, score.PixelPurity);
    }

    private static void AnalyzeTextureComplexity(byte[] pixels, int width, int height, AestheticScore score)
    {
        int total = width * height;
        if (total < 4) { score.TextureComplexity = 0; return; }

        double totalDiff = 0;
        int diffCount = 0;
        int sampled = Math.Min(total, 1000);

        for (int s = 0; s < sampled; s++)
        {
            int x = s * 31 % width;
            int y = (s * 17 + 7) % height;
            int nx = (x + 1) % width;
            int ny = (y + 1) % height;

            int idx1 = (y * width + x) * 4;
            int idx2 = (ny * width + nx) * 4;

            int diff = Math.Abs(pixels[idx1] - pixels[idx2])
                     + Math.Abs(pixels[idx1 + 1] - pixels[idx2 + 1])
                     + Math.Abs(pixels[idx1 + 2] - pixels[idx2 + 2]);

            totalDiff += diff;
            diffCount++;
        }

        if (diffCount == 0) { score.TextureComplexity = 0; return; }

        double avgDiff = totalDiff / diffCount / (3.0 * 255.0);
        score.TextureComplexity = Math.Round(
            avgDiff < 0.05 ? avgDiff * 10 :
            avgDiff < 0.3 ? 0.5 + avgDiff :
            1.0 - Math.Min(0.5, avgDiff - 0.3),
            2);
    }

    private static void AnalyzeSeamlessQuality(byte[] pixels, int width, int height, AestheticScore score)
    {
        int total = height + width;
        if (total == 0) { score.SeamlessQuality = 0.5; return; }

        double leftRightDiff = 0;
        double topBottomDiff = 0;

        for (int y = 0; y < height; y++)
        {
            int leftIdx = (y * width + 0) * 4;
            int rightIdx = (y * width + (width - 1)) * 4;
            int dr = Math.Abs(pixels[leftIdx] - pixels[rightIdx]);
            int dg = Math.Abs(pixels[leftIdx + 1] - pixels[rightIdx + 1]);
            int db = Math.Abs(pixels[leftIdx + 2] - pixels[rightIdx + 2]);
            leftRightDiff += dr + dg + db;
        }

        for (int x = 0; x < width; x++)
        {
            int topIdx = (0 * width + x) * 4;
            int bottomIdx = ((height - 1) * width + x) * 4;
            int dr = Math.Abs(pixels[topIdx] - pixels[bottomIdx]);
            int dg = Math.Abs(pixels[topIdx + 1] - pixels[bottomIdx + 1]);
            int db = Math.Abs(pixels[topIdx + 2] - pixels[bottomIdx + 2]);
            topBottomDiff += dr + dg + db;
        }

        double maxDiff = height * 3.0 * 255.0;
        leftRightDiff = Math.Min(1.0, leftRightDiff / maxDiff);
        topBottomDiff = Math.Min(1.0, topBottomDiff / maxDiff);

        double avgEdgeDiff = (leftRightDiff + topBottomDiff) / 2;
        score.SeamlessQuality = Math.Round(Math.Max(0, 1.0 - avgEdgeDiff * 2), 2);
    }

    private static string GenerateSuggestion(AestheticScore score)
    {
        var suggestions = new System.Collections.Generic.List<string>();

        if (score.ColorHarmony < 0.4)
            suggestions.Add("色彩搭配不协调，尝试使用 HSLAdjust 统一色调或使用更少的颜色。");
        else if (score.ColorHarmony < 0.6)
            suggestions.Add("颜色可以更统一，考虑使用 ColorAdjust 微调色相。");

        if (score.ColorRichness < 0.3)
            suggestions.Add("颜色过于单调，尝试添加更多颜色层次的节点（如 Gradient + Noise 组合）。");

        if (score.Contrast < 0.4)
            suggestions.Add("对比度太低，使用 HSLAdjust 增加对比度或调整亮度。");
        else if (score.Contrast > 0.9)
            suggestions.Add("对比度可能过高，检查是否存在过曝或过暗区域。");

        if (score.TextureComplexity < 0.3)
            suggestions.Add("纹理过于平滑，尝试叠加 Noise 节点增加细节。");
        else if (score.TextureComplexity < 0.4)
            suggestions.Add("纹理细节不足，考虑添加更多纹理层。");
        else if (score.TextureComplexity > 0.85)
            suggestions.Add("纹理过于杂乱，尝试用 Blur 柔化或减少噪波层数。");

        if (score.PixelPurity < 0.5)
            suggestions.Add("存在过多半透明像素，像素艺术应保持边缘锐利。");

        if (score.ContentDensity < 0.3)
            suggestions.Add("图块内容太稀疏，有大量空白区域。");

        if (score.SeamlessQuality < 0.4)
            suggestions.Add("平铺接缝明显，使用 SeamlessBlend 或 TileMirror 节点优化无缝效果。");

        if (suggestions.Count == 0)
        {
            if (score.IsExcellent)
                suggestions.Add("整体质量优秀，无需进一步调整。");
            else if (score.IsGood)
                suggestions.Add("效果良好，可以微调细节使其更完美。");
            else
                suggestions.Add("整体评分中等，建议针对低分项逐一优化。");
        }

        return string.Join(" ", suggestions.Take(3));
    }
}
