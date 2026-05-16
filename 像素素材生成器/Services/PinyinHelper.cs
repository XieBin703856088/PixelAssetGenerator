using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Provides Chinese character to pinyin conversion for search support.
/// </summary>
public static class PinyinHelper
{
    private static readonly Dictionary<char, string> PinyinMap = new()
    {
        ['纯'] = "chun", ['色'] = "se", ['噪'] = "zao", ['波'] = "bo",
        ['渐'] = "jian", ['变'] = "bian", ['棋'] = "qi", ['盘'] = "pan",
        ['木'] = "mu", ['纹'] = "wen", ['云'] = "yun", ['彩'] = "cai",
        ['大'] = "da", ['理'] = "li", ['石'] = "shi", ['地'] = "di",
        ['形'] = "xing", ['水'] = "shui", ['晶'] = "jing", ['鳞'] = "lin",
        ['片'] = "pian", ['铁'] = "tie", ['锈'] = "xiu", ['岩'] = "yan",
        ['浆'] = "jiang", ['旧'] = "jiu", ['版'] = "ban", ['生'] = "sheng",
        ['成'] = "cheng", ['器'] = "qi", ['灌'] = "guan", ['蘑'] = "mo",
        ['菇'] = "gu", ['药'] = "yao", ['盾'] = "dun", ['牌'] = "pai",
        ['武'] = "wu", ['宝'] = "bao", ['命'] = "ming", ['条'] = "tiao",
        ['火'] = "huo", ['焰'] = "yan", ['闪'] = "shan", ['电'] = "dian",
        ['烟'] = "yan", ['雾'] = "wu", ['雨'] = "yu", ['雪'] = "xue",
        ['黏'] = "nian", ['液'] = "ye", ['等'] = "deng", ['离'] = "li",
        ['子'] = "zi", ['符'] = "fu", ['文'] = "wen", ['传'] = "chuan",
        ['送'] = "song", ['门'] = "men", ['能'] = "neng", ['量'] = "liang",
        ['场'] = "chang", ['全'] = "quan", ['息'] = "xi", ['图'] = "tu",
        ['角'] = "jiao", ['梯'] = "ti", ['尖'] = "jian", ['刺'] = "ci",
        ['陷'] = "xian", ['阱'] = "jing", ['炬'] = "ju", ['树'] = "shu",
        ['帧'] = "zhen", ['序'] = "xu", ['列'] = "lie", ['参'] = "can",
        ['数'] = "shu", ['动'] = "dong", ['画'] = "hua", ['纺'] = "fang",
        ['织'] = "zhi", ['编'] = "bian", ['砖'] = "zhuan", ['块'] = "kuai",
        ['细'] = "xi", ['胞'] = "bao", ['状'] = "zhuang", ['圆'] = "yuan",
        ['斑'] = "ban", ['点'] = "dian", ['格'] = "ge", ['栅'] = "zha",
        ['同'] = "tong", ['心'] = "xin", ['螺'] = "luo", ['旋'] = "xuan",
        ['蜂'] = "feng", ['窝'] = "wo", ['浪'] = "lang", ['路'] = "lu",
        ['板'] = "ban", ['颜'] = "yan", ['量'] = "liang", ['化'] = "hua",
        ['像'] = "xiang", ['素'] = "su", ['轮'] = "lun", ['廓'] = "kuo",
        ['调'] = "tiao", ['映'] = "ying", ['射'] = "she", ['分'] = "fen",
        ['析'] = "xi", ['语'] = "yu", ['义'] = "yi", ['控'] = "kong",
        ['制'] = "zhi", ['件'] = "jian", ['选'] = "xuan", ['择'] = "ze",
        ['体'] = "ti", ['屋'] = "wu", ['顶'] = "ding", ['墙'] = "qiang",
        ['壁'] = "bi", ['窗'] = "chuang", ['户'] = "hu", ['柱'] = "zhu",
        ['拱'] = "gong", ['阳'] = "yang", ['台'] = "tai", ['雕'] = "diao",
        ['座'] = "zuo", ['本'] = "ben", ['提'] = "ti", ['取'] = "qu",
        ['类'] = "lei", ['杂'] = "za", ['颜'] = "yan", ['色'] = "se",
        ['提'] = "ti", ['取'] = "qu", ['像'] = "xiang", ['素'] = "su",
        ['轮'] = "lun", ['廓'] = "kuo", ['调'] = "tiao", ['色'] = "se",
        ['板'] = "ban", ['映'] = "ying", ['射'] = "she", ['纤'] = "xian",
        ['维'] = "wei", ['编'] = "bian", ['织'] = "zhi", ['纹'] = "wen",
        ['细'] = "xi", ['胞'] = "bao", ['圆'] = "yuan", ['形'] = "xing",
        ['斑'] = "ban", ['点'] = "dian", ['格'] = "ge", ['栅'] = "zha",
        ['同'] = "tong", ['心'] = "xin", ['螺'] = "luo", ['旋'] = "xuan",
        ['蜂'] = "feng", ['窝'] = "wo", ['波'] = "bo", ['浪'] = "lang",
        ['电'] = "dian", ['路'] = "lu", ['颜'] = "yan", ['色'] = "se",
        ['量'] = "liang", ['化'] = "hua", ['像'] = "xiang", ['素'] = "su",
        ['轮'] = "lun", ['廓'] = "kuo", ['分'] = "fen", ['析'] = "xi",
        ['语'] = "yu", ['义'] = "yi", ['控'] = "kong", ['制'] = "zhi",
        ['条'] = "tiao", ['件'] = "jian", ['选'] = "xuan", ['择'] = "ze",
        ['变'] = "bian", ['体'] = "ti", ['屋'] = "wu", ['顶'] = "ding",
        ['墙'] = "qiang", ['壁'] = "bi", ['地'] = "di", ['板'] = "ban",
        ['窗'] = "chuang", ['户'] = "hu", ['柱'] = "zhu", ['子'] = "zi",
        ['拱'] = "gong", ['门'] = "men", ['阳'] = "yang", ['台'] = "tai",
        ['雕'] = "diao", ['像'] = "xiang", ['文'] = "wen", ['本'] = "ben",
        ['颜'] = "yan", ['色'] = "se", ['提'] = "ti", ['取'] = "qu",
        ['旧'] = "jiu", ['版'] = "ban", ['生'] = "sheng", ['成'] = "cheng",
        ['器'] = "qi", ['生'] = "sheng", ['命'] = "ming", ['条'] = "tiao",
        ['帧'] = "zhen", ['序'] = "xu", ['列'] = "lie", ['参'] = "can",
        ['数'] = "shu", ['动'] = "dong", ['画'] = "hua", ['U'] = "u",
        ['I'] = "i", ['面'] = "mian", ['板'] = "ban", ['按'] = "an",
        ['钮'] = "niu", ['图'] = "tu", ['标'] = "biao",
    };

    /// <summary>
    /// Gets the first letter of the pinyin for each Chinese character in the text.
    /// Non-Chinese characters are kept as-is (lowercased).
    /// </summary>
    public static string GetInitials(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (PinyinMap.TryGetValue(c, out var py) && py.Length > 0)
                sb.Append(py[0]);
            else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9')
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns the full pinyin (without tones) for each character.
    /// Non-Chinese characters are kept as-is.
    /// </summary>
    public static string GetPinyin(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (PinyinMap.TryGetValue(c, out var py))
                sb.Append(py);
            else
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks whether <paramref name="query"/> matches <paramref name="text"/>
    /// by Chinese pinyin initials or full pinyin.
    /// If query is all uppercase letters, match against initials.
    /// Otherwise match against full pinyin.
    /// Also falls back to substring match on the original text.
    /// </summary>
    public static bool MatchesPinyin(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(text)) return false;

        var qLower = query.ToLowerInvariant();

        // Direct substring match (Chinese or English)
        if (text.IndexOf(qLower, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Check if query is all uppercase letters → match initials
        if (query.All(c => c >= 'A' && c <= 'Z'))
        {
            var initials = GetInitials(text);
            if (initials.Contains(qLower))
                return true;
        }

        // Full pinyin match
        var pinyin = GetPinyin(text);
        if (pinyin.Contains(qLower))
            return true;

        // Initials match (even if query is mixed case)
        var initials2 = GetInitials(text);
        if (initials2.Contains(qLower))
            return true;

        return false;
    }
}
