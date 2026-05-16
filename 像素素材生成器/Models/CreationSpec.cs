using System.Collections.Generic;

namespace PixelAssetGenerator.Models;

/// <summary>
/// 从用户自然语言中提取的结构化创作规范。
/// 所有字段都是可选的；解析器尽可能提取，无法提取的字段维持默认值。
/// </summary>
public sealed class CreationSpec
{
    /// <summary>主题/内容描述，如"黑暗魔法书封面""青花瓷盘"</summary>
    public string Subject { get; set; } = "";

    /// <summary>核心视觉风格，如"像素艺术""水墨风""赛博朋克""像素画"</summary>
    public string Style { get; set; } = "";

    /// <summary>情绪/氛围，如"黑暗神秘""明亮清新""阴森""温暖"</summary>
    public string Mood { get; set; } = "";

    /// <summary>自由标签列表，如 ["砖墙", "藤蔓", "破旧"]</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>复杂度等级 1-10，默认 5</summary>
    public int Complexity { get; set; } = 5;

    /// <summary>创作类型: tile(平铺图块), sprite(精灵), icon(图标), pattern(花纹), texture(纹理)</summary>
    public string CreationType { get; set; } = "tile";

    /// <summary>平铺要求: seamless(无缝), edged(有边), single(单张)</summary>
    public string Tiling { get; set; } = "seamless";

    /// <summary>尺寸提示，如"16x16""32x32"，为空表示使用默认</summary>
    public string SizeHint { get; set; } = "";

    /// <summary>要使用的节点类型建议（AI 推荐），如 ["Noise", "SolidColor", "BlendMode"]</summary>
    public List<string> SuggestedNodeTypes { get; set; } = new();

    /// <summary>是否包含用户明确的操作指令（如"创建""生成""做一张"）</summary>
    public bool HasExplicitActionIntent { get; set; }

    /// <summary>是否包含用户修改/调整指令（如"改暗一点""太密了"）</summary>
    public bool IsRefinement { get; set; }
}
