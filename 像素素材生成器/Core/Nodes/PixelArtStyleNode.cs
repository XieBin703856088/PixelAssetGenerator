using System;
using System.Collections.Generic;
using PixelAssetGenerator.Core.PixelArt;

namespace PixelAssetGenerator.Core.Nodes;

/// <summary>
/// Explicit art-direction node for bringing imported or intermediate images into the
/// same discrete palette/cluster language as the procedural 32 px material nodes.
/// </summary>
public sealed class PixelArtStyleNode : GraphNodeBase
{
    private static readonly GraphNodePort[] Inputs =
    {
        new("图像", GraphPortType.Image, "image", IsRequired: true)
    };

    private static readonly GraphNodePort[] Outputs =
    {
        new("像素画", GraphPortType.Image, "image")
    };

    private static readonly NodeParameterDefinition[] ParameterDefinitions =
    {
        NodeParameterDefinition.Integer("paletteSize", 8, 2, 24, 1, "调色板颜色数"),
        NodeParameterDefinition.Integer("minimumClusterSize", 2, 1, 8, 1, "最小像素簇"),
        NodeParameterDefinition.Number("contrast", 1.12, 0.7, 1.6, 0.01, "对比度"),
        NodeParameterDefinition.Number("saturation", 1.06, 0, 1.6, 0.01, "饱和度"),
        NodeParameterDefinition.Choice("dither", "none", ["none", "bayer"], ["关闭", "Bayer 4×4"], "抖色"),
        NodeParameterDefinition.Number("ditherStrength", 0.35, 0, 1, 0.01, "抖色强度"),
        NodeParameterDefinition.Boolean("preserveAlpha", true, "保留透明度")
    };

    public override string TypeName => "PixelArtStyle";
    public override string Category => "ImageProcess";
    public override IReadOnlyList<GraphNodePort> InputPorts => Inputs;
    public override IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
    public override IReadOnlyList<NodeParameterDefinition> Parameters => ParameterDefinitions;

    public override PixelBuffer Process(PixelBuffer?[] inputs,
        IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        var source = inputs.Length > 0 ? inputs[0] : null;
        if (source == null)
            return PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0f, 0f, 0f, 0f);

        var dither = GetChoice(parameters, "dither", "none");
        var profile = new PixelArtStyleProfile(
            PaletteSize: Math.Clamp(GetInt(parameters, "paletteSize", 8), 2, 24),
            MinimumClusterSize: Math.Clamp(GetInt(parameters, "minimumClusterSize", 2), 1, 8),
            Contrast: Math.Clamp(GetFloat(parameters, "contrast", 1.12f), 0.7f, 1.6f),
            Saturation: Math.Clamp(GetFloat(parameters, "saturation", 1.06f), 0f, 1.6f),
            DitherStrength: string.Equals(dither, "bayer", StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(GetFloat(parameters, "ditherStrength", 0.35f), 0f, 1f)
                : 0f,
            PreserveAlpha: GetBool(parameters, "preserveAlpha", true));

        return PixelArtKernel.Stylize(source, profile);
    }
}
