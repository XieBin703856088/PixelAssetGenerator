using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

internal static class PixelDirectionalEffectRenderer
{
    public static PixelBuffer RenderWaterFlow(int size, int seed, float scale, float speed,
        float angle, float waveStrength, float foamAmount, Color deep, Color shallow,
        Color foam, float rippleStrength, float opacity, int octaves, bool invert, float time)
    {
        Color Adjust(Color color) => invert ? PixelMaterialUtility.Invert(color) : color;
        var palette = new[]
        {
            Adjust(PixelMaterialUtility.Shade(deep, -0.18f)),
            Adjust(deep),
            Adjust(PixelMaterialUtility.Mix(deep, shallow, 0.55f)),
            Adjust(shallow),
            Adjust(PixelMaterialUtility.Mix(shallow, foam, 0.58f)),
            Adjust(foam)
        };
        var tile = new PixelTileCanvas(size, 1);
        var harmonicCount = Math.Clamp((int)MathF.Round(scale + octaves * 0.45f), 2, 8);
        var phaseOffset = time * speed * MathF.Tau;
        var direction = GraphNodeBase.Mod((int)MathF.Round(angle), 360) / 360f;
        var macroCell = Math.Max(3, size / 7);
        var macroPeriod = Math.Max(2, size / macroCell);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var phaseX = x / (float)size * MathF.Tau;
            var phaseY = y / (float)size * MathF.Tau;
            var warpX = GraphNodeBase.TileableValueNoise(
                x / (float)(macroCell * 2), y / (float)(macroCell * 2),
                Math.Max(1, macroPeriod / 2), seed + 817);
            var warpY = GraphNodeBase.TileableValueNoise(
                x / (float)(macroCell * 2), y / (float)(macroCell * 2),
                Math.Max(1, macroPeriod / 2), seed + 819);
            var bend = 0.55f + waveStrength * 1.15f;
            var flow = PixelMaterialUtility.TileableDirectionalBands(
                phaseX + (warpX - 0.5f) * bend,
                phaseY + (warpY - 0.5f) * bend,
                direction, harmonicCount, phaseOffset);
            var crossRipple = PixelMaterialUtility.TileableDirectionalBands(
                phaseX + (warpY - 0.5f) * bend * 0.6f,
                phaseY + (warpX - 0.5f) * bend * 0.6f,
                direction + 0.25f, Math.Max(2, harmonicCount / 2), phaseOffset * 0.45f);
            var macro = GraphNodeBase.TileableFractalNoise(
                x / (float)macroCell, y / (float)macroCell, macroPeriod,
                Math.Clamp(octaves, 1, 4), 0.56f, 2f, seed + 823);
            var wave = GraphNodeBase.Lerp(flow,
                flow * 0.58f + macro * 0.28f + crossRipple * 0.14f, waveStrength);
            byte index = wave < 0.28f ? (byte)0
                : wave < 0.47f ? (byte)1
                : wave < 0.67f ? (byte)2
                : wave < 0.82f ? (byte)3 : (byte)4;
            var brokenLine = GraphNodeBase.HashToUnit(x / Math.Max(1, size / 16),
                y / Math.Max(1, size / 16), seed + 829);
            if (foamAmount > 0.05f && flow > 0.93f - foamAmount * 0.08f &&
                macro > 0.53f && brokenLine < 0.18f + foamAmount * 0.32f)
                index = 5;
            else if (rippleStrength > 0.18f && crossRipple > 0.84f &&
                     flow is > 0.58f and < 0.86f && brokenLine > 0.58f)
                index = 4;
            tile.Set(x, y, index);
        }

        var result = tile.ToPixelBuffer(palette);
        if (opacity < 0.999f)
            result.Apply((r, g, b, _) => (r, g, b, opacity));
        return result;
    }
}

public sealed class PixelLightningNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("time", 0, 0, 1, 0.01, "时间"),
        NodeParameterDefinition.Number("branching", 0.5, 0, 1, 0.01, "分支"),
        NodeParameterDefinition.Number("distortion", 0.5, 0, 1, 0.01, "扭曲"),
        NodeParameterDefinition.Number("brightness", 1, 0, 1, 0.01, "亮度"),
        NodeParameterDefinition.Number("thickness", 0.03, 0.005, 0.1, 0.005, "粗细"),
        NodeParameterDefinition.Integer("pixelSize", 1, 1, 4, 1, "像素大小"),
        NodeParameterDefinition.Color("boltColor", Color.FromRgb(180, 200, 255), "闪电颜色")
    };

    public override string TypeName => "Lightning";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var time = Math.Clamp(GetFloat(parameters, "time", 0f), 0f, 1f);
        var branching = Math.Clamp(GetFloat(parameters, "branching", 0.5f), 0f, 1f);
        var distortion = Math.Clamp(GetFloat(parameters, "distortion", 0.5f), 0f, 1f);
        var brightness = Math.Clamp(GetFloat(parameters, "brightness", 1f), 0f, 1f);
        var thickness = Math.Clamp(GetFloat(parameters, "thickness", 0.03f), 0.005f, 0.1f);
        var unit = Math.Max(1, GetInt(parameters, "pixelSize", 1));
        var bolt = GetColor(parameters, "boltColor", Color.FromRgb(180, 200, 255));
        var animationSeed = seed + (int)MathF.Round(time * 9973f);
        var canvas = new PixelSpriteCanvas(size, size);
        var outer = canvas.CreateMask();
        var body = canvas.CreateMask();
        var core = canvas.CreateMask();
        var segments = Math.Clamp(size / 4, 8, 18);
        var points = new List<SpritePoint>(segments + 1);
        var startX = Snap((int)MathF.Round(size * (0.38f + GraphNodeBase.HashToUnit(seed, 1, 839) * 0.24f)), unit);
        var endX = Snap((int)MathF.Round(size * (0.38f + GraphNodeBase.HashToUnit(seed, 2, 839) * 0.24f)), unit);
        for (var i = 0; i <= segments; i++)
        {
            var progress = i / (float)segments;
            var baseX = startX + (endX - startX) * progress;
            var envelope = MathF.Sin(progress * MathF.PI);
            var jitter = (GraphNodeBase.HashToUnit(i, animationSeed, 853) - 0.5f) *
                         size * (0.10f + distortion * 0.24f) * envelope;
            var x = Snap((int)MathF.Round(baseX + jitter), unit);
            var y = Snap((int)MathF.Round(size * (0.04f + progress * 0.91f)), unit);
            points.Add(new SpritePoint(x, y));
        }

        var mainThickness = Math.Max(unit, (int)MathF.Round(size * thickness));
        for (var i = 0; i < points.Count - 1; i++)
        {
            var left = points[i];
            var right = points[i + 1];
            canvas.AddLine(outer, left.X, left.Y, right.X, right.Y, mainThickness + unit * 2);
            canvas.AddLine(body, left.X, left.Y, right.X, right.Y, mainThickness + unit);
            canvas.AddLine(core, left.X, left.Y, right.X, right.Y, Math.Max(1, mainThickness / 2));

            if (i < 2 || i > points.Count - 4 ||
                GraphNodeBase.HashToUnit(i, animationSeed, 857) > branching * 0.48f)
                continue;
            var direction = GraphNodeBase.HashToUnit(i, animationSeed, 859) < 0.5f ? -1 : 1;
            var branchLength = size * (0.10f + branching * 0.18f) *
                               (0.65f + GraphNodeBase.HashToUnit(i, animationSeed, 863) * 0.35f);
            var branchMidX = Snap(left.X + direction * (int)MathF.Round(branchLength * 0.55f), unit);
            var branchMidY = Snap(left.Y + (int)MathF.Round(branchLength * 0.35f), unit);
            var branchEndX = Snap(left.X + direction * (int)MathF.Round(branchLength), unit);
            var branchEndY = Snap(left.Y + (int)MathF.Round(branchLength * 0.7f), unit);
            canvas.AddLine(outer, left.X, left.Y, branchMidX, branchMidY, mainThickness + unit);
            canvas.AddLine(outer, branchMidX, branchMidY, branchEndX, branchEndY, mainThickness + unit);
            canvas.AddLine(body, left.X, left.Y, branchMidX, branchMidY, mainThickness);
            canvas.AddLine(body, branchMidX, branchMidY, branchEndX, branchEndY, mainThickness);
        }

        var outlineColor = PixelMaterialUtility.Shade(bolt, -0.72f);
        var bodyColor = PixelMaterialUtility.Mix(PixelMaterialUtility.Shade(bolt, -0.15f), bolt, brightness);
        var coreColor = PixelMaterialUtility.Mix(bolt, Colors.White, 0.42f + brightness * 0.4f);
        canvas.Paint(outer, outlineColor);
        canvas.Paint(body, bodyColor);
        canvas.Paint(core, coreColor);
        return canvas.ToPixelBuffer();
    }

    private static int Snap(int value, int unit)
        => (int)MathF.Round(value / (float)Math.Max(1, unit)) * Math.Max(1, unit);
}

public sealed class PixelSlimeNode : PixelNatureSpriteNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Color("color", Color.FromRgb(80, 200, 60), "主体颜色"),
        NodeParameterDefinition.Color("highlightColor", Color.FromRgb(200, 255, 160), "高光颜色"),
        NodeParameterDefinition.Number("viscosity", 0.5, 0, 1, 0.01, "黏度"),
        NodeParameterDefinition.Number("bubbles", 0.5, 0, 1, 0.01, "气泡"),
        NodeParameterDefinition.Number("scale", 1.5, 0.5, 5, 0.01, "大小")
    };

    public override string TypeName => "Slime";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var bodyColor = GetColor(parameters, "color", Color.FromRgb(80, 200, 60));
        var highlightColor = GetColor(parameters, "highlightColor", Color.FromRgb(200, 255, 160));
        var viscosity = Math.Clamp(GetFloat(parameters, "viscosity", 0.5f), 0f, 1f);
        var bubbles = Math.Clamp(GetFloat(parameters, "bubbles", 0.5f), 0f, 1f);
        var scale = Math.Clamp(GetFloat(parameters, "scale", 1.5f) / 2f, 0.35f, 1f);
        var unit = Math.Max(1, size / 64);
        var canvas = new PixelSpriteCanvas(size, size);
        var centerX = Position(size, 0.5f, unit) + Jitter(seed, 881, unit * 2);
        var ground = Position(size, 0.84f, unit);
        var width = Distance(size, 0.42f + scale * 0.34f + viscosity * 0.05f, unit);
        var height = Distance(size, 0.32f + scale * 0.32f - viscosity * 0.05f, unit);
        var body = canvas.CreateMask();
        canvas.AddEllipse(body, centerX, ground - height / 2, width / 2, height / 2);
        canvas.AddEllipse(body, centerX - width / 4, ground - height / 4,
            Math.Max(unit, width / 4), Math.Max(unit, height / 4));
        canvas.AddEllipse(body, centerX + width / 4, ground - height / 5,
            Math.Max(unit, width / 4), Math.Max(unit, height / 5));
        canvas.AddRectangle(body, centerX - width / 2 + unit, ground - height / 3,
            centerX + width / 2 - unit, ground);
        var outlineColor = PixelMaterialUtility.Shade(bodyColor, -0.68f);
        var shadowColor = PixelMaterialUtility.Shade(bodyColor, -0.34f);
        canvas.PaintOutline(body, outlineColor, Math.Max(1, size / 40));
        canvas.Paint(body, bodyColor);

        var shadow = canvas.CreateMask();
        canvas.AddEllipse(shadow, centerX + width / 5, ground - height / 4,
            width / 2, Math.Max(unit, height / 3));
        canvas.Paint(PixelSpriteCanvas.Intersect(body, shadow), shadowColor);
        var light = canvas.CreateMask();
        canvas.AddEllipse(light, centerX - width / 5, ground - height * 3 / 4,
            Math.Max(unit, width / 7), Math.Max(unit, height / 10));
        canvas.Paint(PixelSpriteCanvas.Intersect(body, light), highlightColor);

        var bubbleMask = canvas.CreateMask();
        var bubbleCount = Math.Clamp((int)MathF.Round(1 + bubbles * 4), 1, 5);
        for (var i = 0; i < bubbleCount; i++)
        {
            var bx = centerX - width / 3 + (int)(GraphNodeBase.HashToUnit(i, seed, 887) * width * 2 / 3f);
            var by = ground - height / 2 + (int)(GraphNodeBase.HashToUnit(i, seed, 889) * height / 3f);
            canvas.AddEllipse(bubbleMask, bx, by, unit, unit);
        }
        canvas.Paint(PixelSpriteCanvas.Intersect(body, bubbleMask),
            PixelMaterialUtility.Mix(bodyColor, highlightColor, 0.7f));

        var face = canvas.CreateMask();
        var eyeY = ground - height / 2 + unit;
        canvas.AddRectangle(face, centerX - width / 6 - unit, eyeY,
            centerX - width / 6 + unit, eyeY + unit * 2);
        canvas.AddRectangle(face, centerX + width / 6 - unit, eyeY,
            centerX + width / 6 + unit, eyeY + unit * 2);
        canvas.AddLine(face, centerX - unit * 2, eyeY + unit * 4,
            centerX + unit * 2, eyeY + unit * 4, unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(body, face), outlineColor);
        return canvas.ToPixelBuffer();
    }
}

public sealed class PixelIconNode : PixelNatureSpriteNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("iconType", "heart", ["heart", "star", "gear", "mapMarker", "trophy", "skull"],
            ["心形", "星形", "齿轮", "地图标记", "奖杯", "骷髅"], "图标类型"),
        NodeParameterDefinition.Number("size", 0.6, 0.1, 1, 0.01, "大小"),
        NodeParameterDefinition.Color("primaryColor", Color.FromRgb(220, 60, 60), "主颜色"),
        NodeParameterDefinition.Color("secondaryColor", Color.FromRgb(180, 40, 40), "辅助颜色")
    };

    public override string TypeName => "Icon";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var iconType = GetChoice(parameters, "iconType", "heart");
        var scale = Math.Clamp(GetFloat(parameters, "size", 0.6f), 0.1f, 1f);
        var primary = GetColor(parameters, "primaryColor", Color.FromRgb(220, 60, 60));
        var secondary = GetColor(parameters, "secondaryColor", Color.FromRgb(180, 40, 40));
        var canvas = new PixelSpriteCanvas(size, size);
        var centerX = size / 2;
        var centerY = size / 2;
        var radius = Math.Max(3, (int)MathF.Round(size * (0.18f + scale * 0.29f)));
        var shape = canvas.CreateMask();
        BuildIconMask(canvas, shape, iconType, centerX, centerY, radius);
        var outline = DarkOutline(primary, secondary);
        canvas.PaintOutline(shape, outline, Math.Max(1, size / 40));
        canvas.Paint(shape, primary);
        var shadow = canvas.CreateMask();
        canvas.AddRectangle(shadow, centerX, centerY - radius, centerX + radius * 2, centerY + radius * 2);
        canvas.Paint(PixelSpriteCanvas.Intersect(shape, shadow), secondary);
        var highlight = canvas.CreateMask();
        canvas.AddLine(highlight, centerX - radius / 2, centerY - radius / 2,
            centerX - radius / 5, centerY - radius / 2, Math.Max(1, size / 64));
        canvas.Paint(PixelSpriteCanvas.Intersect(shape, highlight),
            PixelMaterialUtility.Shade(primary, 0.34f));
        return canvas.ToPixelBuffer();
    }

    private static void BuildIconMask(PixelSpriteCanvas canvas, bool[] mask, string iconType,
        int cx, int cy, int radius)
    {
        switch (iconType)
        {
            case "star":
            {
                var points = new SpritePoint[10];
                for (var i = 0; i < points.Length; i++)
                {
                    var angle = -MathF.PI / 2f + i * MathF.PI / 5f;
                    var currentRadius = (i & 1) == 0 ? radius : radius * 0.44f;
                    points[i] = new SpritePoint(cx + (int)MathF.Round(MathF.Cos(angle) * currentRadius),
                        cy + (int)MathF.Round(MathF.Sin(angle) * currentRadius));
                }
                canvas.AddPolygon(mask, points);
                break;
            }
            case "gear":
            {
                canvas.AddEllipse(mask, cx, cy, radius * 3 / 4, radius * 3 / 4);
                var tooth = Math.Max(1, radius / 4);
                canvas.AddRectangle(mask, cx - tooth, cy - radius, cx + tooth, cy + radius);
                canvas.AddRectangle(mask, cx - radius, cy - tooth, cx + radius, cy + tooth);
                canvas.AddRectangle(mask, cx - radius * 3 / 4, cy - radius * 3 / 4,
                    cx - radius / 2, cy - radius / 2);
                canvas.AddRectangle(mask, cx + radius / 2, cy - radius * 3 / 4,
                    cx + radius * 3 / 4, cy - radius / 2);
                canvas.AddRectangle(mask, cx - radius * 3 / 4, cy + radius / 2,
                    cx - radius / 2, cy + radius * 3 / 4);
                canvas.AddRectangle(mask, cx + radius / 2, cy + radius / 2,
                    cx + radius * 3 / 4, cy + radius * 3 / 4);
                var hole = canvas.CreateMask();
                canvas.AddEllipse(hole, cx, cy, Math.Max(1, radius / 4), Math.Max(1, radius / 4));
                PixelSpriteCanvas.Subtract(mask, hole);
                break;
            }
            case "mapMarker":
            {
                canvas.AddEllipse(mask, cx, cy - radius / 4, radius * 2 / 3, radius * 2 / 3);
                canvas.AddPolygon(mask, new SpritePoint(cx - radius / 2, cy),
                    new SpritePoint(cx + radius / 2, cy), new SpritePoint(cx, cy + radius));
                var hole = canvas.CreateMask();
                canvas.AddEllipse(hole, cx, cy - radius / 4, Math.Max(1, radius / 5), Math.Max(1, radius / 5));
                PixelSpriteCanvas.Subtract(mask, hole);
                break;
            }
            case "trophy":
            {
                canvas.AddPolygon(mask, new SpritePoint(cx - radius * 2 / 3, cy - radius * 2 / 3),
                    new SpritePoint(cx + radius * 2 / 3, cy - radius * 2 / 3),
                    new SpritePoint(cx + radius / 2, cy), new SpritePoint(cx + radius / 5, cy + radius / 4),
                    new SpritePoint(cx - radius / 5, cy + radius / 4), new SpritePoint(cx - radius / 2, cy));
                canvas.AddEllipse(mask, cx - radius * 2 / 3, cy - radius / 5, radius / 3, radius / 3);
                canvas.AddEllipse(mask, cx + radius * 2 / 3, cy - radius / 5, radius / 3, radius / 3);
                canvas.AddRectangle(mask, cx - Math.Max(1, radius / 8), cy,
                    cx + Math.Max(1, radius / 8), cy + radius * 2 / 3);
                canvas.AddRectangle(mask, cx - radius / 2, cy + radius * 2 / 3,
                    cx + radius / 2, cy + radius * 5 / 6);
                break;
            }
            case "skull":
            {
                canvas.AddEllipse(mask, cx, cy - radius / 5, radius * 3 / 4, radius * 2 / 3);
                canvas.AddRectangle(mask, cx - radius / 2, cy, cx + radius / 2, cy + radius * 2 / 3);
                var holes = canvas.CreateMask();
                canvas.AddEllipse(holes, cx - radius / 3, cy - radius / 5,
                    Math.Max(1, radius / 5), Math.Max(1, radius / 4));
                canvas.AddEllipse(holes, cx + radius / 3, cy - radius / 5,
                    Math.Max(1, radius / 5), Math.Max(1, radius / 4));
                canvas.AddPolygon(holes, new SpritePoint(cx, cy),
                    new SpritePoint(cx - Math.Max(1, radius / 9), cy + radius / 5),
                    new SpritePoint(cx + Math.Max(1, radius / 9), cy + radius / 5));
                PixelSpriteCanvas.Subtract(mask, holes);
                break;
            }
            default:
                canvas.AddEllipse(mask, cx - radius / 3, cy - radius / 3, radius / 2, radius / 2);
                canvas.AddEllipse(mask, cx + radius / 3, cy - radius / 3, radius / 2, radius / 2);
                canvas.AddPolygon(mask, new SpritePoint(cx - radius * 5 / 6, cy - radius / 4),
                    new SpritePoint(cx + radius * 5 / 6, cy - radius / 4),
                    new SpritePoint(cx, cy + radius));
                break;
        }
    }
}
