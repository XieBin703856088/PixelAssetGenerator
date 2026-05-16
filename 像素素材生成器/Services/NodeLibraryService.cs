using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Manages the node library: building display lists, filtering, template files,
/// and static helper methods for node parameter definitions and preview brushes.
/// </summary>
public sealed class NodeLibraryService
{
    /// <summary>
    /// Returns the localized display name for a category key.
    /// Looks up "Cat_" + categoryKey in resx. Falls back to the raw categoryKey if not found.
    /// </summary>
    private static Services.Localization.ILocalizationService Loc
        => Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>();

    public static string GetCategoryDisplayName(string categoryKey)
    {
        // First try _categories.json for the current culture
        var jsonName = TryGetCategoryNameFromJson(categoryKey);
        if (jsonName != null) return jsonName;

        // Fall back to resx
        var loc = Loc;
        var resxKey = "Cat_" + categoryKey;
        var name = loc.GetString(resxKey);
        return name != resxKey ? name : categoryKey;
    }

    /// <summary>
    /// Reads the localized category name from _categories.json for the current UI culture.
    /// Returns null if not found.
    /// </summary>
    private static string? TryGetCategoryNameFromJson(string categoryKey)
    {
        try
        {
            var catsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes", "_categories.json");
            if (!System.IO.File.Exists(catsPath)) return null;

            var json = System.IO.File.ReadAllText(catsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(categoryKey, out var catEntry))
                return null;

            // Try exact culture match first (e.g. "zh-CN")
            var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (catEntry.TryGetProperty(culture, out var localized))
                return localized.GetString();

            // Try two-letter culture (e.g. "zh" from "zh-CN")
            var twoLetter = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            foreach (var prop in catEntry.EnumerateObject())
            {
                var key = prop.Name;
                // Match "zh-Hans" when current is "zh-CN"
                if (key.StartsWith(twoLetter, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }

            // Fall back to English
            if (catEntry.TryGetProperty("en", out var eng))
                return eng.GetString();
            // Fall back to first available
            foreach (var prop in catEntry.EnumerateObject())
                return prop.Value.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Maps node TypeName to subcategory label for the node library display.
    /// </summary>
    public static readonly Dictionary<string, string> SubcategoryMap = new()
    {
        // Generator → 基础
        ["SolidColor"] = "Basic",
        ["Noise"] = "Basic",
        ["Gradient"] = "Basic",
        ["Checkerboard"] = "Basic",

        // Generator → Nature
        ["Wood"] = "Nature",
        ["Cloud"] = "Nature",
        ["Marble"] = "Nature",
        ["Terrain"] = "Nature",
        ["Crystal"] = "Nature",
        ["Scales"] = "Nature",
        ["Rust"] = "Nature",
        ["Magma"] = "Nature",
        ["Rock"] = "Nature",
        ["Bush"] = "Nature",
        ["Mushroom"] = "Nature",
        ["Shield"] = "Nature",
        ["Gem"] = "Pattern",
        ["HealthBar"] = "Pattern",
        ["Fire"] = "Nature",
        ["Lightning"] = "Nature",
        ["Smoke"] = "Nature",
        ["Rain"] = "Nature",
        ["Snow"] = "Nature",
        ["Fog"] = "Nature",
        ["Slime"] = "Nature",
        ["Plasma"] = "Pattern",
        ["Rune"] = "Nature",
        ["EnergyField"] = "Nature",
        ["Hologram"] = "Nature",

        ["Torch"] = "Nature",
        ["Tree"] = "Nature",
        ["FrameSequence"] = "Basic",
        ["ParameterAnimation"] = "Basic",
        ["UIPanel"] = "Pattern",
        ["UIButton"] = "Pattern",
        ["Icon"] = "Pattern",

        // Generator → Pattern
        ["Fibers"] = "Pattern",
        ["Weave"] = "Pattern",
        ["Brick"] = "Pattern",
        ["Alveolus"] = "Pattern",
        ["Shape"] = "Pattern",
        ["SplatterCircular"] = "Pattern",
        ["Lattice"] = "Pattern",
        ["Concentric"] = "Pattern",
        ["Spiral"] = "Pattern",
        ["Honeycomb"] = "Pattern",
        ["Wave"] = "Pattern",
        ["Circuit"] = "Pattern",

        // Filter → Color
        ["ColorQuantize"] = "Color",
        ["PixelPerfectOutline"] = "Color",
        ["PaletteMap"] = "Color",

        // Intelligent → Analysis
        ["ImageAnalysis"] = "Analysis",

        // Intelligent → Adjustment
        ["SemanticControl"] = "Adjustment",
        ["Condition"] = "Adjustment",
        ["Selector"] = "Adjustment",
        ["Variation"] = "Adjustment",

        // Building → Architecture
        ["Wall"] = "Architecture",
        ["Floor"] = "Architecture",

        // Tool -> Basic
        ["Text"] = "Basic",
        ["ColorExtraction"] = "Basic",
    };

    // ─── Parameter definitions ────────────────────────────────────────

    public static IReadOnlyList<NodeParameterDefinition> CreateComputeParameters()
    {
        return new[]
        {
            NodeParameterDefinition.Seed("seed", 1200, 0, 9999)
        };
    }

    public static IReadOnlyList<NodeParameterDefinition> CreateCompositeParameters()
    {
        return new[]
        {
            NodeParameterDefinition.Seed("seed", 1200, 0, 9999)
        };
    }

    // ─── Preview helpers ──────────────────────────────────────────────

    /// <summary>
    /// Returns true if the bitmap is mostly black/empty (e.g. a graph node
    /// that received no input and produced a black result).
    /// </summary>
    public static bool IsBitmapMostlyBlack(BitmapSource bmp)
    {
        try
        {
            if (bmp == null) return true;
            var w = Math.Max(1, bmp.PixelWidth);
            var h = Math.Max(1, bmp.PixelHeight);
            var stride = w * 4;
            var pixels = new byte[w * h * 4];
            bmp.CopyPixels(pixels, stride, 0);

            var nonBlack = 0;
            var total = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var a = pixels[i + 3];
                total++;

                if (a > 128 && (r > 30 || g > 30 || b > 30))
                    nonBlack++;
            }

            return total == 0 || (double)nonBlack / total < 0.05;
        }
        catch
        {
            return true;
        }
    }

    public static Brush? CreateSpecialPreviewBrush(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;
        var lower = typeName.ToLowerInvariant();

        // 渐变: diagonal gradient ramp
        if (lower.Contains("gradient"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(60, 40, 120), Color.FromRgb(220, 180, 60), 45), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Freeze();
            var b = new DrawingBrush(g) { Stretch = Stretch.Uniform }; b.Freeze();
            return b;
        }

        // 纯色: solid filled rect
        if (lower.Contains("solidcolor"))
        {
            var b = new SolidColorBrush(Color.FromRgb(80, 140, 200)); b.Freeze();
            return new DrawingBrush(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(0, 0, 32, 32)))) { Stretch = Stretch.Uniform };
        }

        // 噪波: grayscale noise dots
        if (lower.Contains("noise"))
        {
            var dg = new DrawingGroup();
            var r = new Random(42);
            for (int i = 0; i < 40; i++)
            {
                var x = r.NextDouble() * 32; var y = r.NextDouble() * 32;
                var v = (byte)(r.Next(80, 220));
                dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(v, v, v)), null, new EllipseGeometry(new Point(x, y), 1.5, 1.5)));
            }
            dg.Freeze();
            return new DrawingBrush(dg) { Stretch = Stretch.Uniform };
        }

        // 棋盘: checkerboard pattern
        if (lower.Contains("checkerboard"))
        {
            var dg = new DrawingGroup();
            var s = 8.0;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    var c = (x + y) % 2 == 0 ? Color.FromRgb(200, 180, 140) : Color.FromRgb(80, 60, 40);
                    dg.Children.Add(new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(new Rect(x * s, y * s, s, s))));
                }
            dg.Freeze();
            return new DrawingBrush(dg) { Stretch = Stretch.Uniform };
        }

        // 木纹/树/树皮: brown wavy lines
        if (lower.Contains("wood") || lower.Contains("bark") || lower.Contains("tree"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 40, 20)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            for (int i = 0; i < 5; i++)
            {
                var y = 4 + i * 6;
                var pen = new Pen(new SolidColorBrush(Color.FromRgb((byte)(120 + i * 10), (byte)(80 + i * 8), (byte)40)), 1.2);
                pen.Freeze();
                g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse($"M0,{y} Q8,{y - 2} 16,{y} T32,{y}")));
            }
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 砖块: brick wall
        if (lower.Contains("brick"))
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(140, 70, 50)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var mp = new Pen(new SolidColorBrush(Color.FromRgb(60, 30, 20)), 1); mp.Freeze();
            dg.Children.Add(new GeometryDrawing(null, mp, Geometry.Parse("M0,8 L32,8 M0,16 L32,16 M0,24 L32,24 M0,32 L32,32")));
            dg.Children.Add(new GeometryDrawing(null, mp, Geometry.Parse("M16,0 L16,8 M0,8 L0,16 M24,16 L24,24 M8,24 L8,32")));
            dg.Freeze();
            return new DrawingBrush(dg) { Stretch = Stretch.Uniform };
        }

        // 云彩/云: fluffy cloud shapes
        if (lower.Contains("cloud"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 50, 80)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var white = Brushes.White; white.Freeze();
            g.Children.Add(new GeometryDrawing(white, null, new EllipseGeometry(new Point(12, 18), 8, 6)));
            g.Children.Add(new GeometryDrawing(white, null, new EllipseGeometry(new Point(20, 16), 10, 7)));
            g.Children.Add(new GeometryDrawing(white, null, new EllipseGeometry(new Point(16, 12), 7, 5)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 大理石
        if (lower.Contains("marble"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 195, 185)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(140, 130, 120)), 0.8); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M-4,6 C6,2 12,14 24,8 C30,4 36,10 36,6")));
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M-2,20 C8,16 18,26 28,18 C34,14 38,22 38,18")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 地形/岩石/卵石/石板/草地/苔藓/冰/星空/水流: nature terrain
        if (lower.Contains("terrain") || lower.Contains("rock") || lower.Contains("cobblestone") || lower.Contains("flagstone"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 70, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 100, 60)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M0,16 L8,12 L16,18 L24,10 L32,14")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 火/火焰
        if (lower.Contains("fire"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 10, 5)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 100, 0)), null, Geometry.Parse("M16,4 Q22,14 20,22 Q18,28 16,30 Q14,28 12,22 Q10,14 16,4")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 200, 0)), null, Geometry.Parse("M16,10 Q19,16 18,20 Q17,24 16,26 Q15,24 14,20 Q13,16 16,10")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 闪电
        if (lower.Contains("lightning"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(10, 10, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 255, 200)), null, Geometry.Parse("M18,2 L10,16 L16,16 L14,30 L24,14 L18,14 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 水/水流/雨
        if (lower.Contains("waterflow") || lower.Contains("rain"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 40, 70)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 180, 220)), 1.2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,8 Q12,4 20,8 T32,6")));
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M0,20 Q10,16 18,20 T32,18")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 雪/冰
        if (lower.Contains("snow") || lower.Contains("ice"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 210, 230)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(240, 248, 255)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M16,4 L16,28 M4,16 L28,16 M8,8 L24,24 M24,8 L8,24")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 烟雾/雾 — ground-hugging mist that fades upward
        if (lower.Contains("smoke") || lower.Contains("fog"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 35, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var fogBrush = new LinearGradientBrush(Color.FromRgb(160, 170, 180), Color.FromRgb(60, 65, 70), 90);
            fogBrush.Freeze();
            var fogGeo = new RectangleGeometry(new Rect(2, 16, 28, 14));
            g.Children.Add(new GeometryDrawing(fogBrush, null, fogGeo));
            var wispPen = new Pen(new SolidColorBrush(Color.FromRgb(140, 150, 160)), 1.5); wispPen.Freeze();
            g.Children.Add(new GeometryDrawing(null, wispPen, new EllipseGeometry(new Point(10, 22), 8, 3)));
            g.Children.Add(new GeometryDrawing(null, wispPen, new EllipseGeometry(new Point(22, 26), 6, 2)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // Color adjust / color related
        if (lower.Contains("coloradjust") || lower.Contains("color") || lower.Contains("colorize") || lower.Contains("gradientmap"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(40, 48, 60), Color.FromRgb(90, 140, 200), 0), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var wheel = new EllipseGeometry(new Point(24, 8), 6, 6);
            g.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 1), wheel));
            g.Children.Add(new GeometryDrawing(new RadialGradientBrush(Color.FromRgb(255, 120, 120), Color.FromRgb(120, 200, 120)), null, new EllipseGeometry(new Point(24, 8), 4, 4)));
            g.Freeze();
            var b = new DrawingBrush(g) { Stretch = Stretch.Uniform }; b.Freeze();
            return b;
        }

        // Pixel / pixel-related
        if (lower.Contains("pixel"))
        {
            var dg = new DrawingGroup();
            var cols = 4; var rows = 4; var cell = 8.0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var c = Color.FromRgb((byte)(40 + x * 40), (byte)(80 + y * 20), (byte)(120 + ((x + y) * 10)));
                    dg.Children.Add(new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(new Rect(x * cell, y * cell, cell, cell))));
                }
            dg.Freeze();
            var brush = new DrawingBrush(dg) { Stretch = Stretch.Uniform }; brush.Freeze();
            return brush;
        }

        // Warp / distort
        if (lower.Contains("distort") || lower.Contains("warp") || lower.Contains("displace"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(28, 34, 44)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(170, 200, 220)), 1.4);
            pen.Freeze();
            var path = Geometry.Parse("M2,8 C8,2 16,14 22,8 C28,2 30,14 30,24");
            g.Children.Add(new GeometryDrawing(null, pen, path));
            g.Freeze();
            var b = new DrawingBrush(g) { Stretch = Stretch.Uniform }; b.Freeze();
            return b;
        }

        // 模糊/锐化/滤镜: filter-related
        if (lower.Contains("blur") || lower.Contains("sharpen") || lower.Contains("threshold") || lower.Contains("convolution"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 45, 55)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 200, 255)), 1.2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,6 L4,26 M4,16 L28,16 M28,6 L28,26")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 建筑: simple roof/house icon for walls & floors
        if (lower.Contains("wall") || lower.Contains("floor"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 35, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 80, 50)), null, Geometry.Parse("M16,4 L28,14 L4,14 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(160, 120, 80)), null, new RectangleGeometry(new Rect(8, 14, 16, 14))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 盾牌
        if (lower.Contains("shield"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 30, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 160, 120)), null, Geometry.Parse("M14,4 L18,4 L18,16 L22,20 L22,24 L16,26 L10,24 L10,20 L14,16 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 宝石/水晶
        if (lower.Contains("gem") || lower.Contains("crystal"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 15, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 200, 255)), null, Geometry.Parse("M16,2 L28,14 L16,26 L4,14 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 240, 255)), null, Geometry.Parse("M16,8 L22,14 L16,20 L10,14 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 生命条
        if (lower.Contains("healthbar"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 30, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 60, 60)), null, new RectangleGeometry(new Rect(4, 12, 24, 8))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 200, 60)), null, new RectangleGeometry(new Rect(4, 12, 18, 8))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 电路板
        if (lower.Contains("circuit"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(15, 30, 15)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(60, 200, 60)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,4 L28,4 M4,4 L4,28 M28,4 L28,28 M4,28 L28,28")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 图像分析/智能: bar chart
        if (lower.Contains("analysis"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 200, 150)), null, new RectangleGeometry(new Rect(4, 16, 5, 12))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 180, 80)), null, new RectangleGeometry(new Rect(12, 10, 5, 18))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 150, 220)), null, new RectangleGeometry(new Rect(20, 4, 5, 24))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 条件/选择器/变体/语义控制: branching diamond
        if (lower.Contains("condition") || lower.Contains("selector") || lower.Contains("variation") || lower.Contains("semantic"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 200, 200)), null, Geometry.Parse("M16,4 L26,16 L16,28 L6,16 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 文本
        if (lower.Contains("text"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 35, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 220)), 1.2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,8 L28,8 M4,16 L28,16 M4,24 L28,24")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 动画发生器/波形预览/帧相关: play button
        if (lower.Contains("framesequence") || lower.Contains("parameteranimation") || lower.Contains("frameinterpolation"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 220, 100)), null, Geometry.Parse("M10,6 L26,16 L10,26 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // UI面板/按钮/图标
        if (lower.Contains("uipanel") || lower.Contains("uibutton") || lower.Contains("icon"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(35, 40, 50)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 80, 120)), null, new RectangleGeometry(new Rect(4, 4, 24, 24))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 140, 200)), null, new RectangleGeometry(new Rect(8, 8, 16, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 魔法/符文/传送门/能量场/全息图/等离子
        if (lower.Contains("rune") || lower.Contains("energyfield") || lower.Contains("hologram") || lower.Contains("plasma"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(15, 10, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(180, 100, 255)), 1.2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 10, 10)));
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 6, 6)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 药水/黏液
        if (lower.Contains("slime"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 200, 100)), null, Geometry.Parse("M12,4 L20,4 L20,8 L24,12 L24,28 L8,28 L8,12 L12,8 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 灌木/蘑菇
        if (lower.Contains("bush") || lower.Contains("mushroom"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 40, 25)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 120, 40)), null, new EllipseGeometry(new Point(16, 14), 10, 8)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 80, 30)), null, new RectangleGeometry(new Rect(14, 20, 4, 8))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 噪波注入/扫描线/发光/暗角/风格化: generic filter icon
        if (lower.Contains("noiseinjection") || lower.Contains("scanlines") || lower.Contains("glow") || lower.Contains("vignette") || lower.Contains("stylize"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(35, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 150, 255)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 10, 10)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 颜色提取: eyedropper
        if (lower.Contains("colorextraction"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 35, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 100, 100)), null, new EllipseGeometry(new Point(8, 8), 4, 4)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 200, 100)), null, new EllipseGeometry(new Point(24, 8), 4, 4)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 100, 200)), null, new EllipseGeometry(new Point(16, 24), 4, 4)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 纹理类 (纤维/编织/细胞等): grid pattern
        if (lower.Contains("fibers") || lower.Contains("weave") || lower.Contains("alveolus"))
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 45, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 100, 70)), 0.8); pen.Freeze();
            for (int i = 0; i <= 4; i++)
            {
                var p = i * 8;
                dg.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse($"M{p},0 L{p},32 M0,{p} L32,{p}")));
            }
            dg.Freeze();
            return new DrawingBrush(dg) { Stretch = Stretch.Uniform };
        }

        // 几何形状 (形状/圆形斑点/格栅/同心圆/螺旋/蜂窝/波浪)
        if (lower.Contains("shape") || lower.Contains("splattercircular") || lower.Contains("lattice") ||
            lower.Contains("concentric") || lower.Contains("spiral") || lower.Contains("honeycomb") || lower.Contains("wave"))
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 200, 220)), 1.2); pen.Freeze();
            dg.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 10, 10)));
            dg.Freeze();
            return new DrawingBrush(dg) { Stretch = Stretch.Uniform };
        }

        // ── 以下为未匹配到上述条件的节点，按功能设计独特图标 ──────────────────

        // AI图像生成: 大脑/星形 + 画笔
        if (lower.Contains("aiimagegen"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 15, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 60, 200)), null, new EllipseGeometry(new Point(16, 14), 8, 8)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 150, 255)), null, Geometry.Parse("M16,8 L18,12 L22,12 L19,15 L20,19 L16,16 L12,19 L13,15 L10,12 L14,12 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 自动平铺: 四方连续箭头
        if (lower.Contains("autotile"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var arrow = new Pen(new SolidColorBrush(Color.FromRgb(80, 200, 140)), 1.5); arrow.Freeze();
            g.Children.Add(new GeometryDrawing(null, arrow, Geometry.Parse("M16,4 L16,28 M4,16 L28,16")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 200, 140)), null, Geometry.Parse("M16,4 L13,9 L19,9 Z M16,28 L13,23 L19,23 Z M4,16 L9,13 L9,19 Z M28,16 L23,13 L23,19 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 斜面/浮雕: 3D斜角方块
        if (lower.Contains("bevel"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 30, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 180, 200)), null, Geometry.Parse("M8,6 L24,6 L26,8 L26,24 L24,26 L8,26 L6,24 L6,8 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 100, 130)), null, Geometry.Parse("M8,6 L24,6 L18,12 L12,12 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 混合模式: 叠加圆
        if (lower.Contains("blendmode"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 80, 80)), null, new EllipseGeometry(new Point(12, 13), 7, 7)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 120, 200)), null, new EllipseGeometry(new Point(20, 19), 7, 7)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 画笔: 笔尖
        if (lower.Contains("brush"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 180, 140)), null, Geometry.Parse("M14,4 L20,4 L22,28 L12,28 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(240, 220, 180)), null, Geometry.Parse("M15,4 L19,4 L20,10 L14,10 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 缓存: 磁盘/存储
        if (lower.Contains("cache"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 160, 200)), null, new RectangleGeometry(new Rect(6, 8, 20, 16))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 100, 140)), null, new RectangleGeometry(new Rect(8, 10, 8, 6))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 迷彩: 不规则色块
        if (lower.Contains("camo"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 60, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 100, 50)), null, new EllipseGeometry(new Point(10, 10), 6, 5)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 45, 30)), null, new EllipseGeometry(new Point(22, 20), 7, 5)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 50, 30)), null, new EllipseGeometry(new Point(18, 8), 4, 3)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 锁子甲: 环环相扣
        if (lower.Contains("chainmail"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(35, 35, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var ring = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 180)), 1.2); ring.Freeze();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    g.Children.Add(new GeometryDrawing(null, ring, new EllipseGeometry(new Point(4 + x * 8, 4 + y * 8), 3, 3)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 通道: RGB分离
        if (lower.Contains("channel"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 25, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 60, 60)), null, new RectangleGeometry(new Rect(4, 6, 24, 6))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 200, 60)), null, new RectangleGeometry(new Rect(4, 13, 24, 6))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 60, 200)), null, new RectangleGeometry(new Rect(4, 20, 24, 6))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 色差: RGB偏移
        if (lower.Contains("chromaticaberration"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 20, 25)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 50, 50)), null, Geometry.Parse("M6,8 L26,8 L26,12 L6,12 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 200)), null, Geometry.Parse("M6,20 L26,20 L26,24 L6,24 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 100, 100)), null, Geometry.Parse("M6,14 L26,14 L26,18 L6,18 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 注释: 气泡
        if (lower.Contains("comment"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 100)), null, Geometry.Parse("M6,8 L26,8 L26,20 L18,20 L14,26 L15,20 L6,20 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 常量: 等号
        if (lower.Contains("constant"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 200, 220)), null, Geometry.Parse("M6,11 L26,11 M6,21 L26,21")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 曲线调整: 曲线图
        if (lower.Contains("curves"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 200, 100)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,24 Q10,20 16,14 T28,6")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 投影: 阴影方块
        if (lower.Contains("dropshadow"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 20, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 40, 60)), null, new RectangleGeometry(new Rect(10, 12, 16, 16))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 140, 200)), null, new RectangleGeometry(new Rect(6, 6, 16, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 布料: 编织纹理
        if (lower.Contains("fabric"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 40, 50)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var thread = new Pen(new SolidColorBrush(Color.FromRgb(140, 100, 120)), 1); thread.Freeze();
            for (int i = 0; i <= 4; i++) { var p = i * 8; g.Children.Add(new GeometryDrawing(null, thread, Geometry.Parse($"M0,{p} L32,{p}"))); }
            for (int i = 0; i <= 4; i++) { var p = i * 8; g.Children.Add(new GeometryDrawing(null, thread, Geometry.Parse($"M{p},0 L{p},32"))); }
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 栅栏: 竖条纹
        if (lower.Contains("fence"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 30, 20)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 90, 60)), null, new RectangleGeometry(new Rect(2, 4, 6, 24))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(140, 110, 80)), null, new RectangleGeometry(new Rect(12, 4, 6, 24))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 90, 60)), null, new RectangleGeometry(new Rect(22, 4, 6, 24))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(90, 65, 40)), null, new RectangleGeometry(new Rect(0, 8, 32, 3))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(90, 65, 40)), null, new RectangleGeometry(new Rect(0, 20, 32, 3))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 洪水填充: 油漆桶
        if (lower.Contains("floodfill"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 160, 200)), null, Geometry.Parse("M16,4 L26,20 L6,20 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 120, 160)), null, new RectangleGeometry(new Rect(6, 20, 20, 6))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 草地: 草叶
        if (lower.Contains("grass"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 50, 25)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var stem = new Pen(new SolidColorBrush(Color.FromRgb(60, 180, 60)), 1.2); stem.Freeze();
            g.Children.Add(new GeometryDrawing(null, stem, Geometry.Parse("M8,28 Q6,16 10,6")));
            g.Children.Add(new GeometryDrawing(null, stem, Geometry.Parse("M16,28 Q14,14 18,4")));
            g.Children.Add(new GeometryDrawing(null, stem, Geometry.Parse("M24,28 Q22,18 26,8")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 灰度: 灰阶条
        if (lower.Contains("grayscale"))
        {
            var g = new DrawingGroup();
            for (int i = 0; i < 8; i++)
            {
                var v = (byte)(i * 32);
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(v, v, v)), null, new RectangleGeometry(new Rect(i * 4, 4, 4, 24))));
            }
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // HSL调整: 色相环
        if (lower.Contains("hsladjust"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 60, 60)), null, Geometry.Parse("M16,4 L20,12 L28,12 L22,18 L24,26 L16,20 L8,26 L10,18 L4,12 L12,12 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 熔岩流: 发光条纹
        if (lower.Contains("lavaflow"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(30, 10, 5)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 100, 0)), null, Geometry.Parse("M0,8 Q8,4 16,10 T32,8")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 200, 0)), null, Geometry.Parse("M0,8 Q8,4 16,10 T32,8")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 50, 0)), null, Geometry.Parse("M0,20 Q10,24 20,18 T32,22")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 皮革: 粒面纹理
        if (lower.Contains("leather"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 35, 25)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pore = new Pen(new SolidColorBrush(Color.FromRgb(70, 50, 35)), 0.8); pore.Freeze();
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    g.Children.Add(new GeometryDrawing(null, pore, new EllipseGeometry(new Point(3 + x * 5, 3 + y * 5), 1, 1)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 光照: 灯泡
        if (lower.Contains("lighting"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 20, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 200, 80)), null, Geometry.Parse("M14,6 L18,6 L20,16 L12,16 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 140, 50)), null, new RectangleGeometry(new Rect(14, 18, 4, 4))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(255, 230, 150)), null, new EllipseGeometry(new Point(16, 10), 4, 4)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 线条: 斜线 + 端点
        if (lower.Contains("line"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(160, 200, 240)), 2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,28 L28,4")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 遮罩混合: 渐变遮罩
        if (lower.Contains("maskblend"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 80, 80)), null, new EllipseGeometry(new Point(10, 10), 7, 7)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 80, 200)), null, new EllipseGeometry(new Point(22, 22), 7, 7)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 60, 60)), null, new RectangleGeometry(new Rect(0, 16, 32, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 数学运算: +-×÷
        if (lower.Contains("mathop"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(100, 200, 100)), null, Geometry.Parse("M12,8 L20,8 M16,4 L16,12")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 100, 100)), null, Geometry.Parse("M10,20 L22,20 M10,24 L22,24")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 马赛克: 彩色方块
        if (lower.Contains("mosaic"))
        {
            var g = new DrawingGroup();
            var colors = new[] { Color.FromRgb(180, 80, 80), Color.FromRgb(80, 160, 80), Color.FromRgb(80, 80, 180), Color.FromRgb(180, 180, 80) };
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    g.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[y * 2 + x]), null, new RectangleGeometry(new Rect(x * 16, y * 16, 16, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 苔藓: 绿色斑点
        if (lower.Contains("moss"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(35, 45, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 130, 50)), null, new EllipseGeometry(new Point(8, 8), 4, 3)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 150, 60)), null, new EllipseGeometry(new Point(20, 14), 5, 4)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 120, 40)), null, new EllipseGeometry(new Point(26, 24), 3, 3)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 法线贴图: 法线球
        if (lower.Contains("normalmap"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 80, 160)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(160, 160, 200)), null, new EllipseGeometry(new Point(16, 16), 12, 12)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 240)), null, new EllipseGeometry(new Point(12, 10), 4, 3)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 偏移包裹: 卷曲箭头
        if (lower.Contains("offsetwrap"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 180, 220)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4,16 C4,8 12,4 20,8 C28,12 28,20 20,24")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 180, 220)), null, Geometry.Parse("M20,20 L24,24 L20,28 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 描边: 轮廓方块
        if (lower.Contains("outline"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 100)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(60, 60, 80)), null, new RectangleGeometry(new Rect(8, 8, 16, 16))));
            g.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(8, 8, 16, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 输出: 箭头出口
        if (lower.Contains("output"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 200, 120)), null, Geometry.Parse("M4,6 L28,6 L28,26 L4,26 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 60, 50)), null, Geometry.Parse("M10,16 L22,16 M22,16 L18,12 M22,16 L18,20")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 透视变换: 梯形
        if (lower.Contains("perspectivetransform"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(160, 120, 80)), null, Geometry.Parse("M6,24 L26,24 L22,8 L10,8 Z")));
            var grid = new Pen(new SolidColorBrush(Color.FromRgb(60, 50, 40)), 0.8); grid.Freeze();
            g.Children.Add(new GeometryDrawing(null, grid, Geometry.Parse("M8,24 L14,8 M12,24 L18,8 M16,24 L22,8 M20,24 L26,8")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 海报化: 色阶方块
        if (lower.Contains("posterize"))
        {
            var g = new DrawingGroup();
            var colors = new[] { Color.FromRgb(60, 30, 30), Color.FromRgb(120, 60, 60), Color.FromRgb(200, 100, 100) };
            for (int i = 0; i < 3; i++)
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[i]), null, new RectangleGeometry(new Rect(i * 11, 4, 11, 24))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 预览: 眼睛
        if (lower.Contains("preview"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 220)), null, new EllipseGeometry(new Point(16, 16), 10, 7)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 50, 70)), null, new EllipseGeometry(new Point(16, 16), 5, 5)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 220)), null, new EllipseGeometry(new Point(16, 16), 2, 2)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 径向模糊: 旋转圆圈
        if (lower.Contains("radialblur"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 160, 200)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 10, 10)));
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 6, 6)));
            var arc = new Pen(new SolidColorBrush(Color.FromRgb(200, 220, 240)), 1.5); arc.Freeze();
            g.Children.Add(new GeometryDrawing(null, arc, Geometry.Parse("M16,4 A12,12 0 0,1 28,16")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 随机选择: 骰子
        if (lower.Contains("randomselect"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 220)), null, Geometry.Parse("M6,6 L26,6 L26,26 L6,26 Z")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 70)), null, new EllipseGeometry(new Point(12, 12), 2, 2)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 70)), null, new EllipseGeometry(new Point(20, 20), 2, 2)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 矩形: 空心矩形
        if (lower.Contains("rectangle"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(160, 200, 240)), 2); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(6, 8, 20, 16))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 涟漪: 同心波纹
        if (lower.Contains("ripple"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 30, 45)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 180, 220)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 12, 12)));
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 8, 8)));
            g.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(16, 16), 4, 4)));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 无缝混合: 边缘融合箭头
        if (lower.Contains("seamlessblend"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 160, 120)), null, new RectangleGeometry(new Rect(4, 4, 24, 24))));
            var fade = new LinearGradientBrush(Color.FromRgb(80, 160, 120), Color.FromRgb(25, 35, 30), 0);
            fade.Freeze();
            g.Children.Add(new GeometryDrawing(fade, null, new RectangleGeometry(new Rect(4, 4, 12, 24))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 精灵表: 网格帧
        if (lower.Contains("spritesheet"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var grid = new Pen(new SolidColorBrush(Color.FromRgb(80, 160, 200)), 1); grid.Freeze();
            g.Children.Add(new GeometryDrawing(null, grid, Geometry.Parse("M0,16 L32,16 M16,0 L16,32")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 80, 200)), null, new EllipseGeometry(new Point(8, 8), 4, 4)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 120, 80)), null, new RectangleGeometry(new Rect(18, 18, 10, 10))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 精灵切片: 裁剪框
        if (lower.Contains("spriteslice"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 80)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(40, 45, 55)), null, new RectangleGeometry(new Rect(4, 4, 24, 24))));
            g.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(4, 4, 12, 14))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 星空: 星星
        if (lower.Contains("starfield"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(5, 5, 20)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var star = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 255)), 1); star.Freeze();
            var r = new Random(42);
            for (int i = 0; i < 30; i++) { var x = r.Next(2, 30); var y = r.Next(2, 30); var s = r.NextDouble() * 1.5 + 0.5; g.Children.Add(new GeometryDrawing(null, star, new EllipseGeometry(new Point(x, y), s, s))); }
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 对称: 镜像箭头
        if (lower.Contains("symmetry"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M16,4 L16,28")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 180, 220)), null, Geometry.Parse("M6,12 L14,12 L14,6")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(220, 180, 120)), null, Geometry.Parse("M26,20 L18,20 L18,26")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 平铺组合: 四宫格
        if (lower.Contains("tilecombine"))
        {
            var g = new DrawingGroup();
            var colors = new[] { Color.FromRgb(80, 160, 80), Color.FromRgb(160, 120, 80), Color.FromRgb(80, 120, 180), Color.FromRgb(180, 180, 80) };
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    g.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[y * 2 + x]), null, new RectangleGeometry(new Rect(x * 16, y * 16, 15, 15))));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 平铺镜像: 镜像L
        if (lower.Contains("tilemirror"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 35, 30)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 160, 120)), 1); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M16,0 L16,32 M0,16 L32,16")));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 200, 120)), null, Geometry.Parse("M4,4 L14,4 L14,14 L4,14 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 变换: 旋转/缩放箭头
        if (lower.Contains("transform"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(25, 30, 40)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 180, 220)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(8, 8, 16, 16))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(120, 180, 220)), null, Geometry.Parse("M8,4 L4,8 L8,12 Z")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 漩涡: 扭曲螺旋
        if (lower.Contains("twirl"))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 25, 35)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(180, 140, 200)), 1.5); pen.Freeze();
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M16,4 C24,4 28,10 28,16 C28,24 22,28 16,28 C8,28 4,22 4,16 C4,12 6,8 10,6")));
            g.Freeze();
            return new DrawingBrush(g) { Stretch = Stretch.Uniform };
        }

        // 平铺类型: 五类使用对应颜色
        if (lower.Contains("tilegrass"))
        {
            return CreateSubcategoryBrush(Color.FromRgb(30, 60, 30), Color.FromRgb(62, 130, 72));
        }
        if (lower.Contains("tilestone"))
        {
            return CreateSubcategoryBrush(Color.FromRgb(40, 45, 55), Color.FromRgb(90, 94, 104));
        }
        if (lower.Contains("tilewater"))
        {
            return CreateSubcategoryBrush(Color.FromRgb(20, 50, 90), Color.FromRgb(40, 92, 160));
        }
        if (lower.Contains("tilesand"))
        {
            return CreateSubcategoryBrush(Color.FromRgb(80, 70, 45), Color.FromRgb(166, 138, 88));
        }
        if (lower.Contains("tileroa"))
        {
            return CreateSubcategoryBrush(Color.FromRgb(45, 40, 35), Color.FromRgb(90, 78, 64));
        }

        // Default subcategory-based fallback
        if (SubcategoryMap.TryGetValue(typeName, out var subcat))
        {
            return subcat switch
            {
                "Basic" => CreateSubcategoryBrush(Color.FromRgb(60, 70, 90), Color.FromRgb(120, 150, 200)),
                "Nature" => CreateSubcategoryBrush(Color.FromRgb(30, 60, 30), Color.FromRgb(80, 160, 80)),
                "Pattern" => CreateSubcategoryBrush(Color.FromRgb(50, 40, 60), Color.FromRgb(150, 120, 200)),
                "Color" => CreateSubcategoryBrush(Color.FromRgb(60, 40, 40), Color.FromRgb(220, 100, 100)),
                "Analysis" => CreateSubcategoryBrush(Color.FromRgb(30, 40, 50), Color.FromRgb(100, 180, 220)),
                "Adjustment" => CreateSubcategoryBrush(Color.FromRgb(40, 50, 40), Color.FromRgb(100, 200, 150)),
                "Architecture" => CreateSubcategoryBrush(Color.FromRgb(45, 35, 25), Color.FromRgb(160, 120, 80)),
                _ => null
            };
        }

        return null;
    }

    private static Brush CreateSubcategoryBrush(Color dark, Color accent)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(accent), null, new RectangleGeometry(new Rect(4, 4, 24, 24))));
        g.Freeze();
        return new DrawingBrush(g) { Stretch = Stretch.Uniform };
    }

    /// <summary>
    /// Maps node TypeName to functional description shown in the property panel and status bar.
    /// </summary>
    public static readonly Dictionary<string, string> NodeDescriptionsEn = new()
    {
        ["SolidColor"] = "Generates solid color fill image with configurable color and opacity.",
        ["Noise"] = "Generate procedural noise (Perlin/Value/Simplex) with fractal overlay",
        ["Gradient"] = "Generate linear or radial gradient with stops and angle control",
        ["Checkerboard"] = "Generate checkerboard pattern with two configurable colors and size",
        ["Wood"] = "Generate realistic wood texture with rings and knot details",
        ["Cloud"] = "Generate procedural cloud patterns (cumulus/stratus/cirrus)",
        ["Marble"] = "Generate marble texture with flowing vein patterns",
        ["Terrain"] = "Generate heightmap terrain with multi-layer noise overlay",
        ["WaterFlow"] = "Generate flowing water texture with speed and ripple control",
        ["Crystal"] = "Generate crystal/ice texture with sharp geometric facets",
        ["Scales"] = "Generate fish/dragon scale repeating texture",
        ["Rust"] = "Generate rust corrosion texture for metal aging effects",
        ["Magma"] = "Generate glowing magma texture with fiery veins",
        ["Rock"] = "Generate rocky stone texture with cracks and grain",
        ["Bush"] = "Generate pixel art bush sprite",
        ["Mushroom"] = "Generate pixel art mushroom sprite for fantasy/dungeon scenes",
        ["Shield"] = "Generate pixel art shield sprite with multiple shield shapes",
        ["Gem"] = "Generate cut gem texture with shiny reflective highlights",
        ["HealthBar"] = "Generate pixel art health bar with configurable color and segments",
        ["Fire"] = "Generate procedural fire animation with flickering core and smoke",
        ["Lightning"] = "Generate branching lightning effect with glow",
        ["Smoke"] = "Generate smoke/steam effect with soft translucent edges",
        ["Rain"] = "Generate falling rain effect with adjustable density and speed",
        ["Snow"] = "Generate falling snow effect with ground coverage",
        ["Fog"] = "Generate fog/mist effect based on height blend",
        ["Slime"] = "Generate slime effect with glossy translucent drips",
        ["Plasma"] = "Generate colorful sci-fi plasma effect",
        ["Rune"] = "Generate glowing magic rune inscription",
        ["EnergyField"] = "Generate hexagonal/grid energy shield effect",
        ["Hologram"] = "Generate hologram projection effect with scanlines and chromatic aberration",

        ["Torch"] = "Generate wall-mounted torch with fire glow animation",
        ["Tree"] = "Generate pixel art tree (round/conical/layered/palm/cactus)",
        ["FrameSequence"] = "Generate multi-frame animation sequence with sine wave per-pixel motion",
        ["ParameterAnimation"] = "Preview LFO waveforms (sine/saw/triangle/square) for animation curves",
        ["UIPanel"] = "Generate pixel art UI panel with border/corners/title bar",
        ["UIButton"] = "Generate pixel art button (normal/hover/pressed states)",
        ["Icon"] = "Generate game icons like heart/star/gear/map marker",
        ["ColorAdjust"] = "Adjust image brightness, contrast, saturation and hue",
        ["ColorQuantize"] = "Reduce image to N colors (2-256) with ordered dithering",
        ["Grayscale"] = "Convert color image to grayscale with weighted channels.",
        ["Blur"] = "Gaussian blur with adjustable radius.",
        ["Sharpen"] = "Sharpen image details, enhance edge contrast.",
        ["Glow"] = "Add glow/bloom effect with adjustable intensity and radius",
        ["Scanlines"] = "Overlay CRT scanline effect for retro display look",
        ["NoiseInjection"] = "Add procedural noise interference to image",
        ["Vignette"] = "Add dark corner vignette effect to image",
        ["ChromaticAberration"] = "Simulate lens chromatic aberration/purple fringe.",
        ["Stylize"] = "Apply artistic stylization filter to image",
        ["Pixelate"] = "Pixelate image with multiple sampling modes and dithering",
        ["Distort"] = "Distort image with ripple and swirl effects",
        ["Convolution"] = "Apply custom convolution kernel for image processing",
        ["Threshold"] = "Binarize image based on threshold value with animated threshold",
        ["Outline"] = "Detect and draw image edge outlines",
        ["PixelPerfectOutline"] = "Generate pixel-perfect outline on alpha or color edges",
        ["PaletteMap"] = "Map input image to a specified palette using nearest color matching",
        ["DropShadow"] = "Add drop shadow effect with adjustable direction and offset",
        ["NormalMap"] = "Generate normal map from height map.",
        ["Bevel"] = "Add 3D bevel/emboss effect to image",
        ["Displace"] = "Distort image pixels based on displacement map",
        ["MathOp"] = "Perform math operations between images or channels (add/sub/mul/blend)",
        ["Channel"] = "Split or merge image color channels",
        ["Transform"] = "Rotate, scale, and flip image",
        ["BlendMode"] = "Composite two images with 19 blend modes and opacity control",
        ["MaskBlend"] = "Control blend region of two images using a mask",
        ["Colorize"] = "Colorize grayscale image or overlay a color tint",
        ["Lighting"] = "Add animated lighting/shadow effect to image",
        ["GradientMap"] = "Map grayscale to gradient color band for toning effects",
        ["FrameInterpolation"] = "Interpolate intermediate frames between two keyframes",
        ["OffsetWrap"] = "Offset image with edge wrapping for seamless texture creation",
        ["SeamlessBlend"] = "Blend image edges to achieve seamless tiling",
        ["TileMirror"] = "Mirror-tile image to eliminate seams",
        ["TileCombine"] = "Combine multiple images into a tiling texture",
        ["Output"] = "Output node graph result as final image file",
        ["Preview"] = "Display node output in the preview window",
        ["Text"] = "Overlay text on bitmap surface with font and style selection",
        ["ColorExtraction"] = "Analyze input image to extract dominant colors and output palette",
        ["Constant"] = "Output constant color value or numeric value for parameter control",
        ["Comment"] = "Add text comment on canvas without affecting render",
        ["Symmetry"] = "Mirror image horizontally, vertically, or radially",
        ["RandomSelect"] = "Randomly select output from multiple inputs with weighted probability",
        ["SpriteSheet"] = "Arrange multiple frames into a sprite sheet",
        ["SpriteSlice"] = "Slice a single frame from a sprite sheet",
        ["NineSlice"] = "9-slice scaling that preserves border proportions",
        ["Cache"] = "Cache subgraph computation results for faster repeated evaluation",
        ["PerspectiveTransform"] = "Apply 3D perspective transform/planar mapping to image.",
        ["ImageAnalysis"] = "Output brightness/contrast/entropy/edge density/dominant color/saturation as float values with passthrough",
        ["Condition"] = "Select different branch output based on condition for logic control",
        ["Selector"] = "Select specified channel from multiple inputs.",
        ["SemanticControl"] = "Control node parameter overrides using semantic tags",
        ["Variation"] = "Generate parameter variants from random seeds to explore outputs",
        ["AutoTile"] = "Auto-generate tile set that adapts to neighboring edges",
        ["Wall"] = "Generate building wall texture (stone/plaster/wood plank/adobe)",
        ["Floor"] = "Generate building floor texture (wood plank/stone brick/carpet)",
        ["Fibers"] = "Generate anisotropic fiber/hair texture with soft edges",
        ["Weave"] = "Generate woven fabric texture",
        ["Brick"] = "Generate pixel-accurate brick wall texture with mortar and rounded corners",
        ["Alveolus"] = "Generate biological cell or honeycomb texture",
        ["Shape"] = "Generate custom geometric shapes (rectangle/circle/polygon etc)",
        ["SplatterCircular"] = "Generate random circular splatter or ink dot texture",
        ["Lattice"] = "Generate lattice or grid texture",
        ["Concentric"] = "Generate concentric ring or bullseye pattern",
        ["Spiral"] = "Generate vortex or spiral pattern",
        ["Honeycomb"] = "Generate hexagonal honeycomb arrangement texture",
        ["Wave"] = "Generate sine wave pattern texture",
        ["Circuit"] = "Generate circuit board or tech-style texture",
        ["Bark"] = "Generate tree bark texture with vertical grain and knots",
        ["Cobblestone"] = "Generate cobblestone/gravel ground texture.",
        ["Flagstone"] = "Generate flagstone paving texture",
        ["Grass"] = "Generate grass/grass-tuft texture.",
        ["Moss"] = "Generate moss-covered surface texture",
        ["Ice"] = "Generate ice or frozen surface texture",
        ["Starfield"] = "Generate starry sky/night sky background.",
        ["LavaFlow"] = "Generate flowing lava animation texture",
        ["Fabric"] = "Generate cloth or textile fabric texture",
        ["Chainmail"] = "Generate chainmail or metal ring texture",
        ["Camo"] = "Generate camouflage pattern texture",
        ["Mosaic"] = "Generate mosaic tessellation texture",
        ["Leather"] = "Generate leather texture with pores and wrinkles",
        ["Fence"] = "Generate fence texture with wood grain and nails",
    };

    /// <summary>
    /// Chinese (zh-Hans) descriptions for node types.
    /// </summary>
    public static readonly Dictionary<string, string> NodeDescriptionsZhHans = new()
    {
        ["SolidColor"] = "生成纯色填充图像，可配置颜色和不透明度。",
        ["Noise"] = "生成程序化噪声（Perlin/Value/Simplex），支持分形叠加",
        ["Gradient"] = "生成线性或径向渐变，支持渐变节点和角度控制",
        ["Checkerboard"] = "生成棋盘格图案，可配置两种颜色和格子大小",
        ["Wood"] = "生成逼真的木材纹理，带有年轮和木节细节",
        ["Cloud"] = "生成程序化云朵图案（积云/层云/卷云）",
        ["Marble"] = "生成大理石纹理，带有流动的脉纹图案",
        ["Terrain"] = "生成地形高度图，多层噪声叠加",
        ["WaterFlow"] = "生成流动的水体纹理，支持速度和波纹控制",
        ["Crystal"] = "生成水晶/冰晶纹理，带有锋利的几何切面",
        ["Scales"] = "生成鱼鳞/龙鳞重复纹理",
        ["Rust"] = "生成铁锈腐蚀纹理，用于金属老化效果",
        ["Magma"] = "生成发光的岩浆纹理，带有火红的脉纹",
        ["Rock"] = "生成岩石纹理，带有裂缝和颗粒感",
        ["Bush"] = "生成像素风灌木精灵",
        ["Mushroom"] = "生成像素风蘑菇，适用于幻想/地牢场景",
        ["Shield"] = "生成像素风盾牌精灵，多种盾牌形状可选",
        ["Gem"] = "生成切割宝石纹理，带有闪亮的高光反射",
        ["HealthBar"] = "生成像素风血条，可配置颜色和分段",
        ["Fire"] = "生成程序化火焰动画，带有闪烁核心和烟雾",
        ["Lightning"] = "生成分支闪电效果，带有发光边缘",
        ["Smoke"] = "生成烟雾/蒸汽效果，半透明柔边",
        ["Rain"] = "生成下雨效果，可调节密度和速度",
        ["Snow"] = "生成下雪效果，带有地面覆盖",
        ["Fog"] = "生成雾/薄雾效果，基于高度混合",
        ["Slime"] = "生成史莱姆效果，带有光泽半透明滴落",
        ["Plasma"] = "生成多彩科幻等离子效果",
        ["Rune"] = "生成发光的魔法符文铭文",
        ["EnergyField"] = "生成六边形/网格能量护盾效果",
        ["Hologram"] = "生成全息投影效果，带有扫描线和色差",
        ["Torch"] = "生成壁挂火把，带有火焰发光动画",
        ["Tree"] = "生成像素风树木（圆形/锥形/分层/棕榈/仙人掌）",
        ["FrameSequence"] = "生成多帧动画序列，基于正弦波逐像素运动",
        ["ParameterAnimation"] = "预览LFO波形（正弦/锯齿/三角/方波），用于动画曲线",
        ["UIPanel"] = "生成像素风UI面板，带边框/圆角/标题栏",
        ["UIButton"] = "生成像素风按钮（正常/悬停/按下状态）",
        ["Icon"] = "生成游戏图标，如心形/星形/齿轮/地图标记",
        ["ColorAdjust"] = "调整图像亮度、对比度、饱和度和色相",
        ["ColorQuantize"] = "将图像减少到N种颜色（2-256），支持有序抖动",
        ["Grayscale"] = "将彩色图像转换为灰度，支持加权通道。",
        ["Blur"] = "高斯模糊，半径可调节。",
        ["Sharpen"] = "锐化图像细节，增强边缘对比度。",
        ["Glow"] = "添加发光/辉光效果，可调节强度和半径",
        ["Scanlines"] = "叠加CRT扫描线效果，营造复古显示风格",
        ["NoiseInjection"] = "向图像添加程序化噪声干扰",
        ["Vignette"] = "添加暗角晕影效果",
        ["ChromaticAberration"] = "模拟镜头色差/紫边效果。",
        ["Stylize"] = "对图像应用艺术风格化滤镜",
        ["Pixelate"] = "像素化图像，多种采样模式和抖动选项",
        ["Distort"] = "扭曲图像，支持波纹和漩涡效果",
        ["Convolution"] = "应用自定义卷积核进行图像处理",
        ["Threshold"] = "基于阈值二值化图像，支持动画阈值",
        ["Outline"] = "检测并绘制图像边缘轮廓",
        ["PixelPerfectOutline"] = "在Alpha或颜色边缘上生成像素完美轮廓",
        ["PaletteMap"] = "使用最近颜色匹配将输入图像映射到指定调色板",
        ["DropShadow"] = "添加投影效果，可调节方向和偏移",
        ["NormalMap"] = "从高度图生成法线贴图。",
        ["Bevel"] = "对图像添加3D斜面/浮雕效果",
        ["Displace"] = "基于位移贴图扭曲图像像素",
        ["MathOp"] = "对图像或通道执行数学运算（加/减/乘/混合）",
        ["Channel"] = "分离或合并图像颜色通道",
        ["Transform"] = "旋转、缩放和翻转图像",
        ["BlendMode"] = "合成两张图像，支持19种混合模式和不透明度控制",
        ["MaskBlend"] = "使用遮罩控制两张图像的混合区域",
        ["Colorize"] = "为灰度图像上色或叠加颜色色调",
        ["Lighting"] = "为图像添加动画光照/阴影效果",
        ["GradientMap"] = "将灰度映射到渐变色彩带，用于色调效果",
        ["FrameInterpolation"] = "在两个关键帧之间插值生成中间帧",
        ["OffsetWrap"] = "偏移图像并边缘环绕，用于无缝纹理创建",
        ["SeamlessBlend"] = "混合图像边缘以实现无缝平铺",
        ["TileMirror"] = "镜像平铺图像以消除接缝",
        ["TileCombine"] = "将多张图像合成为平铺纹理",
        ["Output"] = "将节点图结果输出为最终图像文件",
        ["Preview"] = "在预览窗口中显示节点输出",
        ["Text"] = "在图像表面叠加文本，支持字体和样式选择",
        ["ColorExtraction"] = "分析输入图像提取主色调并输出调色板",
        ["Constant"] = "输出常量颜色值或数值，用于参数控制",
        ["Comment"] = "在画布上添加文本注释，不影响渲染",
        ["Symmetry"] = "镜像图像，横向、纵向或径向",
        ["RandomSelect"] = "从多个输入中按加权概率随机选择输出",
        ["SpriteSheet"] = "将多个帧排列为精灵表",
        ["SpriteSlice"] = "从精灵表中切分单个帧",
        ["NineSlice"] = "九宫格缩放，保持边框比例不变",
        ["Cache"] = "缓存子图计算结果，加速重复评估",
        ["PerspectiveTransform"] = "对图像应用3D透视变换/平面映射。",
        ["ImageAnalysis"] = "输出亮度/对比度/熵/边缘密度/主色/饱和度为浮点值，带直通",
        ["Condition"] = "根据条件选择不同分支输出，用于逻辑控制",
        ["Selector"] = "从多个输入中选择指定通道。",
        ["SemanticControl"] = "使用语义标签控制节点参数覆盖",
        ["Variation"] = "从随机种子生成参数变体，探索不同输出",
        ["AutoTile"] = "自动生成适应相邻边缘的瓦片集",
        ["Wall"] = "生成建筑墙壁纹理（石头/灰泥/木板/土坯）",
        ["Floor"] = "生成建筑地板纹理（木板/石砖/地毯）",
        ["Fibers"] = "生成各向异性纤维/毛发纹理，柔边效果",
        ["Weave"] = "生成编织织物纹理",
        ["Brick"] = "生成像素精确的砖墙纹理，带灰浆和圆角",
        ["Alveolus"] = "生成生物细胞或蜂窝纹理",
        ["Shape"] = "生成自定义几何形状（矩形/圆形/多边形等）",
        ["SplatterCircular"] = "生成随机圆形溅射或墨点纹理",
        ["Lattice"] = "生成格子或网格纹理",
        ["Concentric"] = "生成同心环或靶心图案",
        ["Spiral"] = "生成漩涡或螺旋图案",
        ["Honeycomb"] = "生成六边形蜂巢排列纹理",
        ["Wave"] = "生成正弦波图案纹理",
        ["Circuit"] = "生成电路板或科技风格纹理",
        ["Bark"] = "生成树皮纹理，带有纵向纹理和木节",
        ["Cobblestone"] = "生成鹅卵石/砾石地面纹理。",
        ["Flagstone"] = "生成石板铺装纹理",
        ["Grass"] = "生成草地/草丛纹理。",
        ["Moss"] = "生成苔藓覆盖表面纹理",
        ["Ice"] = "生成冰面或冰冻表面纹理",
        ["Starfield"] = "生成星空/夜空背景。",
        ["LavaFlow"] = "生成流动的熔岩动画纹理",
        ["Fabric"] = "生成布料或纺织品纹理",
        ["Chainmail"] = "生成锁子甲或金属环纹理",
        ["Camo"] = "生成迷彩图案纹理",
        ["Mosaic"] = "生成马赛克镶嵌纹理",
        ["Leather"] = "生成皮革纹理，带有毛孔和皱纹",
        ["Fence"] = "生成栅栏纹理，带有木纹和钉子",
    };

    /// <summary>Gets the localized node description for the given type name.</summary>
    public static string GetNodeDescription(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return "";
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var dict = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? NodeDescriptionsZhHans
            : NodeDescriptionsEn;
        return dict.TryGetValue(typeName, out var desc) ? desc : "";
    }

    // ─── Display list building ────────────────────────────────────────

    /// <summary>
    /// Builds a filtered, sorted list of <see cref="NodeLibraryEntry"/> from library items.
    /// Returns a plain list — the caller batches it into the ObservableCollection.
    /// </summary>
    public static List<NodeLibraryEntry> BuildNodeLibraryList(
        ObservableCollection<NodeLibraryItem> library,
        NodeLibraryCategory? selectedCategory,
        string searchText = "")
    {
        var allCategoryLabel = Loc.GetString("LibCat_All");
        var categoryKey = selectedCategory?.Key;
        bool isAllCategory = selectedCategory != null
            && string.Equals(selectedCategory.Name, allCategoryLabel, StringComparison.Ordinal);

        IEnumerable<NodeLibraryItem> filtered = library;

        // Apply category filter UNLESS "All" is selected or categoryKey is empty
        if (!string.IsNullOrWhiteSpace(categoryKey) && !isAllCategory)
        {
            filtered = library.Where(item =>
                string.Equals(item.CategoryKey, categoryKey, StringComparison.Ordinal));
        }

        // Apply search text filter
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || PinyinHelper.MatchesPinyin(item.Name, searchText));
        }

        return filtered
            .OrderBy(item => item.CategoryKey)
            .ThenBy(item => item.Subcategory ?? "")
            .ThenBy(item => item.Name)
            .Select(NodeLibraryEntry.CreateItem)
            .ToList();
    }

    /// <summary>
    /// Returns a search-text filter predicate for the <c>ICollectionView</c>.
    /// Category filtering is already done in <see cref="BuildNodeLibraryList"/>.
    /// </summary>
    public static Predicate<object> CreateSearchFilter(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return _ => true;

        return item =>
        {
            if (item is not NodeLibraryEntry entry) return false;
            if (entry.IsHeader) return true;
            if (entry.Item == null) return false;

            return entry.Item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || PinyinHelper.MatchesPinyin(entry.Item.Name, searchText);
        };
    }

    // ─── Template files ────────────────────────────────────────────────

    public static void RefreshTemplateFiles(ObservableCollection<TemplateFileInfo> templateFiles)
    {
        templateFiles.Clear();
        var templatesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
        if (!System.IO.Directory.Exists(templatesDir))
        {
            try { System.IO.Directory.CreateDirectory(templatesDir); } catch { }
            return;
        }

        foreach (var filePath in System.IO.Directory.GetFiles(templatesDir, "*.pixelgraph"))
        {
            var displayName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var previewPath = System.IO.Path.Combine(templatesDir, displayName + ".png");
            templateFiles.Add(new TemplateFileInfo
            {
                FileName = System.IO.Path.GetFileName(filePath),
                DisplayName = displayName,
                FullPath = filePath,
                PreviewPath = System.IO.File.Exists(previewPath) ? previewPath : ""
            });
        }
    }

}

/// <summary>
/// Represents a template (.pixelgraph) file in the templates directory.
/// </summary>
public class TemplateFileInfo
{
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FullPath { get; set; } = "";
    /// <summary>Preview image path (same name, .png extension). Empty if not found.</summary>
    public string PreviewPath { get; set; } = "";
    public bool HasPreview => !string.IsNullOrEmpty(PreviewPath) && System.IO.File.Exists(PreviewPath);
}
