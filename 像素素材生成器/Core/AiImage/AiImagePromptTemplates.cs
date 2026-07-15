using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.AiImage;

/// <summary>
/// User-editable starter prompts for each supported pixel-asset composition.
/// Keeping these in the core contract ensures the node default and property
/// panel always agree when the asset type changes.
/// </summary>
public static class AiImagePromptTemplates
{
    private static readonly IReadOnlyDictionary<string, string> Templates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprite"] = "一棵长满苔藓的古老橡树，单个独立物件，居中，清晰轮廓",
            ["character"] = "一名年轻的幻想冒险者，全身，正面待机姿势，单个居中角色",
            ["icon"] = "一瓶发光的蓝色魔力药水，单个背包图标，居中，形状清晰",
            ["tile"] = "长满苔藓的鹅卵石地板，俯视角，无缝平铺，均匀光照",
            ["scene"] = "古老森林中的小型神龛，紧凑的俯视角RPG场景，道路和材质层次清晰"
        };

    public static string Get(string? style)
        => style != null && Templates.TryGetValue(style, out var template)
            ? template
            : Templates["sprite"];
}
