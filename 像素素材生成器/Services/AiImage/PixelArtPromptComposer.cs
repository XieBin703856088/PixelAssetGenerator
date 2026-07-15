using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PixelAssetGenerator.Services.AiImage;

internal static class PixelArtPromptComposer
{
    // Domain vocabulary is translated locally so the bundled SD 1.x CLIP text
    // encoder receives useful English semantics without a cloud translation API.
    // Longest matching phrases win, which keeps 女战士 from also becoming 战士.
    private static readonly (string Chinese, string English)[] ChineseTerms =
    [
        ("长满苔藓", "moss-covered"), ("无缝平铺", "seamless tileable pattern"),
        ("透明背景", "transparent background"), ("均匀光照", "uniform lighting"),
        ("清晰轮廓", "clean readable silhouette"), ("材质层次", "clear material layers"),
        ("单个独立物件", "single isolated object"), ("单个物件", "single object"),
        ("背包图标", "inventory icon"), ("俯视角", "top-down view"),
        ("等距视角", "isometric view"), ("侧视角", "side view"),
        ("正面待机姿势", "front-facing idle pose"), ("待机姿势", "idle pose"),
        ("攻击姿势", "attack pose"), ("行走姿势", "walking pose"),
        ("施法姿势", "casting pose"), ("全身", "full body"),
        ("单个角色", "single character"), ("小型场景", "small compact scene"),
        ("古老橡树", "ancient oak tree"), ("橡树", "oak tree"),
        ("松树", "pine tree"), ("棕榈树", "palm tree"), ("枯树", "dead tree"),
        ("树木", "tree"), ("古树", "ancient tree"), ("树", "tree"),
        ("玻璃幕墙", "glass curtain wall"), ("办公大楼", "office building"),
        ("写字楼", "office building"), ("办公楼", "office building"),
        ("多层建筑", "multi-storey building"), ("建筑物", "building"),
        ("大楼", "multi-storey building"), ("建筑", "building"),
        ("一块石块", "one natural rock"), ("一块石头", "one natural rock"),
        ("岩石块", "rock boulder"), ("石块", "rock boulder"),
        ("石头", "natural rock"), ("岩石", "natural rock"),
        ("主入口", "main entrance"), ("入口", "entrance"),
        ("楼层", "floors"), ("窗格", "window grid"), ("屋顶", "roofline"),
        ("鹅卵石地板", "cobblestone floor tile"), ("鹅卵石", "cobblestone"),
        ("石板地面", "flagstone floor"), ("石板", "flagstone"),
        ("木地板", "wooden floor"), ("地板", "floor tile"),
        ("草地", "grass tile"), ("沙地", "sand tile"), ("雪地", "snow tile"),
        ("泥地", "mud tile"), ("道路", "path"), ("悬崖", "cliff"),
        ("石墙", "stone wall"), ("木墙", "wooden wall"), ("砖墙", "brick wall"),
        ("墙壁", "wall"), ("栅栏", "fence"), ("城堡", "castle"),
        ("神龛", "shrine"), ("祭坛", "altar"), ("房屋", "house"),
        ("小屋", "cottage"), ("桥梁", "bridge"), ("桥", "bridge"),
        ("门", "door"), ("窗户", "window"), ("楼梯", "stairs"),
        ("宝箱", "treasure chest"), ("木桶", "wooden barrel"), ("箱子", "crate"),
        ("路灯", "street lamp"), ("火把", "torch"), ("篝火", "campfire"),
        ("魔力药水", "mana potion"), ("生命药水", "health potion"), ("药水", "potion"),
        ("长剑", "long sword"), ("短剑", "short sword"), ("匕首", "dagger"),
        ("弓箭", "bow and arrow"), ("法杖", "magic staff"), ("战斧", "battle axe"),
        ("锤子", "hammer"), ("剑", "sword"), ("盾牌", "shield"),
        ("头盔", "helmet"), ("盔甲", "armor"), ("锁甲", "chainmail"),
        ("钥匙", "key"), ("金币", "gold coin"), ("水晶", "crystal"),
        ("宝石", "gemstone"), ("卷轴", "scroll"), ("魔法书", "spellbook"),
        ("女战士", "female warrior"), ("男战士", "male warrior"), ("战士", "warrior"),
        ("女法师", "female mage"), ("男法师", "male mage"), ("法师", "mage"),
        ("弓箭手", "archer"), ("盗贼", "rogue"), ("骑士", "knight"),
        ("冒险者", "adventurer"), ("公主", "princess"), ("国王", "king"),
        ("角色", "character"), ("史莱姆", "slime monster"), ("骷髅", "skeleton"),
        ("哥布林", "goblin"), ("兽人", "orc"), ("僵尸", "zombie"),
        ("恶魔", "demon"), ("巨龙", "dragon"), ("龙", "dragon"),
        ("怪物", "monster"), ("熊猫", "panda"), ("狐狸", "fox"),
        ("兔子", "rabbit"), ("猫", "cat"), ("狗", "dog"), ("狼", "wolf"),
        ("熊", "bear"), ("马", "horse"), ("鸟", "bird"), ("鱼", "fish"),
        ("蘑菇", "mushroom"), ("花朵", "flower"), ("灌木", "bush"),
        ("森林", "forest"), ("苔藓", "moss"), ("藤蔓", "vines"),
        ("西瓜", "watermelon"), ("苹果", "apple"), ("面包", "bread"),
        ("布料", "fabric"), ("皮革", "leather"), ("大理石", "marble"),
        ("金属", "metal"), ("木制", "wooden"), ("石制", "stone"),
        ("钢铁", "steel"), ("黄金", "gold"), ("白银", "silver"), ("青铜", "bronze"),
        ("生锈", "rusty"), ("破旧", "weathered"), ("古老", "ancient"),
        ("发光", "glowing"), ("魔法", "magical"), ("神圣", "holy"),
        ("邪恶", "evil"), ("可爱", "cute"), ("年轻", "young"),
        ("巨大", "giant"), ("小型", "small"), ("茂密", "lush"),
        ("干枯", "withered"), ("燃烧", "burning"), ("冰冻", "frozen"),
        ("红色", "red"), ("橙色", "orange"), ("黄色", "yellow"),
        ("绿色", "green"), ("青色", "cyan"), ("蓝色", "blue"),
        ("紫色", "purple"), ("粉色", "pink"), ("白色", "white"),
        ("黑色", "black"), ("灰色", "gray"), ("棕色", "brown"),
        ("金色", "golden"), ("银色", "silver"), ("红发", "red hair"),
        ("金发", "blonde hair"), ("黑发", "black hair"), ("白发", "white hair"),
        ("闪电", "lightning"), ("烟雾", "smoke"), ("火焰", "fire"),
        ("水流", "flowing water"), ("雨水", "rain"), ("毒气", "poison gas"),
        ("电路", "circuit pattern"), ("蜂窝", "honeycomb pattern"), ("鳞片", "scales"),
        ("居中", "centered"), ("正面", "front-facing"), ("背面", "back view"),
        ("俯视", "top-down"), ("侧面", "side view"), ("独立", "isolated"),
        ("抱着", "holding in both arms"), ("手持", "holding"), ("挥舞", "wielding"),
        ("站立", "standing"), ("坐着", "sitting"), ("奔跑", "running"),
        ("飞行", "flying"), ("睡觉", "sleeping"), ("形状清晰", "bold readable shape")
    ];

    public static string Compose(
        string prompt,
        string assetType,
        string visualStyle,
        string viewAngle,
        int outputSize)
    {
        prompt = string.IsNullOrWhiteSpace(prompt) ? "fantasy game asset" : prompt.Trim();
        prompt = TranslateChineseGamePrompt(prompt);

        var isBuilding = IsBuilding(prompt);
        var assetPrefix = isBuilding
            ? "single freestanding RPG map building, architectural tileset object"
            : assetType switch
        {
            "scene" => "compact RPG environment scene",
            "tile" => "seamless RPG terrain tile",
            "character" => "full-body RPG character sprite",
            "icon" => "single RPG inventory icon",
            _ => "single RPG world object sprite"
        };

        var styleConstraint = visualStyle switch
        {
            "detailed64" => "detailed 64x64 RPG pixel art, controlled clusters, 18-color hand-picked palette, layered materials",
            "retro16" => "retro 16-bit JRPG pixel art, chunky clusters, 10-color console palette, simple bold shading",
            "darkFantasy" => "dark fantasy RPG pixel art, muted stone palette, deep shadows, restrained highlights, worn materials",
            "cozyRpg" => "cozy life RPG pixel art, warm pastel palette, soft forms made of crisp clusters, friendly readable shapes",
            "tacticalSciFi" => "tactical science-fiction RPG pixel art, cool industrial palette, modular shapes, sharp panel highlights",
            _ => "classic 32x32 RPG pixel art, chunky deliberate clusters, 12-color hand-picked palette, strong readable silhouette"
        };

        var viewConstraint = GetViewConstraint(assetType, viewAngle);
        var subjectConstraint = GetSubjectConstraint(prompt);
        var composition = assetType switch
        {
            "tile" => "edge-to-edge pattern, no perspective, uniform lighting",
            "scene" => "clear focal point, large separated material regions",
            _ => "entire object visible, centered, one object only, clean silhouette"
        };

        var densityConstraint = outputSize <= 32
            ? "very low detail density, features readable on a 32x32 grid"
            : "medium detail density, features grouped for a 64x64 grid";

        return string.Join(", ", new[]
        {
            subjectConstraint,
            prompt,
            assetPrefix,
            "pixelart_style",
            styleConstraint,
            viewConstraint,
            composition,
            densityConstraint,
            "crisp square pixel clusters, hard edges, no anti-aliasing"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static string ComposeNegative(
        string negativePrompt,
        string translatedPrompt,
        string assetType,
        string viewAngle)
    {
        const string required = "photo, photorealistic, 3d render, vector art, smooth gradients, blurry, anti-aliasing, " +
                                "subpixel noise, scattered pixels, excessive texture, text, watermark, collage, duplicate objects";
        var subjectNegative = IsBuilding(translatedPrompt)
            ? "person, face, portrait, character, creature, monster, mushroom, tree, food, item icon, vending machine, arcade cabinet, appliance, furniture, facade fragment, cropped building, melted architecture, continuous window bands, blank facade"
            : string.Empty;
        var rockNegative = IsStandaloneRock(translatedPrompt)
            ? "rainbow, multicolored, neon colors, saturated colors, crystal, gemstone, ore, geode, candy, slime, liquid blob, abstract shape, flowers, container, machine"
            : string.Empty;
        var viewNegative = viewAngle switch
        {
            "isometric45" => "flat front elevation, side view, perspective lens distortion",
            "frontHigh" => "rear view, extreme top-down, flat icon view",
            "topDown" => "eye-level front view, horizon, side elevation",
            "front" => "top-down view, isometric view, side view",
            "side" => "front view, top-down view, isometric view",
            _ => string.Empty
        };
        var tileNegative = assetType == "tile" ? "object sprite, horizon, border, frame" : string.Empty;

        return string.Join(", ", new[] { negativePrompt?.Trim(), required, subjectNegative, rockNegative, viewNegative, tileNegative }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string GetViewConstraint(string assetType, string viewAngle)
    {
        if (assetType == "tile")
            return "strict orthographic top-down view, camera perpendicular to ground";

        return viewAngle switch
        {
            "isometric45" => "three-quarter isometric view, camera yaw 45 degrees, front and side visible, orthographic projection",
            "frontHigh" => "elevated front view, camera looking slightly downward, front dominant, roof partly visible",
            "topDown" => "strict orthographic top-down view, roof and footprint readable, no horizon",
            "front" => "straight front elevation, centered symmetry, no side wall visible",
            "side" => "strict side elevation, single side silhouette, no front facade",
            _ => assetType == "scene"
                ? "three-quarter top-down RPG map view, orthographic camera"
                : "three-quarter front view, slightly elevated orthographic camera"
        };
    }

    private static string GetSubjectConstraint(string prompt)
    {
        if (IsStandaloneRock(prompt))
            return "exactly one natural gray rock boulder, squat irregular rounded mass, broad flat stone facets, heavy solid silhouette, dark gray underside, restrained gray-brown palette";
        if (prompt.Contains("office building", StringComparison.OrdinalIgnoreCase))
            return "modern rectangular office building, complete freestanding mass, glass curtain wall, separate square windows with wall gaps, five readable floors, centered glass double-door entrance";
        if (IsBuilding(prompt))
            return "complete freestanding building, clear roof and entrance, repeated structural rhythm, entire architecture inside frame";
        if (prompt.Contains("tree", StringComparison.OrdinalIgnoreCase))
            return "distinct trunk, grouped canopy masses, visible roots, no scattered leaf noise";
        if (prompt.Contains("character", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("warrior", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("mage", StringComparison.OrdinalIgnoreCase))
            return "clear head torso arms and legs, readable pose, consistent anatomy";
        return string.Empty;
    }

    private static bool IsBuilding(string prompt)
        => prompt.Contains("building", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("house", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("cottage", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("castle", StringComparison.OrdinalIgnoreCase)
           || prompt.Contains("tower", StringComparison.OrdinalIgnoreCase);

    private static bool IsStandaloneRock(string prompt)
    {
        var hasRock = prompt.Contains("rock", StringComparison.OrdinalIgnoreCase)
                      || prompt.Contains("boulder", StringComparison.OrdinalIgnoreCase)
                      || prompt.Contains("natural stone", StringComparison.OrdinalIgnoreCase);
        if (!hasRock)
            return false;

        return !prompt.Contains("cobblestone", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("flagstone", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("stone wall", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("stone floor", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("marble", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("gemstone", StringComparison.OrdinalIgnoreCase)
               && !prompt.Contains("crystal", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBuildingPrompt(string prompt) => IsBuilding(prompt);

    internal static string TranslateChineseGamePrompt(string prompt)
    {
        if (!prompt.Any(IsCjk)) return prompt;

        var occupied = new bool[prompt.Length];
        var matches = new List<(int Position, string English)>();
        foreach (var term in ChineseTerms.OrderByDescending(item => item.Chinese.Length))
        {
            var start = 0;
            while (start < prompt.Length)
            {
                var position = prompt.IndexOf(term.Chinese, start, StringComparison.Ordinal);
                if (position < 0) break;
                var overlaps = false;
                for (var index = position; index < position + term.Chinese.Length; index++)
                    overlaps |= occupied[index];
                if (!overlaps)
                {
                    for (var index = position; index < position + term.Chinese.Length; index++)
                        occupied[index] = true;
                    matches.Add((position, term.English));
                    break;
                }
                start = position + 1;
            }
        }

        var englishParts = matches
            .OrderBy(match => match.Position)
            .Select(match => match.English)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Preserve English fragments from mixed Chinese/English prompts.
        var ascii = new StringBuilder();
        foreach (var character in prompt)
        {
            if (character <= 127 && (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)
                || character is '-' or '_' or '\''))
                ascii.Append(character);
            else if (ascii.Length > 0 && ascii[^1] != ' ')
                ascii.Append(' ');
        }
        var preservedEnglish = string.Join(' ', ascii.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrWhiteSpace(preservedEnglish))
            englishParts.Insert(0, preservedEnglish);

        return englishParts.Count > 0
            ? string.Join(", ", englishParts)
            : "fantasy RPG game asset";
    }

    private static bool IsCjk(char character)
        => character is >= '\u3400' and <= '\u9fff';
}
