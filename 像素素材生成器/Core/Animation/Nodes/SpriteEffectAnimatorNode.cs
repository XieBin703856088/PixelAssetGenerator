using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelAssetGenerator.Core.Animation.Nodes;

/// <summary>Visible one-node sprite effects designed for crisp 32/64 px animation.</summary>
public sealed class SpriteEffectAnimatorNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("图像", GraphPortType.Image, "image", true),
        new("强度", GraphPortType.Float, "strength")
    };
    private static readonly GraphNodePort[] Outputs = { new("图像", GraphPortType.Image, "image") };
    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("effect", "pulseGlow",
            ["pulseGlow", "flash", "dissolve", "hueCycle", "shimmer", "ghost", "outlinePulse"],
            ["呼吸发光", "受击闪白", "像素溶解", "色相循环", "流光扫过", "幽灵闪烁", "描边脉冲"], "动画效果"),
        NodeParameterDefinition.Number("speed", 1.0, 0.05, 8, 0.05, "速度"),
        NodeParameterDefinition.Number("strength", 0.75, 0, 2, 0.01, "强度"),
        NodeParameterDefinition.Number("phase", 0, 0, 1, 0.01, "相位"),
        NodeParameterDefinition.Color("effectColor", Color.FromRgb(120, 225, 255), "效果颜色"),
        NodeParameterDefinition.Integer("pixelStep", 1, 1, 4, 1, "像素块大小"),
        NodeParameterDefinition.Boolean("pingPong", true, "往返循环"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public override string TypeName => "SpriteEffectAnimator";
    public override string Category => "Animation";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.TimeDependent | GraphNodeTraits.Deterministic;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        var size = context.GetEffectiveSize();
        if (source == null) return PixelBuffer.CreateSolid(size, size, 0f, 0f, 0f, 0f);
        var effect = GetChoice(parameters, "effect", "pulseGlow");
        var speed = Math.Max(0.05f, GetFloat(parameters, "speed", 1f));
        var inputStrength = inputs.Length > 1 && inputs[1] != null ? inputs[1]!.GetPixel(0, 0).R : 1f;
        var strength = Math.Clamp(GetFloat(parameters, "strength", 0.75f) * inputStrength, 0f, 2f);
        var phase = GetFloat(parameters, "phase", 0f);
        var color = GetColor(parameters, "effectColor", Color.FromRgb(120, 225, 255));
        var tint = (R: color.R / 255f, G: color.G / 255f, B: color.B / 255f);
        var step = Math.Clamp(GetInt(parameters, "pixelStep", 1), 1, 4);
        var seed = GetInt(parameters, "seed", 42);
        var t = PositiveModulo((context.AnimationTime ?? 0f) * speed + phase, 1f);
        if (GetBool(parameters, "pingPong", true)) t = 1f - MathF.Abs(t * 2f - 1f);
        var wave = 0.5f + 0.5f * MathF.Sin((t + 0.75f) * MathF.Tau);
        var result = PixelBufferPool.Borrow(source.Width, source.Height);

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var sourcePixel = source.GetPixel(x, y);
            var r = sourcePixel.R;
            var g = sourcePixel.G;
            var b = sourcePixel.B;
            var a = sourcePixel.A;
            var edge = IsAlphaEdge(source, x, y);
            var bx = x / step * step;
            var by = y / step * step;
            var noise = TileableValueNoise(bx * 6f / source.Width, by * 6f / source.Height, 6, seed);

            // Effects modify the sprite itself; transparent canvas pixels must stay
            // transparent so dissolve bands never create unrelated coloured debris.
            if (sourcePixel.A <= 0.001f)
            {
                result.SetPixel(x, y, 0f, 0f, 0f, 0f);
                continue;
            }

            switch (effect)
            {
                case "flash":
                {
                    var flash = MathF.Pow(wave, 5f) * strength;
                    r = Lerp(r, tint.R, flash); g = Lerp(g, tint.G, flash); b = Lerp(b, tint.B, flash);
                    break;
                }
                case "dissolve":
                {
                    var threshold = t * 1.18f - 0.08f;
                    if (noise < threshold) a = 0f;
                    else if (noise < threshold + 0.12f * strength)
                    { r = tint.R; g = tint.G; b = tint.B; a = MathF.Max(a, 0.85f); }
                    break;
                }
                case "hueCycle":
                    RgbToHsv(r, g, b, out var hue, out var saturation, out var value);
                    HsvToRgb(PositiveModulo(hue + t * strength, 1f), saturation, value, out r, out g, out b);
                    break;
                case "shimmer":
                {
                    var band = PositiveModulo((x + y * 0.45f) / Math.Max(1f, source.Width) - t * 1.6f, 1f);
                    var shine = MathF.Pow(Math.Clamp(1f - MathF.Abs(band - 0.5f) * 8f, 0f, 1f), 2f) * strength;
                    r = Lerp(r, tint.R, shine); g = Lerp(g, tint.G, shine); b = Lerp(b, tint.B, shine);
                    break;
                }
                case "ghost":
                    a *= Math.Clamp(0.42f + wave * 0.58f * strength, 0f, 1f);
                    r = Lerp(r, tint.R, 0.18f * strength); g = Lerp(g, tint.G, 0.18f * strength); b = Lerp(b, tint.B, 0.18f * strength);
                    break;
                case "outlinePulse":
                    if (edge && a > 0.01f)
                    { var outline = Math.Clamp(0.35f + wave * strength, 0f, 1f); r = Lerp(r, tint.R, outline); g = Lerp(g, tint.G, outline); b = Lerp(b, tint.B, outline); }
                    break;
                default:
                {
                    var glow = wave * strength * (edge ? 0.55f : 0.26f);
                    r = Math.Clamp(r + tint.R * glow, 0f, 1f);
                    g = Math.Clamp(g + tint.G * glow, 0f, 1f);
                    b = Math.Clamp(b + tint.B * glow, 0f, 1f);
                    break;
                }
            }
            result.SetPixel(x, y, Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f), Math.Clamp(a, 0f, 1f));
        }
        return result;
    }

    private static bool IsAlphaEdge(PixelBuffer source, int x, int y)
    {
        if (source.GetPixel(x, y).A <= 0.01f) return false;
        return source.GetPixel(Mod(x - 1, source.Width), y).A <= 0.01f
            || source.GetPixel(Mod(x + 1, source.Width), y).A <= 0.01f
            || source.GetPixel(x, Mod(y - 1, source.Height)).A <= 0.01f
            || source.GetPixel(x, Mod(y + 1, source.Height)).A <= 0.01f;
    }

    private static float PositiveModulo(float value, float modulus)
    { var result = value % modulus; return result < 0f ? result + modulus : result; }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        var max = MathF.Max(r, MathF.Max(g, b)); var min = MathF.Min(r, MathF.Min(g, b)); var d = max - min;
        v = max; s = max <= 0.0001f ? 0f : d / max;
        if (d <= 0.0001f) { h = 0f; return; }
        h = max == r ? ((g - b) / d) % 6f : max == g ? (b - r) / d + 2f : (r - g) / d + 4f;
        h /= 6f; if (h < 0f) h += 1f;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        var c = v * s; var x = c * (1f - MathF.Abs((h * 6f) % 2f - 1f)); var m = v - c;
        (r, g, b) = h switch { < 1f / 6f => (c, x, 0f), < 2f / 6f => (x, c, 0f), < 3f / 6f => (0f, c, x), < 4f / 6f => (0f, x, c), < 5f / 6f => (x, 0f, c), _ => (c, 0f, x) };
        r += m; g += m; b += m;
    }
}
