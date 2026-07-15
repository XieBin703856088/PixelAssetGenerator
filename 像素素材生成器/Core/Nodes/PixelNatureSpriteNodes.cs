using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

public abstract class PixelNatureSpriteNodeBase : PixelMaterialNodeBase
{
    protected static int Position(int size, float normalized, int unit = 1)
        => Snap((int)MathF.Round((size - 1) * normalized), unit);

    protected static int Distance(int size, float normalized, int unit = 1)
        => Math.Max(unit, Snap((int)MathF.Round(size * normalized), unit));

    protected static int Jitter(int seed, int salt, int radius)
        => radius <= 0 ? 0 : (int)MathF.Round((HashToUnit(salt, seed, seed + salt * 17) - 0.5f) * radius * 2f);

    protected static int DetailUnit(int size, int pixelSize = 1)
        => Math.Max(1, (int)MathF.Round(pixelSize * size / 96f));

    protected static int OutlinePixels(int size, int requested)
        => requested <= 0 ? 0 : Math.Max(1, requested * (int)MathF.Round(Math.Max(1f, size / 40f)));

    protected static Color DarkOutline(Color primary, Color secondary)
        => PixelMaterialUtility.Shade(PixelMaterialUtility.Mix(primary, secondary, 0.35f), -0.72f);

    protected static int LightSide(string direction) => direction switch
    {
        "topRight" => 1,
        "front" => 0,
        _ => -1
    };

    private static int Snap(int value, int unit)
    {
        unit = Math.Max(1, unit);
        return (int)MathF.Round(value / (float)unit) * unit;
    }
}

/// <summary>
/// Front-facing RPG tree sprite. The silhouette is assembled from intentional canopy
/// tiers and binary masks; the lighting is painted as clusters instead of gradients.
/// </summary>
public sealed class PixelTreeNode : PixelNatureSpriteNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("treeType", "broadleaf",
            ["broadleaf", "pine", "layered", "palm", "dead"],
            ["阔叶树", "针叶树", "层叠树", "棕榈树", "枯树"], "树型"),
        NodeParameterDefinition.Number("trunkHeight", 0.3, 0.1, 0.8, 0.05, "树干高度"),
        NodeParameterDefinition.Number("canopySize", 0.5, 0.2, 1, 0.05, "树冠大小"),
        NodeParameterDefinition.Integer("canopyLayers", 3, 2, 6, 1, "树冠层数"),
        NodeParameterDefinition.Color("trunkColor", Color.FromRgb(139, 94, 60), "树干颜色"),
        NodeParameterDefinition.Color("leafColor", Color.FromRgb(74, 140, 63), "叶子颜色"),
        NodeParameterDefinition.Color("leafColor2", Color.FromRgb(80, 200, 60), "叶子高光"),
        NodeParameterDefinition.Integer("trunkWidth", 3, 1, 8, 1, "树干宽度"),
        NodeParameterDefinition.Integer("pixelSize", 2, 1, 8, 1, "像素簇大小"),
        NodeParameterDefinition.Integer("outlineWidth", 1, 0, 3, 1, "轮廓宽度"),
        NodeParameterDefinition.Choice("lightDirection", "topLeft",
            ["topLeft", "topRight", "front"], ["左上", "右上", "正面"], "光照方向")
    };

    public override string TypeName => "Tree";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var treeType = GetChoice(parameters, "treeType", "broadleaf");
        var trunkHeight = Math.Clamp(GetFloat(parameters, "trunkHeight", 0.3f), 0.1f, 0.8f);
        var canopyScale = Math.Clamp(GetFloat(parameters, "canopySize", 0.5f), 0.2f, 1f);
        var canopyLayers = Math.Clamp(GetInt(parameters, "canopyLayers", 3), 2, 6);
        var requestedTrunkWidth = Math.Clamp(GetInt(parameters, "trunkWidth", 3), 1, 8);
        var pixelSize = Math.Clamp(GetInt(parameters, "pixelSize", 2), 1, 8);
        var outline = OutlinePixels(size, Math.Clamp(GetInt(parameters, "outlineWidth", 1), 0, 3));
        var lightSide = LightSide(GetChoice(parameters, "lightDirection", "topLeft"));
        var trunk = GetColor(parameters, "trunkColor", Color.FromRgb(139, 94, 60));
        var leaf = GetColor(parameters, "leafColor", Color.FromRgb(74, 140, 63));
        var leafLight = GetColor(parameters, "leafColor2", Color.FromRgb(80, 200, 60));
        var palette = new TreePalette(
            DarkOutline(leaf, trunk),
            PixelMaterialUtility.Shade(trunk, -0.38f),
            trunk,
            PixelMaterialUtility.Shade(trunk, 0.28f),
            PixelMaterialUtility.Shade(leaf, -0.38f),
            leaf,
            leafLight);

        var canvas = new PixelSpriteCanvas(size, size);
        var unit = DetailUnit(size, pixelSize);
        var centerX = Position(size, 0.5f, unit) + Jitter(seed, 3, unit);
        var groundY = Position(size, 0.92f, unit);

        switch (treeType)
        {
            case "pine":
                RenderPine(canvas, centerX, groundY, trunkHeight, canopyScale, canopyLayers,
                    requestedTrunkWidth, unit, outline, lightSide, seed, palette);
                break;
            case "layered":
                RenderLayered(canvas, centerX, groundY, trunkHeight, canopyScale, canopyLayers,
                    requestedTrunkWidth, unit, outline, lightSide, seed, palette);
                break;
            case "palm":
                RenderPalm(canvas, centerX, groundY, trunkHeight, canopyScale,
                    requestedTrunkWidth, unit, outline, lightSide, seed, palette);
                break;
            case "dead":
                RenderDeadTree(canvas, centerX, groundY, trunkHeight, canopyScale,
                    requestedTrunkWidth, unit, outline, lightSide, seed, palette);
                break;
            default:
                RenderBroadleaf(canvas, centerX, groundY, trunkHeight, canopyScale, canopyLayers,
                    requestedTrunkWidth, unit, outline, lightSide, seed, palette);
                break;
        }

        return canvas.ToPixelBuffer();
    }

    private static void RenderBroadleaf(PixelSpriteCanvas canvas, int centerX, int groundY,
        float trunkHeight, float canopyScale, int layers, int requestedTrunkWidth, int unit,
        int outline, int lightSide, int seed, TreePalette palette)
    {
        var width = Distance(canvas.Width, 0.38f + canopyScale * 0.42f, unit);
        var height = Distance(canvas.Height, 0.34f + canopyScale * 0.28f, unit);
        var crownBottom = groundY - Distance(canvas.Height, 0.08f + trunkHeight * 0.24f, unit);
        var top = crownBottom - height;
        RenderTrunk(canvas, centerX, crownBottom, groundY, requestedTrunkWidth, unit,
            outline, lightSide, seed, palette);

        var crown = canvas.CreateMask();
        var rx = Math.Max(unit, width / 4);
        var ry = Math.Max(unit, height / 3);
        canvas.AddEllipse(crown, centerX + Jitter(seed, 11, unit), top + ry, rx, ry);
        canvas.AddEllipse(crown, centerX - width / 4 + Jitter(seed, 13, unit),
            top + height / 2, Math.Max(unit, width / 4), Math.Max(unit, height / 3));
        canvas.AddEllipse(crown, centerX + width / 4 + Jitter(seed, 17, unit),
            top + height / 2 + unit, Math.Max(unit, width / 4), Math.Max(unit, height / 3));
        canvas.AddEllipse(crown, centerX - width / 6, crownBottom - height / 4,
            Math.Max(unit, width / 3), Math.Max(unit, height / 4));
        canvas.AddEllipse(crown, centerX + width / 5, crownBottom - height / 4 - unit,
            Math.Max(unit, width / 3), Math.Max(unit, height / 4));

        for (var i = 3; i < layers; i++)
        {
            var side = (i & 1) == 0 ? -1 : 1;
            canvas.AddEllipse(crown,
                centerX + side * width / 3 + Jitter(seed, 31 + i, unit),
                top + height * (2 + i) / (layers + 4),
                Math.Max(unit, width / (5 + (i & 1))), Math.Max(unit, height / 5));
        }

        canvas.PaintOutline(crown, palette.Outline, outline);
        canvas.Paint(crown, palette.LeafBase);

        var shadow = canvas.CreateMask();
        var shadowX = centerX + (lightSide <= 0 ? width / 5 : -width / 5);
        canvas.AddEllipse(shadow, shadowX, crownBottom - height / 4,
            Math.Max(unit, width / 2), Math.Max(unit, height / 3));
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, shadow), palette.LeafShadow);

        var highlight = canvas.CreateMask();
        var highlightX = centerX + lightSide * width / 5;
        canvas.AddEllipse(highlight, highlightX, top + height / 3,
            Math.Max(unit, width / 5), Math.Max(unit, height / 7));
        canvas.AddEllipse(highlight, highlightX - lightSide * width / 5,
            top + height / 2, Math.Max(unit, width / 8), Math.Max(unit, height / 10));
        if (lightSide == 0)
            canvas.AddEllipse(highlight, centerX, top + height / 3, width / 6, height / 8);
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, highlight), palette.LeafLight);

        var pockets = canvas.CreateMask();
        canvas.AddEllipse(pockets, centerX - width / 5, crownBottom - height / 5,
            Math.Max(unit, width / 14), Math.Max(unit, height / 14));
        canvas.AddEllipse(pockets, centerX + width / 6, top + height / 2,
            Math.Max(unit, width / 16), Math.Max(unit, height / 16));
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, pockets), palette.LeafShadow);
    }

    private static void RenderPine(PixelSpriteCanvas canvas, int centerX, int groundY,
        float trunkHeight, float canopyScale, int layers, int requestedTrunkWidth, int unit,
        int outline, int lightSide, int seed, TreePalette palette)
    {
        var width = Distance(canvas.Width, 0.34f + canopyScale * 0.34f, unit);
        var height = Distance(canvas.Height, 0.48f + canopyScale * 0.32f, unit);
        var crownBottom = groundY - Distance(canvas.Height, 0.06f + trunkHeight * 0.12f, unit);
        var top = crownBottom - height;
        RenderTrunk(canvas, centerX, crownBottom, groundY, requestedTrunkWidth, unit,
            outline, lightSide, seed, palette);

        var crown = canvas.CreateMask();
        var tierCount = Math.Clamp(layers, 3, 6);
        for (var tier = 0; tier < tierCount; tier++)
        {
            var tierTop = top + height * tier / (tierCount + 1);
            var tierBottom = top + height * (tier + 2) / (tierCount + 1);
            var halfWidth = Math.Max(unit, width * (tier + 2) / (2 * (tierCount + 1)));
            canvas.AddPolygon(crown,
                new SpritePoint(centerX + Jitter(seed, 71 + tier, unit), tierTop),
                new SpritePoint(centerX - halfWidth, tierBottom),
                new SpritePoint(centerX - halfWidth / 3, tierBottom - unit),
                new SpritePoint(centerX, tierBottom + unit),
                new SpritePoint(centerX + halfWidth / 3, tierBottom - unit),
                new SpritePoint(centerX + halfWidth, tierBottom));
        }
        canvas.PaintOutline(crown, palette.Outline, outline);
        canvas.Paint(crown, palette.LeafBase);

        var shadow = canvas.CreateMask();
        var shadowSide = lightSide <= 0 ? 1 : -1;
        canvas.AddPolygon(shadow,
            new SpritePoint(centerX, top),
            new SpritePoint(centerX + shadowSide * width / 2, crownBottom),
            new SpritePoint(centerX, crownBottom));
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, shadow), palette.LeafShadow);

        var highlight = canvas.CreateMask();
        var highlightSide = lightSide == 0 ? -1 : lightSide;
        canvas.AddLine(highlight, centerX, top + height / 8,
            centerX + highlightSide * width / 5, top + height * 3 / 5, Math.Max(unit, outline));
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, highlight), palette.LeafLight);

        var seams = canvas.CreateMask();
        for (var tier = 1; tier < tierCount; tier++)
        {
            var y = top + height * (tier + 1) / (tierCount + 1);
            canvas.AddLine(seams, centerX - width * tier / (2 * tierCount), y,
                centerX + width * tier / (2 * tierCount), y, unit);
        }
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, seams), palette.LeafShadow);
    }

    private static void RenderLayered(PixelSpriteCanvas canvas, int centerX, int groundY,
        float trunkHeight, float canopyScale, int layers, int requestedTrunkWidth, int unit,
        int outline, int lightSide, int seed, TreePalette palette)
    {
        var width = Distance(canvas.Width, 0.40f + canopyScale * 0.38f, unit);
        var height = Distance(canvas.Height, 0.38f + canopyScale * 0.28f, unit);
        var crownBottom = groundY - Distance(canvas.Height, 0.09f + trunkHeight * 0.22f, unit);
        var top = crownBottom - height;
        RenderTrunk(canvas, centerX, crownBottom, groundY, requestedTrunkWidth, unit,
            outline, lightSide, seed, palette);

        var tierCount = Math.Clamp(layers, 2, 6);
        var tiers = new List<bool[]>(tierCount);
        var crown = canvas.CreateMask();
        for (var tier = 0; tier < tierCount; tier++)
        {
            var tierMask = canvas.CreateMask();
            var progress = tier / (float)Math.Max(1, tierCount - 1);
            var tierWidth = (int)MathF.Round(width * (0.58f + progress * 0.42f));
            var tierHeight = Math.Max(unit * 2, height / (tierCount + 1));
            var y = top + tierHeight / 2 + tier * (height - tierHeight) / Math.Max(1, tierCount - 1);
            canvas.AddEllipse(tierMask, centerX + Jitter(seed, 101 + tier, unit), y,
                Math.Max(unit, tierWidth / 2), tierHeight);
            tiers.Add(tierMask);
            for (var i = 0; i < crown.Length; i++)
                crown[i] |= tierMask[i];
        }
        canvas.PaintOutline(crown, palette.Outline, outline);
        canvas.Paint(crown, palette.LeafBase);
        for (var tier = 0; tier < tiers.Count; tier++)
        {
            if (tier > 0)
            {
                var seam = canvas.CreateMask();
                var y = top + tier * height / Math.Max(1, tierCount - 1);
                canvas.AddRectangle(seam, centerX - width / 2, y, centerX + width / 2, y + unit);
                canvas.Paint(PixelSpriteCanvas.Intersect(tiers[tier], seam), palette.LeafShadow);
            }
            var glint = canvas.CreateMask();
            var glintX = centerX + (lightSide == 0 ? -1 : lightSide) * width / 5;
            var glintY = top + (tier * 2 + 1) * height / (tierCount * 2);
            canvas.AddEllipse(glint, glintX, glintY, Math.Max(unit, width / 10), Math.Max(unit, height / 18));
            canvas.Paint(PixelSpriteCanvas.Intersect(tiers[tier], glint), palette.LeafLight);
        }
    }

    private static void RenderPalm(PixelSpriteCanvas canvas, int centerX, int groundY,
        float trunkHeight, float canopyScale, int requestedTrunkWidth, int unit,
        int outline, int lightSide, int seed, TreePalette palette)
    {
        var crownY = groundY - Distance(canvas.Height, 0.34f + trunkHeight * 0.36f, unit);
        var bend = Jitter(seed, 131, Distance(canvas.Width, 0.04f, unit));
        var trunkWidth = Math.Max(unit * 2, Distance(canvas.Width, requestedTrunkWidth / 42f, unit));
        var woody = canvas.CreateMask();
        canvas.AddLine(woody, centerX, groundY, centerX + bend, crownY, trunkWidth);
        canvas.AddLine(woody, centerX - trunkWidth, groundY, centerX, groundY, unit);
        canvas.AddLine(woody, centerX, groundY, centerX + trunkWidth, groundY, unit);
        canvas.PaintOutline(woody, palette.Outline, outline);
        canvas.Paint(woody, palette.TrunkBase);
        var trunkLight = canvas.CreateMask();
        canvas.AddLine(trunkLight, centerX + (lightSide <= 0 ? -1 : 1) * trunkWidth / 3,
            groundY - unit, centerX + bend + (lightSide <= 0 ? -1 : 1) * trunkWidth / 3,
            crownY + unit, unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(woody, trunkLight), palette.TrunkLight);

        var crown = canvas.CreateMask();
        var reach = Distance(canvas.Width, 0.22f + canopyScale * 0.20f, unit);
        var drop = Distance(canvas.Height, 0.12f + canopyScale * 0.08f, unit);
        var hubX = centerX + bend;
        canvas.AddEllipse(crown, hubX, crownY, Math.Max(unit, reach / 7), Math.Max(unit, drop / 5));
        var endpoints = new[]
        {
            new SpritePoint(hubX - reach, crownY + drop / 2),
            new SpritePoint(hubX - reach * 4 / 5, crownY - drop / 3),
            new SpritePoint(hubX - reach / 3, crownY - drop),
            new SpritePoint(hubX + reach / 3, crownY - drop),
            new SpritePoint(hubX + reach * 4 / 5, crownY - drop / 3),
            new SpritePoint(hubX + reach, crownY + drop / 2),
            new SpritePoint(hubX + reach / 2, crownY + drop)
        };
        foreach (var endpoint in endpoints)
        {
            canvas.AddLine(crown, hubX, crownY, endpoint.X, endpoint.Y, Math.Max(unit, outline));
            canvas.AddEllipse(crown, endpoint.X, endpoint.Y, Math.Max(unit, reach / 12), Math.Max(unit, drop / 7));
        }
        canvas.PaintOutline(crown, palette.Outline, outline);
        canvas.Paint(crown, palette.LeafBase);

        var shadow = canvas.CreateMask();
        canvas.AddRectangle(shadow, hubX, crownY, hubX + reach + outline, crownY + drop + outline);
        if (lightSide > 0)
        {
            shadow = canvas.CreateMask();
            canvas.AddRectangle(shadow, hubX - reach - outline, crownY,
                hubX, crownY + drop + outline);
        }
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, shadow), palette.LeafShadow);

        var highlight = canvas.CreateMask();
        var highlighted = lightSide >= 0 ? endpoints[4] : endpoints[1];
        canvas.AddLine(highlight, hubX, crownY, highlighted.X, highlighted.Y, unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(crown, highlight), palette.LeafLight);
    }

    private static void RenderDeadTree(PixelSpriteCanvas canvas, int centerX, int groundY,
        float trunkHeight, float canopyScale, int requestedTrunkWidth, int unit,
        int outline, int lightSide, int seed, TreePalette palette)
    {
        var top = groundY - Distance(canvas.Height, 0.50f + trunkHeight * 0.32f, unit);
        var width = Distance(canvas.Width, 0.20f + canopyScale * 0.22f, unit);
        var trunkWidth = Math.Max(unit * 2, Distance(canvas.Width, requestedTrunkWidth / 38f, unit));
        var wood = canvas.CreateMask();
        canvas.AddPolygon(wood,
            new SpritePoint(centerX - trunkWidth / 2, groundY),
            new SpritePoint(centerX - trunkWidth / 3, top + (groundY - top) / 3),
            new SpritePoint(centerX - unit, top),
            new SpritePoint(centerX + unit, top),
            new SpritePoint(centerX + trunkWidth / 2, groundY));
        var forkY = top + (groundY - top) / 3;
        canvas.AddLine(wood, centerX, forkY, centerX - width / 2, top + unit * 2, Math.Max(unit, trunkWidth / 2));
        canvas.AddLine(wood, centerX - width / 2, top + unit * 2,
            centerX - width / 2 - unit * 2, top, unit);
        canvas.AddLine(wood, centerX + unit, forkY + unit * 2,
            centerX + width / 2, top + (groundY - top) / 7, Math.Max(unit, trunkWidth / 2));
        canvas.AddLine(wood, centerX + width / 2, top + (groundY - top) / 7,
            centerX + width / 2 + unit * 2, top - unit, unit);
        canvas.AddLine(wood, centerX - trunkWidth, groundY, centerX + trunkWidth, groundY, unit);
        canvas.PaintOutline(wood, palette.Outline, outline);
        canvas.Paint(wood, palette.TrunkBase);

        var shadow = canvas.CreateMask();
        var shadowX = centerX + (lightSide <= 0 ? 0 : -width);
        canvas.AddRectangle(shadow, shadowX, top, shadowX + width, groundY);
        canvas.Paint(PixelSpriteCanvas.Intersect(wood, shadow), palette.TrunkShadow);
        var knots = canvas.CreateMask();
        canvas.AddEllipse(knots, centerX - unit, forkY + unit * 4, Math.Max(unit, trunkWidth / 3), unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(wood, knots), palette.Outline);
    }

    private static void RenderTrunk(PixelSpriteCanvas canvas, int centerX, int crownBottom, int groundY,
        int requestedWidth, int unit, int outline, int lightSide, int seed, TreePalette palette)
    {
        var width = Math.Max(unit * 2, Distance(canvas.Width, requestedWidth / 40f, unit));
        var trunk = canvas.CreateMask();
        canvas.AddPolygon(trunk,
            new SpritePoint(centerX - width / 3, crownBottom - unit * 2),
            new SpritePoint(centerX + width / 3, crownBottom - unit * 2),
            new SpritePoint(centerX + width / 2, groundY - unit),
            new SpritePoint(centerX + width, groundY),
            new SpritePoint(centerX, groundY - unit),
            new SpritePoint(centerX - width, groundY));
        var branchY = crownBottom + Math.Max(unit, (groundY - crownBottom) / 4);
        canvas.AddLine(trunk, centerX, branchY, centerX - width * 2, crownBottom - unit, Math.Max(unit, width / 2));
        canvas.AddLine(trunk, centerX, branchY + unit, centerX + width * 2, crownBottom, Math.Max(unit, width / 2));
        canvas.PaintOutline(trunk, palette.Outline, outline);
        canvas.Paint(trunk, palette.TrunkBase);

        var shadow = canvas.CreateMask();
        if (lightSide <= 0)
            canvas.AddRectangle(shadow, centerX, crownBottom - unit * 2, centerX + width * 2, groundY);
        else
            canvas.AddRectangle(shadow, centerX - width * 2, crownBottom - unit * 2, centerX, groundY);
        canvas.Paint(PixelSpriteCanvas.Intersect(trunk, shadow), palette.TrunkShadow);

        var highlight = canvas.CreateMask();
        var highlightX = centerX + (lightSide <= 0 ? -1 : 1) * Math.Max(unit, width / 4);
        canvas.AddLine(highlight, highlightX, crownBottom + unit,
            highlightX + Jitter(seed, 191, unit), groundY - unit * 2, unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(trunk, highlight), palette.TrunkLight);
    }

    private readonly record struct TreePalette(Color Outline, Color TrunkShadow, Color TrunkBase,
        Color TrunkLight, Color LeafShadow, Color LeafBase, Color LeafLight);
}

/// <summary>Compact RPG shrub sprite with a grounded silhouette and clustered light.</summary>
public sealed class PixelBushNode : PixelNatureSpriteNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("shape", "circle", ["circle", "ellipse", "irregular"],
            ["圆形", "横向", "不规则"], "形状"),
        NodeParameterDefinition.Number("size", 0.5, 0.1, 1, 0.01, "大小"),
        NodeParameterDefinition.Number("density", 0.6, 0.1, 1, 0.01, "枝叶密度"),
        NodeParameterDefinition.Integer("blobCount", 5, 2, 16, 1, "叶簇数量"),
        NodeParameterDefinition.Color("leafColor", Color.FromRgb(80, 160, 60), "叶子颜色"),
        NodeParameterDefinition.Color("shadowColor", Color.FromRgb(40, 90, 30), "阴影颜色"),
        NodeParameterDefinition.Color("highlightColor", Color.FromRgb(130, 220, 100), "高光颜色"),
        NodeParameterDefinition.Number("brightness", 0, -0.5, 0.5, 0.01, "亮度"),
        NodeParameterDefinition.Integer("outlineWidth", 1, 0, 3, 1, "轮廓宽度")
    };

    public override string TypeName => "Bush";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var shape = GetChoice(parameters, "shape", "circle");
        var scale = Math.Clamp(GetFloat(parameters, "size", 0.5f), 0.1f, 1f);
        var density = Math.Clamp(GetFloat(parameters, "density", 0.6f), 0.1f, 1f);
        var count = Math.Clamp(GetInt(parameters, "blobCount", 5), 2, 16);
        var brightness = GetFloat(parameters, "brightness", 0f);
        var leaf = PixelMaterialUtility.Adjust(
            GetColor(parameters, "leafColor", Color.FromRgb(80, 160, 60)), brightness, 0.06f, false);
        var shadow = PixelMaterialUtility.Adjust(
            GetColor(parameters, "shadowColor", Color.FromRgb(40, 90, 30)), brightness, 0.06f, false);
        var highlight = PixelMaterialUtility.Adjust(
            GetColor(parameters, "highlightColor", Color.FromRgb(130, 220, 100)), brightness, 0.06f, false);
        var outline = OutlinePixels(size, Math.Clamp(GetInt(parameters, "outlineWidth", 1), 0, 3));
        var canvas = new PixelSpriteCanvas(size, size);
        var unit = Math.Max(1, size / 64);
        var width = Distance(size, 0.34f + scale * 0.48f, unit);
        var heightFactor = shape == "ellipse" ? 0.47f : shape == "irregular" ? 0.64f : 0.58f;
        var height = Math.Max(unit * 3, (int)MathF.Round(width * heightFactor));
        var centerX = Position(size, 0.5f, unit);
        var bottom = Position(size, 0.82f, unit);
        var top = bottom - height;

        var bush = canvas.CreateMask();
        var usefulCount = Math.Clamp(count, 3, 10);
        for (var i = 0; i < usefulCount; i++)
        {
            var progress = usefulCount == 1 ? 0.5f : i / (float)(usefulCount - 1);
            var x = centerX - width / 2 + (int)MathF.Round(width * progress) + Jitter(seed, 211 + i, unit * 2);
            var arch = 1f - MathF.Abs(progress * 2f - 1f);
            var y = bottom - height / 3 - (int)MathF.Round(arch * height * 0.34f) + Jitter(seed, 241 + i, unit);
            if (shape == "irregular")
                y += Jitter(seed, 271 + i, Math.Max(unit, height / 8));
            var radiusX = Math.Max(unit * 2, width / (usefulCount + 1) +
                (int)MathF.Round(density * width / 12f));
            var radiusY = Math.Max(unit * 2, height / 3 + Jitter(seed, 301 + i, Math.Max(unit, height / 10)));
            canvas.AddEllipse(bush, x, y, radiusX, radiusY);
        }
        canvas.AddEllipse(bush, centerX, bottom - height / 3, width / 3, Math.Max(unit, height / 3));
        canvas.PaintOutline(bush, DarkOutline(shadow, leaf), outline);
        canvas.Paint(bush, leaf);

        var darkCluster = canvas.CreateMask();
        canvas.AddEllipse(darkCluster, centerX + width / 5, bottom - height / 4,
            width / 2, Math.Max(unit, height / 3));
        canvas.Paint(PixelSpriteCanvas.Intersect(bush, darkCluster), shadow);

        var lightCluster = canvas.CreateMask();
        canvas.AddEllipse(lightCluster, centerX - width / 5, top + height / 3,
            Math.Max(unit, width / 6), Math.Max(unit, height / 7));
        canvas.AddEllipse(lightCluster, centerX + width / 12, top + height / 2,
            Math.Max(unit, width / 10), Math.Max(unit, height / 10));
        canvas.Paint(PixelSpriteCanvas.Intersect(bush, lightCluster), highlight);

        var pockets = canvas.CreateMask();
        canvas.AddEllipse(pockets, centerX - width / 7, bottom - height / 4,
            Math.Max(unit, width / 18), Math.Max(unit, height / 14));
        canvas.AddEllipse(pockets, centerX + width / 4, top + height / 2,
            Math.Max(unit, width / 20), Math.Max(unit, height / 14));
        canvas.Paint(PixelSpriteCanvas.Intersect(bush, pockets), shadow);
        return canvas.ToPixelBuffer();
    }
}

/// <summary>Hard-edged fantasy mushroom sprite with separate cap, underside and stem layers.</summary>
public sealed class PixelMushroomNode : PixelNatureSpriteNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Choice("capType", "dome", ["dome", "flat", "conical", "trumpet"],
            ["圆顶", "扁平", "圆锥", "喇叭"], "伞帽类型"),
        NodeParameterDefinition.Number("size", 0.5, 0.1, 1, 0.01, "大小"),
        NodeParameterDefinition.Number("capRatio", 0.6, 0.3, 0.9, 0.01, "伞帽比例"),
        NodeParameterDefinition.Color("capColor", Color.FromRgb(200, 60, 60), "伞帽颜色"),
        NodeParameterDefinition.Color("capHighlight", Color.FromRgb(240, 120, 100), "伞帽高光"),
        NodeParameterDefinition.Color("stemColor", Color.FromRgb(220, 200, 180), "菌柄颜色"),
        NodeParameterDefinition.Boolean("spots", true, "斑点"),
        NodeParameterDefinition.Integer("outlineWidth", 1, 0, 3, 1, "轮廓宽度")
    };

    public override string TypeName => "Mushroom";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var capType = GetChoice(parameters, "capType", "dome");
        var scale = Math.Clamp(GetFloat(parameters, "size", 0.5f), 0.1f, 1f);
        var capRatio = Math.Clamp(GetFloat(parameters, "capRatio", 0.6f), 0.3f, 0.9f);
        var cap = GetColor(parameters, "capColor", Color.FromRgb(200, 60, 60));
        var capLight = GetColor(parameters, "capHighlight", Color.FromRgb(240, 120, 100));
        var stem = GetColor(parameters, "stemColor", Color.FromRgb(220, 200, 180));
        var spots = GetBool(parameters, "spots", true);
        var outline = OutlinePixels(size, Math.Clamp(GetInt(parameters, "outlineWidth", 1), 0, 3));
        var outlineColor = DarkOutline(cap, stem);
        var canvas = new PixelSpriteCanvas(size, size);
        var unit = Math.Max(1, size / 64);
        var totalHeight = Distance(size, 0.36f + scale * 0.46f, unit);
        var capHeight = Math.Max(unit * 4, (int)MathF.Round(totalHeight * capRatio));
        var stemHeight = Math.Max(unit * 4, totalHeight - capHeight / 3);
        var centerX = Position(size, 0.5f, unit) + Jitter(seed, 401, unit);
        var bottom = Position(size, 0.84f, unit);
        var capBottom = bottom - stemHeight + capHeight / 4;
        var capTop = capBottom - capHeight;
        var capWidth = Math.Max(unit * 6, (int)MathF.Round(capHeight *
            (capType == "flat" ? 2.05f : capType == "conical" ? 1.45f : 1.75f)));
        capWidth = Math.Min(capWidth, Distance(size, 0.82f, unit));
        var stemWidth = Math.Max(unit * 3, capWidth / 4);

        var stemMask = canvas.CreateMask();
        canvas.AddPolygon(stemMask,
            new SpritePoint(centerX - stemWidth / 3, capBottom - unit),
            new SpritePoint(centerX + stemWidth / 3, capBottom - unit),
            new SpritePoint(centerX + stemWidth / 2, bottom - unit),
            new SpritePoint(centerX + stemWidth / 3, bottom),
            new SpritePoint(centerX - stemWidth / 2, bottom),
            new SpritePoint(centerX - stemWidth / 2, bottom - unit));
        canvas.PaintOutline(stemMask, outlineColor, outline);
        canvas.Paint(stemMask, stem);
        var stemShadow = canvas.CreateMask();
        canvas.AddRectangle(stemShadow, centerX, capBottom - unit, centerX + stemWidth, bottom);
        canvas.Paint(PixelSpriteCanvas.Intersect(stemMask, stemShadow), PixelMaterialUtility.Shade(stem, -0.28f));
        var stemLight = canvas.CreateMask();
        canvas.AddLine(stemLight, centerX - stemWidth / 4, capBottom + unit,
            centerX - stemWidth / 3, bottom - unit * 2, unit);
        canvas.Paint(PixelSpriteCanvas.Intersect(stemMask, stemLight), PixelMaterialUtility.Shade(stem, 0.22f));

        var capMask = canvas.CreateMask();
        switch (capType)
        {
            case "flat":
                canvas.AddPolygon(capMask,
                    new SpritePoint(centerX - capWidth / 2, capBottom - capHeight / 3),
                    new SpritePoint(centerX - capWidth / 3, capTop + capHeight / 4),
                    new SpritePoint(centerX + capWidth / 3, capTop + capHeight / 4),
                    new SpritePoint(centerX + capWidth / 2, capBottom - capHeight / 3),
                    new SpritePoint(centerX + capWidth * 2 / 5, capBottom),
                    new SpritePoint(centerX - capWidth * 2 / 5, capBottom));
                break;
            case "conical":
                canvas.AddPolygon(capMask,
                    new SpritePoint(centerX + Jitter(seed, 431, unit), capTop),
                    new SpritePoint(centerX + capWidth / 5, capTop + capHeight / 3),
                    new SpritePoint(centerX + capWidth / 2, capBottom - unit),
                    new SpritePoint(centerX + capWidth / 3, capBottom),
                    new SpritePoint(centerX - capWidth / 3, capBottom),
                    new SpritePoint(centerX - capWidth / 2, capBottom - unit),
                    new SpritePoint(centerX - capWidth / 5, capTop + capHeight / 3));
                break;
            case "trumpet":
                canvas.AddPolygon(capMask,
                    new SpritePoint(centerX - capWidth / 2, capTop + capHeight / 5),
                    new SpritePoint(centerX - capWidth / 4, capTop),
                    new SpritePoint(centerX, capTop + capHeight / 5),
                    new SpritePoint(centerX + capWidth / 4, capTop),
                    new SpritePoint(centerX + capWidth / 2, capTop + capHeight / 5),
                    new SpritePoint(centerX + capWidth / 3, capBottom),
                    new SpritePoint(centerX - capWidth / 3, capBottom));
                break;
            default:
                canvas.AddEllipse(capMask, centerX, capBottom - capHeight / 3,
                    capWidth / 2, Math.Max(unit, capHeight * 2 / 3));
                var trim = canvas.CreateMask();
                canvas.AddRectangle(trim, 0, capBottom + unit, size - 1, size - 1);
                PixelSpriteCanvas.Subtract(capMask, trim);
                break;
        }
        canvas.PaintOutline(capMask, outlineColor, outline);
        canvas.Paint(capMask, cap);

        var underside = canvas.CreateMask();
        canvas.AddRectangle(underside, centerX - capWidth / 2, capBottom - Math.Max(unit, capHeight / 7),
            centerX + capWidth / 2, capBottom);
        canvas.Paint(PixelSpriteCanvas.Intersect(capMask, underside), PixelMaterialUtility.Shade(cap, -0.34f));

        var capHighlightMask = canvas.CreateMask();
        canvas.AddEllipse(capHighlightMask, centerX - capWidth / 5, capTop + capHeight / 3,
            Math.Max(unit, capWidth / 7), Math.Max(unit, capHeight / 8));
        canvas.Paint(PixelSpriteCanvas.Intersect(capMask, capHighlightMask), capLight);

        if (spots)
        {
            var spotMask = canvas.CreateMask();
            var spotCount = Math.Clamp(capWidth / Math.Max(3, unit * 4), 2, 5);
            for (var i = 0; i < spotCount; i++)
            {
                var x = centerX - capWidth / 3 + i * capWidth * 2 / Math.Max(1, (spotCount - 1) * 3)
                        + Jitter(seed, 461 + i, unit * 2);
                var y = capTop + capHeight / 3 + Jitter(seed, 491 + i, Math.Max(unit, capHeight / 6));
                canvas.AddEllipse(spotMask, x, y, Math.Max(unit, capWidth / 20), Math.Max(unit, capHeight / 14));
            }
            var visibleSpots = PixelSpriteCanvas.Intersect(capMask, spotMask);
            PixelSpriteCanvas.Subtract(visibleSpots, underside);
            canvas.Paint(visibleSpots, PixelMaterialUtility.Mix(stem, Colors.White, 0.38f));
        }

        return canvas.ToPixelBuffer();
    }
}
