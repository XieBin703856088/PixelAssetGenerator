using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Applies reusable over-lifetime behaviours after particle integration.  It is a
/// chainable particle node and therefore affects only emitters connected upstream.
/// </summary>
public sealed class ParticleBehaviorNode : IGraphNode
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("粒子", GraphPortType.Particle, "particles", true)
    };

    private static readonly GraphNodePort[] Outputs =
    {
        new("粒子", GraphPortType.Particle, "particles")
    };

    private static readonly NodeParameterDefinition[] Definitions =
    {
        NodeParameterDefinition.Choice("behavior", "pulse",
            ["pulse", "flicker", "zigzag", "orbit", "drag", "colorCycle"],
            ["大小脉动", "明暗闪烁", "曲折运动", "速度环绕", "空气阻力", "色相循环"], "行为"),
        NodeParameterDefinition.Number("strength", 0.35, 0, 5, 0.01, "强度"),
        NodeParameterDefinition.Number("frequency", 3.0, 0.05, 30, 0.05, "频率"),
        NodeParameterDefinition.Number("phase", 0.0, 0, 1, 0.01, "相位"),
        NodeParameterDefinition.Boolean("randomPhase", true, "粒子随机相位"),
        NodeParameterDefinition.Seed("seed", 42, 0, 99999, "随机种子")
    };

    public string TypeName => "ParticleBehavior";
    public string Category => "Particle";
    public IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => Definitions;
    public GraphNodeTraits Traits => GraphNodeTraits.Stateful | GraphNodeTraits.TimeDependent;

    public PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
        => PixelBuffer.CreateSolid(1, 1, 0f, 0f, 0f, 0f);

    public void ApplyBehavior(ParticleBuffer buffer,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var behavior = GraphNodeBase.GetChoice(parameters, "behavior", "pulse");
        var strength = Math.Max(0f, GraphNodeBase.GetFloat(parameters, "strength", 0.35f));
        var frequency = Math.Max(0.05f, GraphNodeBase.GetFloat(parameters, "frequency", 3f));
        var phase = GraphNodeBase.GetFloat(parameters, "phase", 0f);
        var randomPhase = GraphNodeBase.GetBool(parameters, "randomPhase", true);
        var seed = GraphNodeBase.GetInt(parameters, "seed", 42);
        var span = buffer.AsSpan();

        for (var i = 0; i < buffer.ActiveCount; i++)
        {
            ref var particle = ref span[i];
            if (!particle.Active) continue;

            var age = Math.Clamp(1f - particle.Life, 0f, 1f);
            var particlePhase = randomPhase ? GraphNodeBase.HashToUnit(i, seed, 2213) : 0f;
            var cycle = (age * frequency + phase + particlePhase) * MathF.Tau;

            switch (behavior)
            {
                case "flicker":
                {
                    var baseAlpha = Lerp(particle.StartA, particle.EndA, age);
                    var band = 0.5f + 0.5f * MathF.Sin(cycle);
                    particle.A = Math.Clamp(baseAlpha * (1f - strength * 0.75f + band * strength * 0.75f), 0f, 1f);
                    break;
                }
                case "zigzag":
                {
                    var speed = MathF.Sqrt(particle.VX * particle.VX + particle.VY * particle.VY);
                    if (speed > 0.0001f)
                    {
                        var nx = -particle.VY / speed;
                        var ny = particle.VX / speed;
                        var acceleration = MathF.Sin(cycle) * strength;
                        particle.VX += nx * acceleration * context.DeltaTime;
                        particle.VY += ny * acceleration * context.DeltaTime;
                    }
                    break;
                }
                case "orbit":
                {
                    var angle = strength * context.DeltaTime * (0.5f + frequency * 0.25f);
                    var cosine = MathF.Cos(angle);
                    var sine = MathF.Sin(angle);
                    var vx = particle.VX;
                    var vy = particle.VY;
                    particle.VX = vx * cosine - vy * sine;
                    particle.VY = vx * sine + vy * cosine;
                    break;
                }
                case "drag":
                {
                    var damping = MathF.Pow(Math.Clamp(1f - strength * 0.12f, 0.02f, 1f), context.DeltaTime * 60f);
                    particle.VX *= damping;
                    particle.VY *= damping;
                    break;
                }
                case "colorCycle":
                {
                    RgbToHsv(particle.R, particle.G, particle.B, out var hue, out var saturation, out var value);
                    hue = PositiveModulo(hue + context.GlobalTime * frequency * strength * 0.08f + particlePhase * 0.08f, 1f);
                    HsvToRgb(hue, saturation, value, out particle.R, out particle.G, out particle.B);
                    break;
                }
                default: // pulse
                {
                    var baseSize = Lerp(particle.StartSize, particle.EndSize, age);
                    var pulse = 1f + MathF.Sin(cycle) * Math.Min(strength, 0.95f);
                    particle.Size = Math.Max(0.0005f, baseSize * pulse);
                    break;
                }
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        v = max;
        s = max <= 0.0001f ? 0f : delta / max;
        if (delta <= 0.0001f)
        {
            h = 0f;
            return;
        }
        h = max == r
            ? ((g - b) / delta) % 6f
            : max == g ? (b - r) / delta + 2f : (r - g) / delta + 4f;
        h /= 6f;
        if (h < 0f) h += 1f;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        var c = v * s;
        var x = c * (1f - MathF.Abs((h * 6f) % 2f - 1f));
        var m = v - c;
        (r, g, b) = h switch
        {
            < 1f / 6f => (c, x, 0f),
            < 2f / 6f => (x, c, 0f),
            < 3f / 6f => (0f, c, x),
            < 4f / 6f => (0f, x, c),
            < 5f / 6f => (x, 0f, c),
            _ => (c, 0f, x)
        };
        r += m;
        g += m;
        b += m;
    }
}
