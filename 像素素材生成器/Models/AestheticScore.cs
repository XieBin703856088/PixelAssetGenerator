namespace PixelAssetGenerator.Models;

/// <summary>
/// 美学评估结果。所有分数范围 0.0 ~ 1.0。
/// </summary>
public sealed class AestheticScore
{
    /// <summary>综合美学评分（加权平均）</summary>
    public double Overall { get; set; }

    /// <summary>色彩和谐度：颜色搭配是否协调</summary>
    public double ColorHarmony { get; set; }

    /// <summary>色彩丰富度：颜色种类和层次</summary>
    public double ColorRichness { get; set; }

    /// <summary>对比度：明暗对比是否适当</summary>
    public double Contrast { get; set; }

    /// <summary>纹理复杂度：细节丰富程度</summary>
    public double TextureComplexity { get; set; }

    /// <summary>像素纯度：是否保持了像素艺术的特征（边缘清晰、不模糊）</summary>
    public double PixelPurity { get; set; }

    /// <summary>是否可平铺（用于 tile 类型）</summary>
    public double SeamlessQuality { get; set; }

    /// <summary>是否有大量纯色/空区域（低分意味着有内容问题）</summary>
    public double ContentDensity { get; set; }

    /// <summary>评估时是否出错</summary>
    public bool HasError { get; set; }

    /// <summary>错误描述</summary>
    public string ErrorMessage { get; set; } = "";

    /// <summary>主要问题和改进建议（AI 可读）</summary>
    public string Suggestion { get; set; } = "";

    /// <summary>是否需要修正迭代（低于阈值）</summary>
    public bool NeedsImprovement => Overall < 0.5;

    /// <summary>是否良好（高于良好阈值）</summary>
    public bool IsGood => Overall >= 0.6;

    /// <summary>是否优秀（高于优秀阈值）</summary>
    public bool IsExcellent => Overall >= 0.8;

    /// <summary>序列化为文本（嵌入系统提示词）</summary>
    public string ToPromptText()
    {
        return
            $"综合评分: {Overall:F2}\n" +
            $"色彩和谐: {ColorHarmony:F2}\n" +
            $"色彩丰富: {ColorRichness:F2}\n" +
            $"对比度: {Contrast:F2}\n" +
            $"纹理复杂度: {TextureComplexity:F2}\n" +
            $"像素纯度: {PixelPurity:F2}\n" +
            $"平铺质量: {SeamlessQuality:F2}\n" +
            $"内容密度: {ContentDensity:F2}\n" +
            $"改进建议: {Suggestion}";
    }
}
