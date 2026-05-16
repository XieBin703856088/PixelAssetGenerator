using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    /// <summary>
    /// Fallback localization table for choice values that come from hardcoded C# nodes
    /// (those nodes call NodeParameterDefinition.Choice without display labels).
    /// When NodeParameterViewModel receives choices == displayChoices, it applies this map.
    /// </summary>
    internal static class ChoiceValueTranslations
    {
        internal static readonly Dictionary<string, string> ZhHans = new(StringComparer.Ordinal)
        {
            // Blend modes
            { "normal", "正常" }, { "multiply", "正片叠底" }, { "screen", "滤色" },
            { "overlay", "叠加" }, { "softLight", "柔光" }, { "hardLight", "强光" },
            { "difference", "差值" }, { "exclusion", "排除" }, { "linearDodge", "线性减淡" },
            { "linearBurn", "线性加深" }, { "colorDodge", "颜色减淡" }, { "colorBurn", "颜色加深" },
            { "lighter", "变亮" }, { "darker", "变暗" }, { "hue", "色相/色调" },
            { "saturation", "饱和度" }, { "color", "颜色" }, { "luminosity", "明度" },
            { "dissolve", "溶解" },
            // Blur types
            { "gaussian", "高斯模糊" }, { "box", "方框模糊" }, { "motion", "运动模糊" },
            { "radial", "径向" },
            // Shapes / geometry (shared across shape, wave, tree, etc.)
            { "circle", "圆形" }, { "ellipse", "椭圆" }, { "rectangle", "矩形" },
            { "diamond", "菱形" }, { "triangle", "三角形" }, { "pentagon", "五边形" },
            { "hexagon", "六边形" }, { "star", "星形" }, { "cross", "十字" },
            { "ring", "环形" }, { "sphere", "球体" }, { "square", "方形" },
            { "teardrop", "水滴" }, { "custom", "自定义" }, { "irregular", "不规则" },
            { "round", "圆形" }, { "conical", "锥形" },
            // Directions
            { "horizontal", "水平" }, { "vertical", "垂直" }, { "diagonal", "对角线" },
            { "both", "双向" }, { "top", "顶部" }, { "topLeft", "左上" },
            { "topRight", "右上" }, { "bottomLeft", "左下" }, { "bottomRight", "右下" },
            // Dithering
            { "none", "无" }, { "bayer4x4", "Bayer 4x4" }, { "bayer8x8", "Bayer 8x8" },
            { "floydSteinberg", "Floyd-Steinberg" }, { "atkinson", "Atkinson" },
            { "ordered4x4", "有序 4x4" },
            // Color/channel
            { "luminance", "亮度" }, { "red", "红通道" }, { "green", "绿通道" },
            { "blue", "蓝通道" }, { "alpha", "透明通道" }, { "RGB", "RGB" },
            { "HSVWeighted", "HSV加权" }, { "perceptual", "感知" }, { "HSV", "HSV" },
            { "R", "R通道" }, { "G", "G通道" }, { "B", "B通道" }, { "A", "Alpha通道" },
            { "grayscale", "灰度" },
            // Noise types
            { "Value", "值噪声" }, { "Gradient", "梯度噪声" }, { "Voronoi", "沃罗诺伊" },
            { "Ridge", "脊状噪声" }, { "Turbulence", "湍流噪声" },
            { "filmGrain", "胶片颗粒" }, { "colorNoise", "色彩噪声" },
            { "dustScratches", "尘埃划痕" }, { "snow", "雪花" }, { "pixelJitter", "像素抖动" },
            // Gradient modes
            { "linearHorizontal", "线性水平" }, { "linearVertical", "线性垂直" },
            { "angular", "角度" },
            // Math operations
            { "add", "加" }, { "subtract", "减" }, { "divide", "除" },
            { "max", "取最大" }, { "min", "取最小" },
            { "absoluteDifference", "绝对差" }, { "screenBlend", "滤色混合" },
            // Outline/shadow positions
            { "outside", "外侧" }, { "inside", "内侧" },
            { "dropShadow", "投影" }, { "innerShadow", "内阴影" }, { "glow", "发光" },
            // Sharpen types
            { "unsharpMask", "USM锐化" }, { "laplacian", "拉普拉斯" }, { "edgeEnhance", "边缘增强" },
            // Threshold modes
            { "blackWhite", "黑白" }, { "preserveColor", "保留颜色" }, { "bandPass", "带通" },
            // Mirror modes
            { "mirrorHorizontal", "水平镜像" }, { "mirrorVertical", "垂直镜像" },
            { "mirrorDiagonal", "对角镜像" }, { "mirrorQuad", "四象限镜像" },
            { "quadrant", "四象限" }, { "eighth", "八分" }, { "checkerboard", "棋盘格" },
            // Tile layouts
            { "grid", "网格" }, { "horizontalStrip", "水平条带" }, { "verticalStrip", "垂直条带" },
            { "brick", "砖块" }, { "dome", "穹顶" }, { "flat", "平面" },
            // Sample modes
            { "center", "中心" }, { "average", "平均" }, { "nearest", "最近邻" },
            { "uniform", "均匀采样" }, { "kmeans", "K-均值" },
            // Wave types
            { "sine", "正弦" }, { "sawtooth", "锯齿" }, { "ripple", "波纹" },
            // Cloud types
            { "cumulus", "积云" }, { "stratus", "层云" }, { "cirrus", "卷云" }, { "mixed", "混合" },
            // Tree / plant
            { "layered", "分层" }, { "palm", "棕榈" }, { "cactus", "仙人掌" },
            // Wall types
            { "stone", "石头" }, { "plaster", "灰泥" }, { "planks", "木板" }, { "adobe", "土坯" },
            // Floor types
            { "herringbone", "人字纹" }, { "stoneTile", "石砖" }, { "carpet", "地毯" },
            // Fire types
            { "campfire", "篝火" }, { "torch", "火炬" }, { "magicFire", "魔法火焰" }, { "wildfire", "野火" },
            // Smoke types
            { "thick", "浓烟" }, { "mist", "薄雾" }, { "steam", "蒸汽" }, { "dust", "尘埃" },
            // Glow types
            { "soft", "柔和" }, { "edgeGlow", "边缘发光" }, { "neon", "霓虹" },
            // Stylize
            { "cartoon", "卡通" }, { "oilPaint", "油画" }, { "emboss", "浮雕" },
            { "pixelOutline", "像素轮廓" }, { "posterize", "色调分离" }, { "watercolor", "水彩" },
            // Distort types
            { "swirl", "漩涡" }, { "pinch", "收缩" }, { "polar", "极坐标" },
            // Fabric types
            { "twill", "斜纹" }, { "satin", "缎面" },
            // Rune styles
            { "norse", "北欧" }, { "magic", "魔法" }, { "ancient", "古代" }, { "holy", "神圣" },
            // Rust/corrosion
            { "ironRust", "铁锈" }, { "patina", "铜绿" },
            // Scanlines
            { "horizontalScanlines", "水平扫描线" }, { "verticalScanlines", "垂直扫描线" },
            { "shadowMask", "阴影掩模" },
            // Interpolation
            { "linear", "线性" }, { "cosine", "余弦" }, { "cubic", "三次" },
            // Spiral types
            { "archimedean", "阿基米德螺旋" }, { "logarithmic", "对数螺旋" },
            // Shield types
            { "kite", "风筝形" }, { "tower", "塔形" },
            // Spatter / splatter
            { "cluster", "簇状" }, { "geode", "晶洞" },
            // Crystal / misc
            { "noise", "噪声" },
            // Condition operators
            { "greaterThan", "大于" }, { "lessThan", "小于" }, { "equalTo", "等于" }, { "inRange", "范围内" },
            // Convolution presets
            { "boxBlur", "方框模糊" }, { "gaussianBlur", "高斯模糊" }, { "sharpen", "锐化" },
            { "edgeDetect", "边缘检测" }, { "outline", "描边" }, { "engrave", "雕刻" },
            // Gradient map
            { "dualColor", "双色" }, { "gradientRamp", "渐变坡度" },
            // Color extraction sizes
            { "small", "小" }, { "medium", "中" }, { "large", "大" },
            // Constant types
            { "float", "浮点数" },
            // Animation play modes
            { "loop", "循环" }, { "pingPong", "乒乓" }, { "singleShot", "单次" },
            { "crossFade", "交叉淡入淡出" }, { "slide", "滑动" },
            // UI Button states
            { "hover", "悬停" }, { "pressed", "按下" },
            // Variation modes
            { "seedScramble", "种子扰动" }, { "parameterNoise", "参数噪声" }, { "styleBlend", "风格混合" },
            // Backgrounds / fill
            { "transparent", "透明" }, { "black", "黑色" }, { "white", "白色" },
            // Light types
            { "directional", "平行光" }, { "point", "点光源" }, { "hemisphere", "半球光" },
            // Mask channels
            { "redChannel", "红通道" }, { "greenChannel", "绿通道" },
            { "blueChannel", "蓝通道" }, { "alphaChannel", "Alpha通道" },
            // Output formats (keep as-is, already clear)
            { "PNG", "PNG" }, { "BMP", "BMP" },
            // Cache keys
            { "cache1", "缓存1" }, { "cache2", "缓存2" }, { "cache3", "缓存3" },
            { "cache4", "缓存4" }, { "cache5", "缓存5" },
            // Health bar shapes
            { "health", "生命" }, { "mana", "魔法" }, { "stamina", "体力" },
            { "heart", "心形" }, { "gear", "齿轮" }, { "mapMarker", "地图标记" },
            { "trophy", "奖杯" }, { "skull", "骷髅" },
            // Icon types
            { "basic", "基础" }, { "structure", "结构" },
            // Analysis modes
            { "all", "全部" },
            // Gradient/concentric patterns
            { "standard", "标准" }, { "spot", "点状" }, { "stripe", "条纹" }, { "fragment", "碎片" },
            // Cap types
            { "pointed", "尖角" }, { "rounded", "圆角" }, { "vShape", "V形" },
            // Top shapes (fence/bevel etc.)
            { "sharp", "锐利" },
            // Spacing modes
            { "index", "索引" }, { "threshold", "阈值" },
            // Selection modes
            { "replace", "替换" },
            // Colorize / GradientMap modes
            { "single", "单色" },
            // Output size (numeric strings — keep as-is)
            { "8", "8" }, { "16", "16" }, { "32", "32" }, { "48", "48" },
            { "64", "64" }, { "96", "96" }, { "128", "128" }, { "256", "256" }, { "512", "512" },
            // Pixel art categories
            { "pixelArt", "像素画" }, { "pixelCharacter", "像素角色" },
            { "pixelScene", "像素场景" }, { "pixelIcon", "像素图标" }, { "pixelUI", "像素UI" },
            { "random", "随机" },
            // Layout hexagonal
            { "hexagonal", "六边形" },
            // Cap/shape for nodes
            { "default", "默认" },
        };

        /// <summary>
        /// English translations for choice values — used when UI culture is English.
        /// Keys that map to themselves (e.g. "circle" → "Circle") are included for clarity
        /// and to distinguish translated entries from missing ones.
        /// </summary>
        internal static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
        {
            { "normal", "Normal" }, { "multiply", "Multiply" }, { "screen", "Screen" },
            { "overlay", "Overlay" }, { "softLight", "Soft Light" }, { "hardLight", "Hard Light" },
            { "difference", "Difference" }, { "exclusion", "Exclusion" }, { "linearDodge", "Linear Dodge" },
            { "linearBurn", "Linear Burn" }, { "colorDodge", "Color Dodge" }, { "colorBurn", "Color Burn" },
            { "lighter", "Lighter" }, { "darker", "Darker" }, { "hue", "Hue" },
            { "saturation", "Saturation" }, { "color", "Color" }, { "luminosity", "Luminosity" },
            { "dissolve", "Dissolve" },
            { "gaussian", "Gaussian" }, { "box", "Box" }, { "motion", "Motion" },
            { "radial", "Radial" },
            { "circle", "Circle" }, { "ellipse", "Ellipse" }, { "rectangle", "Rectangle" },
            { "diamond", "Diamond" }, { "triangle", "Triangle" }, { "pentagon", "Pentagon" },
            { "hexagon", "Hexagon" }, { "star", "Star" }, { "cross", "Cross" },
            { "ring", "Ring" }, { "sphere", "Sphere" }, { "square", "Square" },
            { "teardrop", "Teardrop" }, { "custom", "Custom" }, { "irregular", "Irregular" },
            { "round", "Round" }, { "conical", "Conical" },
            { "horizontal", "Horizontal" }, { "vertical", "Vertical" }, { "diagonal", "Diagonal" },
            { "both", "Both" }, { "top", "Top" }, { "topLeft", "Top-Left" },
            { "topRight", "Top-Right" }, { "bottomLeft", "Bottom-Left" }, { "bottomRight", "Bottom-Right" },
            { "none", "None" }, { "bayer4x4", "Bayer 4x4" }, { "bayer8x8", "Bayer 8x8" },
            { "floydSteinberg", "Floyd-Steinberg" }, { "atkinson", "Atkinson" },
            { "ordered4x4", "Ordered 4x4" },
            { "luminance", "Luminance" }, { "red", "Red Channel" }, { "green", "Green Channel" },
            { "blue", "Blue Channel" }, { "alpha", "Alpha Channel" }, { "RGB", "RGB" },
            { "HSVWeighted", "HSV Weighted" }, { "perceptual", "Perceptual" }, { "HSV", "HSV" },
            { "R", "R Channel" }, { "G", "G Channel" }, { "B", "B Channel" }, { "A", "Alpha" },
            { "grayscale", "Grayscale" },
            { "Value", "Value" }, { "Gradient", "Gradient" }, { "Voronoi", "Voronoi" },
            { "Ridge", "Ridge" }, { "Turbulence", "Turbulence" },
            { "filmGrain", "Film Grain" }, { "colorNoise", "Color Noise" },
            { "dustScratches", "Dust & Scratches" }, { "snow", "Snow" }, { "pixelJitter", "Pixel Jitter" },
            { "linearHorizontal", "Linear Horizontal" }, { "linearVertical", "Linear Vertical" },
            { "angular", "Angular" },
            { "add", "Add" }, { "subtract", "Subtract" }, { "divide", "Divide" },
            { "max", "Max" }, { "min", "Min" },
            { "absoluteDifference", "Absolute Difference" }, { "screenBlend", "Screen Blend" },
            { "outside", "Outside" }, { "inside", "Inside" },
            { "dropShadow", "Drop Shadow" }, { "innerShadow", "Inner Shadow" }, { "glow", "Glow" },
            { "unsharpMask", "Unsharp Mask" }, { "laplacian", "Laplacian" }, { "edgeEnhance", "Edge Enhance" },
            { "blackWhite", "Black & White" }, { "preserveColor", "Preserve Color" }, { "bandPass", "Band Pass" },
            { "mirrorHorizontal", "Mirror Horizontal" }, { "mirrorVertical", "Mirror Vertical" },
            { "mirrorDiagonal", "Mirror Diagonal" }, { "mirrorQuad", "Mirror Quad" },
            { "quadrant", "Quadrant" }, { "eighth", "Eighth" }, { "checkerboard", "Checkerboard" },
            { "grid", "Grid" }, { "horizontalStrip", "Horizontal Strip" }, { "verticalStrip", "Vertical Strip" },
            { "brick", "Brick" }, { "dome", "Dome" }, { "flat", "Flat" },
            { "center", "Center" }, { "average", "Average" }, { "nearest", "Nearest Neighbor" },
            { "uniform", "Uniform" }, { "kmeans", "K-Means" },
            { "sine", "Sine" }, { "sawtooth", "Sawtooth" }, { "ripple", "Ripple" },
            { "cumulus", "Cumulus" }, { "stratus", "Stratus" }, { "cirrus", "Cirrus" }, { "mixed", "Mixed" },
            { "layered", "Layered" }, { "palm", "Palm" }, { "cactus", "Cactus" },
            { "stone", "Stone" }, { "plaster", "Plaster" }, { "planks", "Planks" }, { "adobe", "Adobe" },
            { "herringbone", "Herringbone" }, { "stoneTile", "Stone Tile" }, { "carpet", "Carpet" },
            { "campfire", "Campfire" }, { "torch", "Torch" }, { "magicFire", "Magic Fire" }, { "wildfire", "Wildfire" },
            { "thick", "Thick" }, { "mist", "Mist" }, { "steam", "Steam" }, { "dust", "Dust" },
            { "soft", "Soft" }, { "edgeGlow", "Edge Glow" }, { "neon", "Neon" },
            { "cartoon", "Cartoon" }, { "oilPaint", "Oil Paint" }, { "emboss", "Emboss" },
            { "pixelOutline", "Pixel Outline" }, { "posterize", "Posterize" }, { "watercolor", "Watercolor" },
            { "swirl", "Swirl" }, { "pinch", "Pinch" }, { "polar", "Polar" },
            { "twill", "Twill" }, { "satin", "Satin" },
            { "norse", "Norse" }, { "magic", "Magic" }, { "ancient", "Ancient" }, { "holy", "Holy" },
            { "ironRust", "Iron Rust" }, { "patina", "Patina" },
            { "horizontalScanlines", "Horizontal Scanlines" }, { "verticalScanlines", "Vertical Scanlines" },
            { "shadowMask", "Shadow Mask" },
            { "linear", "Linear" }, { "cosine", "Cosine" }, { "cubic", "Cubic" },
            { "archimedean", "Archimedean" }, { "logarithmic", "Logarithmic" },
            { "kite", "Kite" }, { "tower", "Tower" },
            { "cluster", "Cluster" }, { "geode", "Geode" },
            { "noise", "Noise" },
            { "greaterThan", "Greater Than" }, { "lessThan", "Less Than" }, { "equalTo", "Equal To" }, { "inRange", "In Range" },
            { "boxBlur", "Box Blur" }, { "gaussianBlur", "Gaussian Blur" }, { "sharpen", "Sharpen" },
            { "edgeDetect", "Edge Detect" }, { "outline", "Outline" }, { "engrave", "Engrave" },
            { "dualColor", "Dual Color" }, { "gradientRamp", "Gradient Ramp" },
            { "small", "Small" }, { "medium", "Medium" }, { "large", "Large" },
            { "float", "Float" },
            { "loop", "Loop" }, { "pingPong", "Ping-Pong" }, { "singleShot", "Single Shot" },
            { "crossFade", "Cross-Fade" }, { "slide", "Slide" },
            { "hover", "Hover" }, { "pressed", "Pressed" },
            { "seedScramble", "Seed Scramble" }, { "parameterNoise", "Parameter Noise" }, { "styleBlend", "Style Blend" },
            { "transparent", "Transparent" }, { "black", "Black" }, { "white", "White" },
            { "directional", "Directional" }, { "point", "Point" }, { "hemisphere", "Hemisphere" },
            { "redChannel", "Red Channel" }, { "greenChannel", "Green Channel" },
            { "blueChannel", "Blue Channel" }, { "alphaChannel", "Alpha Channel" },
            { "PNG", "PNG" }, { "BMP", "BMP" },
            { "cache1", "Cache 1" }, { "cache2", "Cache 2" }, { "cache3", "Cache 3" },
            { "cache4", "Cache 4" }, { "cache5", "Cache 5" },
            { "health", "Health" }, { "mana", "Mana" }, { "stamina", "Stamina" },
            { "heart", "Heart" }, { "gear", "Gear" }, { "mapMarker", "Map Marker" },
            { "trophy", "Trophy" }, { "skull", "Skull" },
            { "basic", "Basic" }, { "structure", "Structure" },
            { "all", "All" },
            { "standard", "Standard" }, { "spot", "Spot" }, { "stripe", "Stripe" }, { "fragment", "Fragment" },
            { "pointed", "Pointed" }, { "rounded", "Rounded" }, { "vShape", "V-Shape" },
            { "sharp", "Sharp" },
            { "index", "Index" }, { "threshold", "Threshold" },
            { "replace", "Replace" },
            { "single", "Single" },
            { "8", "8" }, { "16", "16" }, { "32", "32" }, { "48", "48" },
            { "64", "64" }, { "96", "96" }, { "128", "128" }, { "256", "256" }, { "512", "512" },
            { "pixelArt", "Pixel Art" }, { "pixelCharacter", "Pixel Character" },
            { "pixelScene", "Pixel Scene" }, { "pixelIcon", "Pixel Icon" }, { "pixelUI", "Pixel UI" },
            { "random", "Random" },
            { "hexagonal", "Hexagonal" },
            { "default", "Default" },
        };

        /// <summary>Gets the translation dictionary for the specified culture code.</summary>
        internal static IReadOnlyDictionary<string, string> ForCulture(string cultureCode)
        {
            if (cultureCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return ZhHans;
            if (cultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return En;
            // Fallback to empty dictionary for unsupported cultures
            return Empty;
        }

        private static readonly Dictionary<string, string> Empty = new();
    }

    public enum NodeParameterKind
    {
        Number,
        Integer,
        Boolean,
        Choice,
        PointList,
        Color,
        Seed,
        Text
    }

    public sealed class NodeParameterDefinition
    {
        /// <summary>Canonical key for dictionary lookups (e.g., "seed", "scale").</summary>
        public string Name { get; }
        /// <summary>Localized display name for UI (e.g., "种子", "缩放").</summary>
        public string DisplayName { get; }
        public NodeParameterKind Kind { get; }
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }
        public double DefaultNumber { get; }
        public int DefaultInt { get; }
        public bool DefaultBool { get; }
        public string? DefaultChoice { get; }
        public IReadOnlyList<string> Choices { get; }
        /// <summary>Localized display labels for Choices (same order). May equal Choices if no localization.</summary>
        public IReadOnlyList<string> DisplayChoices { get; }
        public System.Windows.Media.Color DefaultColor { get; }
        public string DefaultText { get; }

        private NodeParameterDefinition(string name,
            string displayName,
            NodeParameterKind kind,
            double min,
            double max,
            double step,
            double defaultNumber,
            int defaultInt,
            bool defaultBool,
            string? defaultChoice,
            IReadOnlyList<string>? choices,
            System.Windows.Media.Color defaultColor = default,
            string defaultText = "",
            IReadOnlyList<string>? displayChoices = null)
        {
            Name = name;
            DisplayName = displayName;
            Kind = kind;
            Min = min;
            Max = max;
            Step = step;
            DefaultNumber = defaultNumber;
            DefaultInt = defaultInt;
            DefaultBool = defaultBool;
            DefaultChoice = defaultChoice;
            Choices = choices ?? Array.Empty<string>();
            DisplayChoices = displayChoices ?? Choices;
            DefaultColor = defaultColor;
            DefaultText = defaultText;
        }

        public static NodeParameterDefinition Number(string name, double defaultValue, double min, double max, double step, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Number, min, max, step, defaultValue, 0, false, null, null);
        }

        public static NodeParameterDefinition Integer(string name, int defaultValue, int min, int max, int step, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Integer, min, max, step, 0d, defaultValue, false, null, null);
        }

        public static NodeParameterDefinition Boolean(string name, bool defaultValue, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Boolean, 0d, 1d, 1d, 0d, 0, defaultValue, null, null);
        }

        public static NodeParameterDefinition Choice(string name, string defaultValue, IReadOnlyList<string> choices, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Choice, 0d, 0d, 0d, 0d, 0, false, defaultValue, choices);
        }

        public static NodeParameterDefinition Choice(string name, string defaultValue, IReadOnlyList<string> choices, IReadOnlyList<string> displayChoices, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Choice, 0d, 0d, 0d, 0d, 0, false, defaultValue, choices, displayChoices: displayChoices);
        }

        public static NodeParameterDefinition PointList(string name, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.PointList, 0d, 0d, 0d, 0d, 0, false, null, null);
        }

        public static NodeParameterDefinition Color(string name, System.Windows.Media.Color defaultColor, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Color, 0d, 0d, 0d, 0d, 0, false, null, null, defaultColor);
        }

        public static NodeParameterDefinition Seed(string name, int defaultValue, int min = 0, int max = 9999, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Seed, min, max, 1, 0d, defaultValue, false, null, null);
        }

        public static NodeParameterDefinition Text(string name, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Text, 0d, 0d, 0d, 0d, 0, false, null, null);
        }

        /// <summary>Text with a default value.</summary>
        public static NodeParameterDefinition Text(string name, string defaultValue, string? displayName = null)
        {
            return new NodeParameterDefinition(name, displayName ?? name, NodeParameterKind.Text, 0d, 0d, 0d, 0d, 0, false, null, null, defaultText: defaultValue);
        }

        /// <summary>Creates a ViewModel from this definition. Static version for external use.</summary>
        public static NodeParameterViewModel CreateViewModelFromDef(NodeParameterDefinition def) => def.CreateViewModel();

        public NodeParameterViewModel CreateViewModel()
        {
            var viewModel = new NodeParameterViewModel(Name, DisplayName, Kind, Min, Max, Step, Choices, DisplayChoices)
            {
                DefaultNumberValue = DefaultNumber,
                DefaultIntValue = DefaultInt,
                DefaultBoolValue = DefaultBool,
                DefaultChoiceValue = DefaultChoice ?? (Choices.Count > 0 ? Choices[0] : null),
                DefaultColorValue = DefaultColor,
                DefaultTextValue = DefaultText
            };

            switch (Kind)
            {
                case NodeParameterKind.Seed:
                case NodeParameterKind.Integer:
                    viewModel.IntValue = DefaultInt;
                    break;
                case NodeParameterKind.Boolean:
                    viewModel.BoolValue = DefaultBool;
                    break;
                case NodeParameterKind.Choice:
                    viewModel.SelectedChoice = viewModel.DefaultChoiceValue;
                    break;
                case NodeParameterKind.PointList:
                    // default empty list
                    break;
                case NodeParameterKind.Text:
                    viewModel.TextValue = DefaultText;
                    break;
                case NodeParameterKind.Color:
                    viewModel.ColorValue = DefaultColor;
                    break;
                default:
                    viewModel.NumberValue = DefaultNumber;
                    break;
            }

            return viewModel;
        }
    }

    public sealed class NodeParameterViewModel : INotifyPropertyChanged
    {
        private double _numberValue;
        private int _intValue;
        private bool _boolValue;
        private string? _selectedChoice;
        private ObservableCollection<Point> _pointListValue = new();
        private System.Windows.Media.Color _colorValue = Colors.White;

        // Default values for reset
        public double DefaultNumberValue { get; set; }
        public int DefaultIntValue { get; set; }
        public bool DefaultBoolValue { get; set; }
        public string? DefaultChoiceValue { get; set; }
        public System.Windows.Media.Color DefaultColorValue { get; set; }
        public string? DefaultTextValue { get; set; }

        public NodeParameterViewModel(string name, NodeParameterKind kind, double min, double max, double step, IReadOnlyList<string> choices)
        {
            Name = name;
            DisplayName = name;
            Kind = kind;
            Min = min;
            Max = max;
            Step = step;
            Choices = new ObservableCollection<string>(choices ?? Array.Empty<string>());
            DisplayChoices = new ObservableCollection<string>(choices ?? Array.Empty<string>());
            _choiceToDisplay = BuildChoiceMap(choices, choices);
            _displayToChoice = BuildReverseMap(_choiceToDisplay);
        }

        public NodeParameterViewModel(string name, string displayName, NodeParameterKind kind, double min, double max, double step, IReadOnlyList<string> choices)
            : this(name, displayName, kind, min, max, step, choices, choices) { }

        public NodeParameterViewModel(string name, string displayName, NodeParameterKind kind, double min, double max, double step, IReadOnlyList<string> choices, IReadOnlyList<string> displayChoices)
        {
            Name = name;
            DisplayName = displayName;
            Kind = kind;
            Min = min;
            Max = max;
            Step = step;
            Choices = new ObservableCollection<string>(choices ?? Array.Empty<string>());
            // If displayChoices equals choices (no localization was provided), apply the fallback translation table.
            var effectiveDisplay = ReferenceEquals(displayChoices, choices) || displayChoices == null || IsIdenticalList(choices, displayChoices)
                ? ApplyFallbackTranslations(choices)
                : displayChoices;
            DisplayChoices = new ObservableCollection<string>(effectiveDisplay);
            _choiceToDisplay = BuildChoiceMap(choices, effectiveDisplay);
            _displayToChoice = BuildReverseMap(_choiceToDisplay);
        }

        private static IReadOnlyList<string> ApplyFallbackTranslations(IReadOnlyList<string>? choices)
        {
            if (choices == null) return Array.Empty<string>();
            var dict = ChoiceValueTranslations.ForCulture(
                System.Globalization.CultureInfo.CurrentUICulture.Name);
            var result = new List<string>(choices.Count);
            foreach (var v in choices)
                result.Add(dict.TryGetValue(v, out var t) ? t : v);
            return result;
        }

        private static bool IsIdenticalList(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Canonical key for dictionary lookups.</summary>
        public string Name { get; }
        /// <summary>Localized display name for UI.</summary>
        public string DisplayName { get; set; }

        public NodeParameterKind Kind { get; }

        public double Min { get; }

        public double Max { get; }

        public double Step { get; }

        public ObservableCollection<string> Choices { get; }

        /// <summary>Localized display strings for Choices (same order, maps to Choices values).</summary>
        public ObservableCollection<string> DisplayChoices { get; }

        private Dictionary<string, string> _choiceToDisplay;
        private Dictionary<string, string> _displayToChoice;

        /// <summary>Rebuilds the choice-to-display and display-to-choice mappings after updating DisplayChoices.</summary>
        public void RebuildChoiceMappings()
        {
            _choiceToDisplay = BuildChoiceMap(Choices, DisplayChoices);
            _displayToChoice = BuildReverseMap(_choiceToDisplay);
            // Force multi-property notification so ComboBox rebinds SelectedItem
            OnPropertyChanged(nameof(SelectedChoice));
            OnPropertyChanged(nameof(SelectedDisplayChoice));
        }

        /// <summary>
        /// Forces the Choice ComboBox to fully rebind by cycling SelectedChoice through a
        /// temporary empty value, then back to the original. WPF's ComboBox SelectedItem binding
        /// caches the old display string even when the DataTemplate is rebuilt; this workaround
        /// guarantees the ComboBox picks up the new language label.
        /// </summary>
        public void ForceRefreshChoiceDisplay()
        {
            var old = _selectedChoice;
            // Force set to empty (always different)
            _selectedChoice = "";
            OnPropertyChanged(nameof(SelectedChoice));
            OnPropertyChanged(nameof(SelectedDisplayChoice));
            // Restore
            _selectedChoice = old;
            OnPropertyChanged(nameof(SelectedChoice));
            OnPropertyChanged(nameof(SelectedDisplayChoice));
        }

        private static Dictionary<string, string> BuildChoiceMap(IReadOnlyList<string>? values, IReadOnlyList<string>? labels)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (values == null) return dict;
            for (int i = 0; i < values.Count; i++)
            {
                var v = values[i];
                var l = (labels != null && i < labels.Count) ? labels[i] : v;
                dict[v] = l;
            }
            return dict;
        }

        private static Dictionary<string, string> BuildReverseMap(Dictionary<string, string> forward)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in forward)
                dict[kv.Value] = kv.Key;
            return dict;
        }

        /// <summary>Resets the parameter value to its definition default.</summary>
        public void ResetToDefault()
        {
            switch (Kind)
            {
                case NodeParameterKind.Number:
                    NumberValue = DefaultNumberValue;
                    break;
                case NodeParameterKind.Seed:
                case NodeParameterKind.Integer:
                    IntValue = DefaultIntValue;
                    break;
                case NodeParameterKind.Boolean:
                    BoolValue = DefaultBoolValue;
                    break;
                case NodeParameterKind.Choice:
                    SelectedChoice = DefaultChoiceValue;
                    break;
                case NodeParameterKind.Text:
                    TextValue = DefaultTextValue;
                    break;
                case NodeParameterKind.Color:
                    ColorValue = DefaultColorValue;
                    break;
                case NodeParameterKind.PointList:
                    PointListValue.Clear();
                    break;
            }
        }

        /// <summary>The localized display string for the currently selected choice.</summary>
        public string? SelectedDisplayChoice
        {
            get => _selectedChoice != null && _choiceToDisplay.TryGetValue(_selectedChoice, out var d) ? d : _selectedChoice;
            set
            {
                var raw = value != null && _displayToChoice.TryGetValue(value, out var r) ? r : value;
                if (SetField(ref _selectedChoice, raw, nameof(SelectedChoice)))
                    OnPropertyChanged(nameof(SelectedDisplayChoice));
            }
        }

        public double NumberValue
        {
            get => _numberValue;
            set
            {
                // enforce 0.001 precision for number parameters
                var rounded = Math.Round(value, 3);
                SetField(ref _numberValue, rounded);
            }
        }

        public int IntValue
        {
            get => _intValue;
            set => SetField(ref _intValue, value);
        }

        public bool BoolValue
        {
            get => _boolValue;
            set => SetField(ref _boolValue, value);
        }

        public string? SelectedChoice
        {
            get => _selectedChoice;
            set => SetField(ref _selectedChoice, value);
        }

        private string? _textValue;
        public string? TextValue
        {
            get => _textValue;
            set => SetField(ref _textValue, value);
        }

        public ObservableCollection<Point> PointListValue
        {
            get => _pointListValue;
            set => SetField(ref _pointListValue, value);
        }

        public System.Windows.Media.Color ColorValue
        {
            get => _colorValue;
            set => SetField(ref _colorValue, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
