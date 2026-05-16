using System;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services;

/// <summary>
/// 像素验证后处理链 — 确保所有 AI 生成的高级节点输出满足像素艺术硬性标准。
/// 作为配方的"语法糖"自动包裹，AI 无需关心这些细节。
///
/// 后处理步骤：
/// 1. 调色板合规 — 如果未限制调色板，自动插入 palette 节点
/// 2. 分辨率锐化 — 模糊操作后强制 pixelate，恢复像素块锐利感
/// 3. 无缝平铺检测 — 边缘连续性检测与修复
/// 4. 边缘清晰度 — 检查并修复柔边
/// </summary>
public static class PixelPostProcessor
{
    /// <summary>
    /// 对 PixelBuffer 执行完整的后处理链，返回处理后的结果。
    /// 如果输入为 null 或不需要调整，返回原始 buffer（不修改）。
    /// </summary>
    public static PixelBuffer? Process(PixelBuffer? buffer, PostProcessOptions? options = null)
    {
        if (buffer == null) return null;

        var opts = options ?? new PostProcessOptions();
        var current = buffer.Clone();

        // 步骤1：调色板合规
        if (opts.EnforcePalette)
        {
            current = ApplyPaletteConstraint(current, opts.PaletteColorCount);
        }

        // 步骤2：分辨率锐化
        if (opts.SharpenPixels)
        {
            current = ApplyPixelSharpening(current);
        }

        // 步骤3：无缝平铺修复
        if (opts.FixSeamless)
        {
            current = ApplySeamlessFix(current);
        }

        // 步骤4：边缘清晰度
        if (opts.EnforceEdgeSharpness)
        {
            current = ApplyEdgeSharpening(current);
        }

        return current;
    }

    /// <summary>
    /// 生成后处理参数 — AI 可以根据需求选择启用的处理步骤。
    /// </summary>
    public static PostProcessOptions CreateOptions(
        bool isTile = true,
        bool hasBlurOps = false,
        int targetColors = 0)
    {
        return new PostProcessOptions
        {
            EnforcePalette = targetColors > 0 && targetColors <= 32,
            PaletteColorCount = targetColors > 0 ? targetColors : 16,
            SharpenPixels = hasBlurOps,
            FixSeamless = isTile,
            EnforceEdgeSharpness = true
        };
    }

    /// <summary>检查 PixelBuffer 是否需要进行后处理</summary>
    public static string? Analyze(PixelBuffer? buffer)
    {
        if (buffer == null) return null;

        var issues = new System.Collections.Generic.List<string>();
        int w = buffer.Width;
        int h = buffer.Height;
        var pixels = buffer.AsReadOnlySpan();

        // 检查调色板数量
        var colors = new System.Collections.Generic.HashSet<int>();
        int semiTransparent = 0;
        double totalEdgeDiff = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = buffer.GetPixel(x, y);
                int ri = (int)(r * 255), gi = (int)(g * 255), bi = (int)(b * 255), ai = (int)(a * 255);

                if (ai > 10 && ai < 245) semiTransparent++;

                // 颜色量化哈希
                int quantized = (ri / 16) << 16 | (gi / 16) << 8 | (bi / 16);
                colors.Add(quantized);

                // 边缘差异
                if (x == 0 || y == 0)
                {
                    var (r2, g2, b2, _) = buffer.GetPixelWrapped(x + 1, y + 1);
                    totalEdgeDiff += Math.Abs(r - r2) + Math.Abs(g - g2) + Math.Abs(b - b2);
                }
            }
        }

        int colorCount = colors.Count;
        if (colorCount > 32)
            issues.Add($"调色板颜色过多 ({colorCount}色)，建议限制到16色以内");

        if (semiTransparent > w * h * 0.1)
            issues.Add($"半透明像素较多 ({(double)semiTransparent / (w * h) * 100:F0}%)，建议锐化边缘");

        double avgEdgeDiff = totalEdgeDiff / (w + h);
        if (avgEdgeDiff < 0.05)
            issues.Add("边缘对比度低，建议检查平铺接缝");

        return issues.Count > 0 ? string.Join("；", issues) : null;
    }

    // ─── 各步骤实现 ───

    private static PixelBuffer ApplyPaletteConstraint(PixelBuffer buffer, int maxColors)
    {
        // 简化实现：使用中值切割量化减少颜色数量
        int w = buffer.Width, h = buffer.Height;
        var pixels = buffer.AsReadOnlySpan();
        var result = buffer.Clone();
        var resultData = result.AsSpan();

        // 收集所有颜色
        var colorList = new System.Collections.Generic.List<(float R, float G, float B, float A)>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            colorList.Add((pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]));
        }

        // 简单量化：按亮度排序后均匀采样 maxColors 个颜色作为调色板
        var palette = new System.Collections.Generic.List<(float R, float G, float B)>();
        var sorted = colorList
            .Select(c => (c, Lum: c.R * 0.299f + c.G * 0.587f + c.B * 0.114f))
            .OrderBy(x => x.Lum)
            .ToList();

        int step = Math.Max(1, sorted.Count / maxColors);
        for (int i = 0; i < sorted.Count && palette.Count < maxColors; i += step)
            palette.Add((sorted[i].c.R, sorted[i].c.G, sorted[i].c.B));

        if (palette.Count == 0) return result;

        // 将每个像素映射到最近的调色板颜色
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = buffer.GetPixel(x, y);
                if (a < 0.01f) continue;

                float minDist = float.MaxValue;
                float bestR = r, bestG = g, bestB = b;

                foreach (var (pr, pg, pb) in palette)
                {
                    float dr = r - pr, dg = g - pg, db = b - pb;
                    float dist = dr * dr + dg * dg + db * db;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestR = pr; bestG = pg; bestB = pb;
                    }
                }

                result.SetPixel(x, y, bestR, bestG, bestB, a);
            }
        }

        return result;
    }

    private static PixelBuffer ApplyPixelSharpening(PixelBuffer buffer)
    {
        // 简化的边缘锐化：基于相邻像素差异的 Unsharp Mask
        int w = buffer.Width, h = buffer.Height;
        var result = buffer.Clone();
        float strength = 0.3f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = buffer.GetPixel(x, y);
                if (a < 0.01f) continue;

                // 3x3 均值
                float sumR = 0, sumG = 0, sumB = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var (nr, ng, nb, _) = buffer.GetPixelWrapped(x + dx, y + dy);
                        sumR += nr; sumG += ng; sumB += nb;
                        count++;
                    }
                }

                float avgR = sumR / count, avgG = sumG / count, avgB = sumB / count;
                float sharpR = r + (r - avgR) * strength;
                float sharpG = g + (g - avgG) * strength;
                float sharpB = b + (b - avgB) * strength;

                result.SetPixel(x, y,
                    Math.Clamp(sharpR, 0, 1),
                    Math.Clamp(sharpG, 0, 1),
                    Math.Clamp(sharpB, 0, 1),
                    a);
            }
        }

        return result;
    }

    private static PixelBuffer ApplySeamlessFix(PixelBuffer buffer)
    {
        // 边缘混合：对靠近边缘的像素进行线性渐变混合
        int w = buffer.Width, h = buffer.Height;
        int blendWidth = Math.Max(1, w / 8);
        var result = buffer.Clone();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < blendWidth; x++)
            {
                float t = (float)x / blendWidth;
                float blend = t * t * (3 - 2 * t); // smoothstep

                var (r1, g1, b1, a1) = buffer.GetPixel(x, y);
                var (r2, g2, b2, _) = buffer.GetPixelWrapped(x + w, y);
                var (r3, g3, b3, _) = buffer.GetPixel(0, (y + h) % h);

                float r = r1 * (1 - blend) + ((r2 + r3) / 2) * blend;
                float g = g1 * (1 - blend) + ((g2 + g3) / 2) * blend;
                float b = b1 * (1 - blend) + ((b2 + b3) / 2) * blend;

                result.SetPixel(x, y, Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), a1);
            }
        }

        // 垂直边缘混合
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < blendWidth; y++)
            {
                float t = (float)y / blendWidth;
                float blend = t * t * (3 - 2 * t);

                var (r1, g1, b1, a1) = buffer.GetPixel(x, y);
                var (r2, g2, b2, _) = buffer.GetPixelWrapped(x, y + h);
                var (r3, g3, b3, _) = buffer.GetPixel((x + w) % w, 0);

                float r = r1 * (1 - blend) + ((r2 + r3) / 2) * blend;
                float g = g1 * (1 - blend) + ((g2 + g3) / 2) * blend;
                float b = b1 * (1 - blend) + ((b2 + b3) / 2) * blend;

                result.SetPixel(x, y, Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), a1);
            }
        }

        return result;
    }

    private static PixelBuffer ApplyEdgeSharpening(PixelBuffer buffer)
    {
        // 对半透明边缘像素进行阈值硬化
        int w = buffer.Width, h = buffer.Height;
        var result = buffer.Clone();
        float threshold = 0.3f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = buffer.GetPixel(x, y);
                if (a > 0.05f && a < 0.95f)
                {
                    // 半透明像素：硬化到最近的不透明或透明
                    float newA = a > threshold ? 1f : 0f;
                    result.SetPixel(x, y, r, g, b, newA);
                }
            }
        }

        return result;
    }
}

/// <summary>后处理选项</summary>
public sealed class PostProcessOptions
{
    public bool EnforcePalette { get; set; }
    public int PaletteColorCount { get; set; } = 16;
    public bool SharpenPixels { get; set; } = true;
    public bool FixSeamless { get; set; } = true;
    public bool EnforceEdgeSharpness { get; set; } = true;
}
