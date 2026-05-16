#if VORTICE
using System.Collections.Generic;
using Vortice.Direct3D11;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Core.Gpu
{
    /// <summary>
    /// GPU-native node contract. Implement this when a node can produce its output
    /// entirely on the GPU and return an <see cref="ID3D11Texture2D"/> that contains
    /// the rendered RGBA tile. Returning null indicates the GPU path is not available
    /// for the current parameters and the evaluator should fall back to the CPU path.
    /// </summary>
    internal interface IGpuNativeNode
    {
        /// <summary>
        /// Process the node on the GPU and return a texture containing the result.
        /// Implementations must not call Present or otherwise depend on UI thread.
        /// The provided device/context are owned by the caller; do not dispose them.
        /// </summary>
        /// <param name="device">D3D11 device for resource creation.</param>
        /// <param name="context">D3D11 device context for dispatch / draw calls.</param>
        /// <param name="inputTextures">Upstream node textures (may contain nulls for missing inputs).</param>
        /// <param name="parameters">Node parameters dictionary.</param>
        /// <param name="graphContext">Graph evaluation context (tile size, seed).</param>
        /// <returns>GPU texture with result or null to signal fallback to CPU.</returns>
        ID3D11Texture2D? ProcessGpuNative(
            ID3D11Device device,
            ID3D11DeviceContext context,
            IReadOnlyList<ID3D11Texture2D?> inputTextures,
            IReadOnlyDictionary<string, object> parameters,
            PixelGraphContext graphContext);
    }
}
#endif
