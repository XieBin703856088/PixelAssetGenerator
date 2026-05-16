using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.Particles;

namespace PixelAssetGenerator.Core.Particles.Nodes;

/// <summary>
/// Renders particles as light sources that illuminate the background texture.
/// Each particle contributes additive glow to the output image based on its
/// color, size, and intensity. The resulting glow can be composited over
/// the existing scene.
/// </summary>
public sealed class ParticleLightNode : IGraphNode
{
    public string TypeName => "ParticleLight";
    public string Category => "Particle";

    private static readonly IReadOnlyList<GraphNodePort> _inputs = new[]
    {
        new GraphNodePort("Particles", GraphPortType.Particle),
        new GraphNodePort("Background", GraphPortType.Image),
    };

    private static readonly IReadOnlyList<GraphNodePort> _outputs = new[]
    {
        new GraphNodePort("Image", GraphPortType.Image),
    };

    private static readonly IReadOnlyList<NodeParameterDefinition> _parameters = new[]
    {
        NodeParameterDefinition.Number("glowRadius", 0.05, 0.005, 0.3, 0.005, "光晕半径"),
        NodeParameterDefinition.Number("intensity", 1.0, 0.0, 5.0, 0.1, "光照强度"),
        NodeParameterDefinition.Choice("compositeMode", "additive",
            new[] { "additive", "screen", "alpha" },
            new[] { "叠加", "滤色", "透明混合" }, "合成模式"),
        NodeParameterDefinition.Choice("colorSource", "particle",
            new[] { "particle", "white", "rainbow" },
            new[] { "粒子颜色", "白色", "彩虹" }, "光源颜色"),
        NodeParameterDefinition.Boolean("falloffEnabled", true, "启用衰减"),
        NodeParameterDefinition.Number("falloffPower", 2.0, 0.5, 4.0, 0.1, "衰减指数"),
    };

    public IReadOnlyList<GraphNodePort> InputPorts => _inputs;
    public IReadOnlyList<GraphNodePort> OutputPorts => _outputs;
    public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var background = inputs.Length > 1 ? inputs[1] : null;
        var size = context.TileSize;
        var output = background?.Clone() ?? PixelBufferPool.Borrow(size, size);

        // We need to find the particle buffer from upstream emitter
        // Since we don't have direct access here, the ParticleEvaluationService
        // handles rendering. This node produces the glow image which gets composited.
        // For now, return the background as-is; actual glow rendering is done
        // externally by ParticleEvaluationService.

        return output;
    }

    /// <summary>
    /// Renders particle glow onto an output buffer.
    /// Called by ParticleEvaluationService.
    /// </summary>
    public void RenderGlow(ParticleBuffer particleBuffer, PixelBuffer output,
        IReadOnlyDictionary<string, object> parameters)
    {
        var glowRadius = GraphNodeBase.GetFloat(parameters, "glowRadius", 0.05f);
        var intensity = GraphNodeBase.GetFloat(parameters, "intensity", 1f);
        var compositeMode = GraphNodeBase.GetChoice(parameters, "compositeMode", "additive");
        var colorSource = GraphNodeBase.GetChoice(parameters, "colorSource", "particle");
        var falloffEnabled = GraphNodeBase.GetBool(parameters, "falloffEnabled", true);
        var falloffPower = GraphNodeBase.GetFloat(parameters, "falloffPower", 2f);

        var w = output.Width;
        var h = output.Height;
        var data = output.AsSpan();
        var count = particleBuffer.ActiveCount;
        var particles = particleBuffer.ActiveSpan();
        var radiusPixels = Math.Max(glowRadius * w, 2f);
        var radiusSq = radiusPixels * radiusPixels;

        for (var i = 0; i < count; i++)
        {
            ref readonly var p = ref particles[i];
            if (!p.Active) continue;

            var cx = p.X * w;
            var cy = p.Y * h;

            // Determine light color
            float lr, lg, lb;
            switch (colorSource)
            {
                case "particle":
                    lr = p.R; lg = p.G; lb = p.B;
                    break;
                case "white":
                    lr = lg = lb = 1f;
                    break;
                case "rainbow":
                {
                    var hue = p.X + p.Life * 0.3f;
                    HueToRgb(hue, out lr, out lg, out lb);
                    break;
                }
                default:
                    lr = lg = lb = 1f;
                    break;
            }

            // Bounding box
            var minX = Math.Max(0, (int)(cx - radiusPixels));
            var maxX = Math.Min(w - 1, (int)(cx + radiusPixels));
            var minY = Math.Max(0, (int)(cy - radiusPixels));
            var maxY = Math.Min(h - 1, (int)(cy + radiusPixels));

            for (var py = minY; py <= maxY; py++)
            {
                for (var px = minX; px <= maxX; px++)
                {
                    var dx = px - cx;
                    var dy = py - cy;
                    var distSq = dx * dx + dy * dy;

                    if (distSq > radiusSq) continue;

                    // Glow falloff
                    var glow = falloffEnabled
                        ? MathF.Pow(1f - MathF.Sqrt(distSq) / radiusPixels, falloffPower)
                        : 1f - MathF.Sqrt(distSq) / radiusPixels;

                    glow = Math.Max(0f, glow) * intensity * p.A;

                    var idx = (py * w + px) * 4;
                    var srcR = lr * glow;
                    var srcG = lg * glow;
                    var srcB = lb * glow;

                    switch (compositeMode)
                    {
                        case "additive":
                            data[idx] = Math.Min(1f, data[idx] + srcR);
                            data[idx + 1] = Math.Min(1f, data[idx + 1] + srcG);
                            data[idx + 2] = Math.Min(1f, data[idx + 2] + srcB);
                            break;

                        case "screen":
                            data[idx] = 1f - (1f - data[idx]) * (1f - srcR);
                            data[idx + 1] = 1f - (1f - data[idx + 1]) * (1f - srcG);
                            data[idx + 2] = 1f - (1f - data[idx + 2]) * (1f - srcB);
                            break;

                        case "alpha":
                            var invA = 1f - glow;
                            data[idx] = data[idx] * invA + srcR;
                            data[idx + 1] = data[idx + 1] * invA + srcG;
                            data[idx + 2] = data[idx + 2] * invA + srcB;
                            data[idx + 3] = Math.Min(1f, data[idx + 3] + glow);
                            break;
                    }
                }
            }
        }
    }

    private static void HueToRgb(float hue, out float r, out float g, out float b)
    {
        var h = hue - MathF.Floor(hue); // [0, 1)
        var i = (int)(h * 6f);
        var f = h * 6f - i;
        var q = 1f - f;

        (r, g, b) = (i % 6) switch
        {
            0 => (1f, f, 0f),
            1 => (q, 1f, 0f),
            2 => (0f, 1f, f),
            3 => (0f, q, 1f),
            4 => (f, 0f, 1f),
            _ => (1f, 0f, q)
        };
    }
}
