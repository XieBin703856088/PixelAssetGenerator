using System;
using System.Collections.Generic;
using System.Windows.Media;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>Layered hard-edge smoke/steam/dust plume for small RPG effects.</summary>
public sealed class PixelSmokeNode : PixelMaterialNodeBase
{
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Seed("seed", 42, 0, 9999, "种子"),
        NodeParameterDefinition.Number("time", 0, 0, 1, 0.01, "时间"),
        NodeParameterDefinition.Choice("smokeType", "thick", ["thick", "mist", "steam", "dust"],
            ["浓烟", "薄雾", "蒸汽", "灰尘"], "烟雾类型"),
        NodeParameterDefinition.Number("size", 0.5, 0.1, 1, 0.01, "大小"),
        NodeParameterDefinition.Number("density", 0.6, 0, 1, 0.01, "密度"),
        NodeParameterDefinition.Number("speed", 0.5, 0, 2, 0.01, "速度"),
        NodeParameterDefinition.Number("wind", 0, -1, 1, 0.01, "风力"),
        NodeParameterDefinition.Integer("pixelSize", 1, 1, 4, 1, "像素大小"),
        NodeParameterDefinition.Color("smokeColor", Color.FromRgb(160, 150, 140), "烟雾颜色")
    };

    public override string TypeName => "Smoke";
    public override string Category => "Noise";
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var size = context.GetEffectiveSize();
        var seed = GetInt(parameters, "seed", context.Seed);
        var time = context.AnimationTime ?? Math.Clamp(GetFloat(parameters, "time", 0f), 0f, 1f);
        var type = GetChoice(parameters, "smokeType", "thick");
        var smokeSize = Math.Clamp(GetFloat(parameters, "size", 0.5f), 0.1f, 1f);
        var density = Math.Clamp(GetFloat(parameters, "density", 0.6f), 0f, 1f);
        var speed = Math.Clamp(GetFloat(parameters, "speed", 0.5f), 0f, 2f);
        var wind = Math.Clamp(GetFloat(parameters, "wind", 0f), -1f, 1f);
        var unit = Math.Clamp(GetInt(parameters, "pixelSize", 1), 1, 4);
        var color = GetColor(parameters, "smokeColor", Color.FromRgb(160, 150, 140));
        return Render(size, seed, time, type, smokeSize, density, speed, wind, unit, color);
    }

    private static PixelBuffer Render(int size, int seed, float time, string type,
        float smokeSize, float density, float speed, float wind, int unit, Color smokeColor)
    {
        var canvas = new PixelSpriteCanvas(size, size);
        if (density <= 0.01f)
            return canvas.ToPixelBuffer();

        var countMultiplier = type switch
        {
            "steam" => 0.72f,
            "mist" => 1.2f,
            "dust" => 0.9f,
            _ => 1f
        };
        var puffCount = Math.Clamp((int)MathF.Round((6 + density * 13) * countMultiplier), 4, 24);
        var puffs = new List<SmokePuff>(puffCount);

        for (var i = 0; i < puffCount; i++)
        {
            var phase = GraphNodeBase.HashToUnit(i, seed, 1801);
            var rate = type == "steam" ? 1.45f : type == "mist" ? 0.42f : type == "dust" ? 0.55f : 0.82f;
            var age = phase + time * speed * rate;
            age -= MathF.Floor(age);
            var lateralSeed = GraphNodeBase.HashToUnit(i, seed, 1811) - 0.5f;
            var wobbleSeed = GraphNodeBase.HashToUnit(i, seed, 1823) * MathF.Tau;
            float x;
            float y;
            float radiusX;
            float radiusY;

            if (type == "mist")
            {
                x = GraphNodeBase.HashToUnit(i, seed, 1829) * size +
                    wind * age * size * 0.32f + MathF.Sin(age * MathF.Tau + wobbleSeed) * size * 0.045f;
                x = GraphNodeBase.Mod((int)MathF.Round(x), size);
                y = size * (0.58f + lateralSeed * 0.22f - age * 0.12f);
                radiusX = size * (0.075f + smokeSize * 0.09f) * (0.8f + age * 0.55f);
                radiusY = radiusX * 0.48f;
            }
            else if (type == "dust")
            {
                x = size * (0.5f + lateralSeed * (0.28f + smokeSize * 0.2f)) +
                    wind * age * size * 0.42f + MathF.Sin(age * MathF.Tau * 1.2f + wobbleSeed) * size * 0.035f;
                y = size * (0.84f - age * 0.23f + MathF.Sin(wobbleSeed) * 0.035f);
                radiusX = size * (0.055f + smokeSize * 0.07f) * (0.85f + age * 0.7f);
                radiusY = radiusX * 0.62f;
            }
            else
            {
                var narrowness = type == "steam" ? 0.16f : 0.28f;
                var travel = type == "steam" ? 0.82f : 0.72f;
                x = size * (0.5f + lateralSeed * narrowness) + wind * age * size * 0.36f +
                    MathF.Sin(age * MathF.Tau * 1.35f + wobbleSeed) * size * (type == "steam" ? 0.035f : 0.065f);
                y = size * (0.91f - age * travel);
                radiusX = size * (type == "steam" ? 0.038f + smokeSize * 0.055f : 0.052f + smokeSize * 0.075f) *
                          (0.68f + age * (type == "steam" ? 0.65f : 0.95f));
                radiusY = radiusX * (type == "steam" ? 1.22f : 0.88f);
            }

            puffs.Add(new SmokePuff(Snap(x, unit), Snap(y, unit),
                Math.Max(unit, Snap(radiusX, unit)), Math.Max(unit, Snap(radiusY, unit)), age, i));
        }

        // Older, larger puffs sit behind newly emitted smoke at the source.
        puffs.Sort((left, right) => right.Age.CompareTo(left.Age));
        foreach (var puff in puffs)
        {
            var opacity = density * (1f - puff.Age * (type == "mist" ? 0.48f : 0.72f));
            opacity *= type == "steam" ? 0.74f : type == "mist" ? 0.62f : 0.92f;
            opacity = Quantize(Math.Clamp(opacity, 0.16f, 0.96f), 4);
            var bodyColor = type == "steam"
                ? PixelMaterialUtility.Shade(smokeColor, 0.24f)
                : type == "dust" ? PixelMaterialUtility.Shade(smokeColor, -0.08f) : smokeColor;
            var outlineColor = PixelMaterialUtility.Shade(bodyColor, type == "mist" ? -0.22f : -0.46f);
            var shadeColor = PixelMaterialUtility.Shade(bodyColor, -0.2f);
            var lightColor = PixelMaterialUtility.Shade(bodyColor, type == "steam" ? 0.34f : 0.22f);
            var body = canvas.CreateMask();
            canvas.AddEllipse(body, puff.X, puff.Y, puff.RadiusX, puff.RadiusY);
            canvas.AddEllipse(body, puff.X - puff.RadiusX / 2, puff.Y + unit,
                Math.Max(unit, puff.RadiusX * 2 / 3), Math.Max(unit, puff.RadiusY * 2 / 3));
            canvas.AddEllipse(body, puff.X + puff.RadiusX / 2, puff.Y + unit,
                Math.Max(unit, puff.RadiusX * 2 / 3), Math.Max(unit, puff.RadiusY * 2 / 3));
            canvas.PaintOutline(body, WithAlpha(outlineColor, opacity * 0.9f), unit);
            canvas.Paint(body, WithAlpha(bodyColor, opacity));

            var shade = canvas.CreateMask();
            canvas.AddEllipse(shade, puff.X + unit, puff.Y + Math.Max(unit, puff.RadiusY / 3),
                Math.Max(unit, puff.RadiusX - unit), Math.Max(unit, puff.RadiusY / 2));
            canvas.Paint(PixelSpriteCanvas.Intersect(shade, body), WithAlpha(shadeColor, opacity));

            if (GraphNodeBase.HashToUnit(puff.Index, seed, 1847) > 0.22f)
            {
                var highlight = canvas.CreateMask();
                canvas.AddEllipse(highlight, puff.X - Math.Max(unit, puff.RadiusX / 3),
                    puff.Y - Math.Max(unit, puff.RadiusY / 3),
                    Math.Max(unit, puff.RadiusX / 3), Math.Max(unit, puff.RadiusY / 4));
                canvas.Paint(PixelSpriteCanvas.Intersect(highlight, body), WithAlpha(lightColor, opacity));
            }
        }
        return canvas.ToPixelBuffer();
    }

    private static int Snap(float value, int unit)
        => (int)MathF.Round(value / Math.Max(1, unit)) * Math.Max(1, unit);

    private static float Quantize(float value, int steps)
        => MathF.Round(value * steps) / steps;

    private static Color WithAlpha(Color color, float alpha)
    {
        var quantized = Quantize(Math.Clamp(alpha, 0f, 1f), 4);
        return Color.FromArgb((byte)MathF.Round(quantized * 255f), color.R, color.G, color.B);
    }

    private readonly record struct SmokePuff(int X, int Y, int RadiusX, int RadiusY, float Age, int Index);
}
