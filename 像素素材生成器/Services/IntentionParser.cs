using System;
using System.Collections.Generic;
using System.Text.Json;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// 意图解析器：将用户的自然语言输入转化为结构化的 CreationSpec。
/// 使用规则 + LLM 辅助的方式提取风格/情绪/标签/复杂度等信息。
/// </summary>
public static class IntentionParser
{
    /// <summary>
    /// 从用户输入中解析结构化创作规范。
    /// 使用启发式规则提取关键要素，对无法确定的部分保留默认值。
    /// </summary>
    public static CreationSpec Parse(string userInput)
    {
        var spec = new CreationSpec { Subject = userInput };

        if (string.IsNullOrWhiteSpace(userInput))
            return spec;

        var input = userInput.Trim();

        // --- 检测操作意图 ---
        spec.HasExplicitActionIntent = ContainsAny(input, EXPLICIT_ACTION_KEYWORDS);
        spec.IsRefinement = ContainsAny(input, REFINEMENT_KEYWORDS);

        // --- 提取 tiling 类型 ---
        if (ContainsAny(input, SEAMLESS_KEYWORDS))
            spec.Tiling = "seamless";
        else if (ContainsAny(input, EDGED_KEYWORDS))
            spec.Tiling = "edged";

        // --- 提取 creation type ---
        spec.CreationType = DetectCreationType(input);

        // --- 提取尺寸 ---
        spec.SizeHint = ExtractSizeHint(input);

        // --- 提取风格 ---
        var (style, mood) = DetectStyleAndMood(input);
        spec.Style = style;
        spec.Mood = mood;

        // --- 提取复杂度 ---
        spec.Complexity = DetectComplexity(input);

        // --- 提取标签 ---
        spec.Tags = DetectTags(input);

        return spec;
    }

    /// <summary>
    /// 将 CreationSpec 转换为纯文本格式，嵌入系统提示词。
    /// AI 可以基于结构化规范选择更合适的节点和参数。
    /// </summary>
    public static string ToPromptBlock(CreationSpec spec)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(spec.Subject))
            parts.Add($"主题: {spec.Subject}");

        if (!string.IsNullOrEmpty(spec.Style))
            parts.Add($"风格: {spec.Style}");

        if (!string.IsNullOrEmpty(spec.Mood))
            parts.Add($"氛围: {spec.Mood}");

        if (spec.Tags.Count > 0)
            parts.Add($"标签: {string.Join(", ", spec.Tags)}");

        if (spec.Complexity > 0)
            parts.Add($"复杂度: {spec.Complexity}/10");

        if (!string.IsNullOrEmpty(spec.CreationType))
            parts.Add($"创作类型: {spec.CreationType}");

        parts.Add($"平铺要求: {spec.Tiling}");

        if (!string.IsNullOrEmpty(spec.SizeHint))
            parts.Add($"尺寸提示: {spec.SizeHint}");

        if (spec.SuggestedNodeTypes.Count > 0)
            parts.Add($"建议节点: {string.Join(", ", spec.SuggestedNodeTypes)}");

        if (spec.IsRefinement)
            parts.Add("注意: 这是对上次结果的修正/调整指令，请在此基础上修改而非重新创建。");

        return string.Join("\n", parts);
    }

    // ==================== 启发式检测方法 ====================

    private static bool ContainsAny(string text, string[] keywords)
    {
        foreach (var kw in keywords)
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static readonly string[] EXPLICIT_ACTION_KEYWORDS =
        { "创建", "生成", "做", "画", "制作", "设计", "新建", "绘制", "渲染", "create", "make", "generate", "draw" };

    private static readonly string[] REFINEMENT_KEYWORDS =
        { "太", "一点", "一些", "不够", "改", "调整", "修改", "暗", "亮", "密", "疏",
          "增加", "减少", "更", "换", "替换", "重新", "another", "more", "less", "darker", "brighter" };

    private static readonly string[] SEAMLESS_KEYWORDS =
        { "无缝", "平铺", "铺贴", "重复", "seamless", "tile", "tiling", "repeatable", "pattern" };

    private static readonly string[] EDGED_KEYWORDS =
        { "有边", "边框", "独立", "edged", "single", "bordered" };

    /// <summary>风格和情绪的启发式映射表</summary>
    private static readonly (string[] Keywords, string Style, string Mood)[] STYLE_MOOD_MAP =
    {
        (new[] { "像素", "pixel", "8bit", "8-bit", "retro", "点阵", "马赛克", "fc" }, "像素艺术", "怀旧"),
        (new[] { "赛博朋克", "cyberpunk", "霓虹", "neon", "未来" }, "赛博朋克", "科技感"),
        (new[] { "水墨", "国画", "写意", "ink", "brush" }, "水墨风", "淡雅"),
        (new[] { "暗黑", "黑暗", "黑暗神秘", "黑暗奇幻", "dark", "gothic", "恐怖", "阴森", "墓地" }, "黑暗奇幻", "黑暗神秘"),
        (new[] { "史诗", "幻想", "奇幻", "魔法", "magic", "fantasy", "rpg", "勇者" }, "奇幻", "史诗"),
        (new[] { "可爱", "卡通", "cute", "cartoon", "kawaii", "萌", "Q版" }, "卡通可爱", "明快"),
        (new[] { "中国风", "中式", "古典", "传统", "古风", "汉风", "china", "chinese style" }, "中国风", "典雅"),
        (new[] { "清新", "明亮", "自然", "清新自然", "轻快", "fresh", "bright", "pastel" }, "清新自然", "明亮轻快"),
        (new[] { "复古", "vintage", "旧化", "做旧", "古旧", "rusty", "磨损" }, "复古", "怀旧"),
        (new[] { "科幻", "sci-fi", "未来", "机械", "科技", "robot" }, "科幻", "冷峻"),
        (new[] { "蒸汽波", "vaporwave", "怀旧80", "合成器" }, "蒸汽波", "迷幻"),
        (new[] { "手绘", "手稿", "素描", "sketch", "handdrawn", "水彩", "水粉" }, "手绘风", "自然"),
        (new[] { "像素画", "点绘", "dither", "cros", "stipple" }, "像素艺术", "精致"),
    };

    private static (string Style, string Mood) DetectStyleAndMood(string input)
    {
        foreach (var (keywords, style, mood) in STYLE_MOOD_MAP)
        {
            if (ContainsAny(input, keywords))
                return (style, mood);
        }
        return ("", "");
    }

    private static string DetectCreationType(string input)
    {
        if (ContainsAny(input, new[] { "图块", "瓷砖", "地砖", "墙砖", "地板", "地面", "铺地",
                                         "tile", "铺装", "砖块", "石板", "碎石路", "马赛克" }))
            return "tile";
        if (ContainsAny(input, new[] { "花纹", "pattern", "花样", "纹理", "texture" }))
            return "pattern";
        if (ContainsAny(input, new[] { "精灵", "sprite", "角色", "敌人", "人物", "怪物", "道具", "item" }))
            return "sprite";
        if (ContainsAny(input, new[] { "图标", "icon", "按钮", "button", "ui" }))
            return "icon";
        return "tile";
    }

    private static int DetectComplexity(string input)
    {
        if (ContainsAny(input, new[] { "简单", "简约", "极简", "simple", "minimal", "plain" }))
            return 2;
        if (ContainsAny(input, new[] { "复杂", "精细", "华丽", "丰富", "精美", "复杂精细", "detailed", "complex", "elaborate", "fancy" }))
            return 8;
        if (ContainsAny(input, new[] { "中等", "适中", "普通", "medium", "moderate", "normal" }))
            return 5;
        // 检测数字
        var match = System.Text.RegularExpressions.Regex.Match(input, @"复杂度[约\s]*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
            return Math.Clamp(n, 1, 10);
        return 5;
    }

    private static string ExtractSizeHint(string input)
    {
        var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)\s*[xX×]\s*(\d+)");
        if (match.Success)
            return match.Value;
        // 常见像素尺寸别名
        if (ContainsAny(input, new[] { "16x16", "小图标", "tiny" }))
            return "16x16";
        if (ContainsAny(input, new[] { "32x32", "中等", "medium" }))
            return "32x32";
        if (ContainsAny(input, new[] { "64x64", "大", "large", "big" }))
            return "64x64";
        if (ContainsAny(input, new[] { "128x128", "超大", "huge" }))
            return "128x128";
        return "";
    }

    private static List<string> DetectTags(string input)
    {
        var tags = new List<string>();
        var lower = input.ToLowerInvariant();

        // 常见材质标签
        var tagMap = new (string[] Keywords, string Tag)[]
        {
            (new[] { "砖", "砖块", "brick", "红砖" }, "砖块"),
            (new[] { "地砖", "地板", "地面", "铺地", "铺装", "floor tile", "tiling" }, "地砖"),
            (new[] { "瓷砖", "瓦片", "马赛克", "mosaic", "ceramic tile" }, "瓷砖"),
            (new[] { "石", "石头", "stone", "rock", "石板", "碎石", "大理石", "花岗岩", "cobblestone", "flagstone", "marble" }, "石材"),
            (new[] { "木", "木头", "wood", "木质", "木材", "木板", "timber", "plank" }, "木质"),
            (new[] { "金属", "铁", "metal", "iron", "steel", "钢", "链甲", "chainmail" }, "金属"),
            (new[] { "水", "water", "liquid", "流体", "液体", "河流", "水流", "river" }, "液体"),
            (new[] { "冰", "ice", "frozen", "frost" }, "冰"),
            (new[] { "火", "fire", "火焰", "岩浆", "lava", "熔岩", "magma" }, "火"),
            (new[] { "草", "grass", "草地", "植被", "lawn", "meadow", "vegetation" }, "草地"),
            (new[] { "沙", "sand", "沙漠", "desert", "dune" }, "沙地"),
            (new[] { "雪", "snow", "雪地", "snowy" }, "雪地"),
            (new[] { "苔", "moss", "青苔", "苔藓", "lichen" }, "苔藓"),
            (new[] { "藤", "vine", "藤蔓", "ivy", "creeper" }, "藤蔓"),
            (new[] { "水晶", "crystal", "宝石", "gem", "钻石", "diamond" }, "水晶"),
            (new[] { "织物", "布料", "织物", "fabric", "布", "衣服", "cloth", "textile" }, "织物"),
            (new[] { "皮革", "leather", "皮质", "兽皮", "hide" }, "皮革"),
            (new[] { "墙壁", "墙面", "围墙", "墙砖", "城墙", "wall" }, "砖块"),
            (new[] { "血", "blood", "血迹", "血腥", "gore" }, "血迹"),
            (new[] { "魔", "magic", "rune", "符文", "魔法" }, "魔法"),
            (new[] { "光", "glow", "发光", "shine", "glowing" }, "发光"),
            (new[] { "脏", "dirty", "旧", "磨损", "破旧" }, "破旧"),
            (new[] { "金", "gold", "golden", "华丽" }, "金色"),
        };

        foreach (var (keywords, tag) in tagMap)
        {
            if (ContainsAny(input, keywords) && !tags.Contains(tag))
                tags.Add(tag);
        }

        // 从风格情绪映射中提取标签
        foreach (var (keywords, style, _) in STYLE_MOOD_MAP)
        {
            if (ContainsAny(input, keywords))
            {
                if (!tags.Contains(style))
                    tags.Add(style);
            }
        }

        return tags;
    }

    /// <summary>
    /// 将 CreationSpec 序列化为 JSON（与系统提示词一起传递给 LLM）
    /// </summary>
    public static string ToJson(CreationSpec spec)
    {
        return JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true });
    }
}
