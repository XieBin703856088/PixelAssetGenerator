using System.Diagnostics;

#if VORTICE
using Vortice.Direct3D11;
using PixelAssetGenerator.Core.Gpu;
#endif

namespace PixelAssetGenerator.Core
{
    /// <summary>
    /// GPU helper facade. All GPU work is routed through <see cref="Gpu.GpuCompute"/>
    /// (single shared D3D11 device) serialized by <see cref="Gpu.GpuScheduler"/>.
    /// When compiled without the <c>VORTICE</c> symbol every method returns false so
    /// callers can fall back to the CPU path.
    /// </summary>
    public static class GpuProcessor
    {
#if VORTICE
        /// <summary>
        /// GPU-accelerated seamless blend (offset + crossfade) between image and its half-offset wrap.
        /// </summary>
        public static bool TrySeamlessBlend(PixelBuffer input, PixelBuffer output, float blendWidth, int blendShape, int blendDirection, float blendStrength, bool showSeam)
        {
            if (input == null || output == null) return false;
            if (input.Width != output.Width || input.Height != output.Height) return false;
            try
            {
                var result = GpuScheduler.Instance.Execute(() =>
                {
                    var tex = GpuCompute.RasterizeSeamlessBlendToTexture(input, blendWidth, blendShape, blendDirection, blendStrength, showSeam);
                    if (tex == null) return (PixelBuffer?)null;
                    try { return GpuCompute.ReadTextureToPixelBuffer(tex); }
                    finally { try { tex.Dispose(); } catch { } }
                });
                if (result == null) return false;
                result.AsSpan().CopyTo(output.AsSpan());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuProcessor.TrySeamlessBlend 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// GPU-accelerated color adjustments: brightness, contrast, saturation, hue shift, gamma,
        /// tint multipliers, shadow/highlight clip, palette steps and invert.
        /// </summary>
        public static bool TryColorAdjust(PixelBuffer input, PixelBuffer output,
            float brightness, float contrast, float saturation, float hueShiftDegrees, float gamma,
            float colorTemp, float tintR, float tintG, float tintB,
            float shadowClip, float highlightClip, int paletteSteps, bool invert)
        {
            if (input == null || output == null) return false;
            if (input.Width != output.Width || input.Height != output.Height) return false;
            try
            {
                var result = GpuScheduler.Instance.Execute(() =>
                {
                    var tex = GpuCompute.RasterizeColorAdjustToTexture(input, brightness, contrast, saturation, hueShiftDegrees, gamma, colorTemp, tintR, tintG, tintB, shadowClip, highlightClip, paletteSteps, invert);
                    if (tex == null) return (PixelBuffer?)null;
                    try { return GpuCompute.ReadTextureToPixelBuffer(tex); }
                    finally { try { tex.Dispose(); } catch { } }
                });
                if (result == null) return false;
                result.AsSpan().CopyTo(output.AsSpan());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuProcessor.TryColorAdjust 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// GPU-accelerated image-based distortion (legacy). Delegates to <see cref="TryDistort"/>.
        /// </summary>
        public static bool TryDistort_Old(
            PixelBuffer input,
            PixelBuffer output,
            int seed,
            int distortType,
            float strength,
            float frequency,
            int octaves,
            float xStrength,
            float yStrength,
            float angle,
            float centerX,
            float centerY)
        {
            return TryDistort(input, output, seed, distortType, strength, frequency, octaves, xStrength, yStrength, angle, centerX, centerY);
        }

        /// <summary>
        /// GPU-accelerated pixelation.
        /// </summary>
        public static bool TryPixelate(
            PixelBuffer input,
            PixelBuffer output,
            int blockSize,
            int sampleMode,
            int paletteSteps,
            int ditherMode)
        {
            if (input == null || output == null) return false;
            if (input.Width != output.Width || input.Height != output.Height) return false;
            try
            {
                var result = GpuScheduler.Instance.Execute(() =>
                {
                    var tex = GpuCompute.RasterizePixelateToTexture(input, blockSize, sampleMode, paletteSteps, ditherMode);
                    if (tex == null) return (PixelBuffer?)null;
                    try { return GpuCompute.ReadTextureToPixelBuffer(tex); }
                    finally { try { tex.Dispose(); } catch { } }
                });
                if (result == null) return false;
                result.AsSpan().CopyTo(output.AsSpan());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuProcessor.TryPixelate 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// GPU-accelerated post-process (brightness, contrast, threshold, invert) applied in-place.
        /// </summary>
        public static bool TryPostProcess(
            PixelBuffer buffer,
            float brightness,
            float contrast,
            float threshLow,
            float threshHigh,
            bool invert,
            bool colorOutput)
        {
            if (buffer is null) return false;
            try
            {
                return GpuScheduler.Instance.Execute(() =>
                    GpuCompute.PostProcessInPlace(buffer, brightness, contrast, threshLow, threshHigh, invert, colorOutput));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuProcessor.TryPostProcess 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// GPU-accelerated image distortion.
        /// </summary>
        public static bool TryDistort(
            PixelBuffer input,
            PixelBuffer output,
            int seed,
            int distortType,
            float strength,
            float frequency,
            int octaves,
            float xStrength,
            float yStrength,
            float angle,
            float centerX,
            float centerY)
        {
            if (input == null || output == null) return false;
            if (input.Width != output.Width || input.Height != output.Height) return false;
            try
            {
                var result = GpuScheduler.Instance.Execute(() =>
                {
                    var tex = GpuCompute.RasterizeDistortToTexture(input, seed, distortType, strength, frequency, octaves, xStrength, yStrength, angle, centerX, centerY);
                    if (tex == null) return (PixelBuffer?)null;
                    try { return GpuCompute.ReadTextureToPixelBuffer(tex); }
                    finally { try { tex.Dispose(); } catch { } }
                });
                if (result == null) return false;
                result.AsSpan().CopyTo(output.AsSpan());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuProcessor.TryDistort 异常: {ex.Message}");
                return false;
            }
        }
#endif
    }
}
