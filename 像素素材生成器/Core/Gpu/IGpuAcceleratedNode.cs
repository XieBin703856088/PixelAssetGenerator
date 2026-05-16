using System.Collections.Generic;

namespace PixelAssetGenerator.Core;

/// <summary>
/// Optional interface nodes can implement to provide a GPU-accelerated processing path.
/// Implementations should return null to indicate the GPU path is not able to produce a result
/// (causes the evaluator to fall back to the CPU path).
/// </summary>
public interface IGpuAcceleratedNode
{
    PixelBuffer? ProcessGpu(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context);
}
