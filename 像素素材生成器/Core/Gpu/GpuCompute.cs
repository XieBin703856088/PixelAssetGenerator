#if VORTICE
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using ResourceUsage = Vortice.Direct3D11.Usage;

namespace PixelAssetGenerator.Core.Gpu
{
    // Minimal GPU compute helper using Direct3D11 (Vortice) to rasterize simple shapes.
    internal sealed class GpuCompute : IDisposable
    {
        // Consolidated HLSL source loaded from disk (Shaders.hlsl) to compile entry points on demand.
        private static string? s_consolidatedShaderSource;

        // Cache compiled compute shaders by entry point name to avoid repeated compilation latency.
        private readonly System.Collections.Generic.Dictionary<string, ID3D11ComputeShader> _shaderCache = new(System.StringComparer.Ordinal);

        // Reusable dynamic constant buffer used for tiled dispatch updates to avoid allocating many small buffers.
        private ID3D11Buffer? _dynamicTileConstantBuffer;
        private int _dynamicTileConstantBufferSize = 0;
        // Reusable render targets for double-buffered rendering to GPU textures.
        private ID3D11Texture2D[]? _renderTextures;
        private ID3D11UnorderedAccessView[]? _renderUavs;
        private ID3D11ShaderResourceView[]? _renderSrvs;
        // Cached shared handles for the render targets to avoid expensive per-frame queries
        private IntPtr[]? _renderSharedHandles;

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private ID3D11ComputeShader? _computeShader;

        private GpuCompute(ID3D11Device device, ID3D11DeviceContext context, ID3D11ComputeShader? cs)
        {
            _device = device;
            _context = context;
            _computeShader = cs;
            // shader cache initialized in field initializer
        }

        /// <summary>
        /// Kick off background initialization (non-blocking). Useful to warm the D3D device and reduce first-interaction latency.
        /// </summary>
        public static void InitializeInBackground()
        {
            // Fire-and-forget background initialization
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    EnsureInitialized();
                    // After device created, precompile common shaders to hide compile latency
                    try
                    {
                        var inst = s_instance;
                        if (inst != null)
                        {
                            // Pre-warm common entry points used in previews
                            _ = inst.GetOrCreateShader("CS_SolidColorMain");
                            _ = inst.GetOrCreateShader("CS_GradientMain");
                            _ = inst.GetOrCreateShader("CS_NoiseMain");
                            _ = inst.GetOrCreateShader("CS_FibersMain");
                            _ = inst.GetOrCreateShader("CS_WeaveMain");
                            _ = inst.GetOrCreateShader("CS_ConvolutionMain");
                            _ = inst.GetOrCreateShader("CS_CheckerboardMain");
                            _ = inst.GetOrCreateShader("CS_WoodMain");
                            _ = inst.GetOrCreateShader("CS_CloudMain");
                            _ = inst.GetOrCreateShader("CS_MarbleMain");
                            _ = inst.GetOrCreateShader("CS_LatticeMain");
                            _ = inst.GetOrCreateShader("CS_ConcentricMain");
                            _ = inst.GetOrCreateShader("CS_SpiralMain");
                            _ = inst.GetOrCreateShader("CS_HoneycombMain");
                            _ = inst.GetOrCreateShader("CS_WaveMain");
                            _ = inst.GetOrCreateShader("CS_NormalMapMain");
                        }
                    }
                    catch { }
                }
                catch { }
            });
        }

        private ID3D11ComputeShader? GetOrCreateShader(string entryPoint)
        {
            if (string.IsNullOrEmpty(entryPoint)) return null;
            lock (_shaderCache)
            {
                if (_shaderCache.TryGetValue(entryPoint, out var existing) && existing != null)
                    return existing;

                try
                {
                    // First, prefer precompiled bytecode emitted at build time: Core/Gpu/Shaders.<Entry>.cso
                    try
                    {
                        var outPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Core", "Gpu", $"Shaders.{entryPoint}.cso");
                        if (System.IO.File.Exists(outPath))
                        {
                            var bc = System.IO.File.ReadAllBytes(outPath);
                            var csFromBlob = _device.CreateComputeShader(bc);
                            if (csFromBlob != null)
                            {
                                _shaderCache[entryPoint] = csFromBlob;
                                return csFromBlob;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GpuCompute: 加载预编译着色器 {entryPoint} 失败: {ex.Message}");
                    }

                    // If no precompiled CSO was found on disk, try to load a precompiled
                    // shader embedded as a resource in the executing assembly. This lets
                    // builds that package shaders as embedded resources work without
                    // relying on the runtime D3D compiler.
                    try
                    {
                        var asm = System.Reflection.Assembly.GetExecutingAssembly();
                        // Resource name pattern: <Namespace>.Shaders.<Entry>.cso
                        var resourceName = $"{typeof(GpuCompute).Namespace}.Shaders.{entryPoint}.cso";
                        using var rs = asm.GetManifestResourceStream(resourceName);
                        if (rs != null)
                        {
                            var len = (int)rs.Length;
                            var bc = new byte[len];
                            var read = rs.Read(bc, 0, len);
                            if (read == len)
                            {
                                var csFromEmbedded = _device.CreateComputeShader(bc);
                                if (csFromEmbedded != null)
                                {
                                    _shaderCache[entryPoint] = csFromEmbedded;
                                    return csFromEmbedded;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GpuCompute: 加载嵌入式着色器 {entryPoint} 失败: {ex.Message}");
                    }

                    // Ensure consolidated source is available
                    var src = s_consolidatedShaderSource;
                    if (string.IsNullOrEmpty(src))
                    {
                        // Try to read from deployed location (output directory)
                        try
                        {
                            var p = System.IO.Path.Combine(AppContext.BaseDirectory, "Core", "Gpu", "Shaders.hlsl");
                            if (System.IO.File.Exists(p)) src = System.IO.File.ReadAllText(p);
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(src)) return null;

                    if (!GpuCompiler.TryCompile(src, entryPoint, "cs_5_0", out var bytecode, out var err))
                    {
                        // Surface compile errors to logs and keep last init error for UI/debugging
                        try
                        {
                            s_lastInitError = $"Shader compile failed for entry={entryPoint}: {err}";
                        }
                        catch { }
                        try { Debug.WriteLine($"GpuCompute: 着色器编译失败，入口点={entryPoint}: {err}"); } catch { }
                        try { System.Diagnostics.Trace.TraceError($"GpuCompute: shader compile failed for entry={entryPoint}: {err}"); } catch { }
                        return null;
                    }

                    var cs = _device.CreateComputeShader(bytecode);
                    if (cs != null) _shaderCache[entryPoint] = cs;
                    return cs;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GpuCompute: GetOrCreateShader 异常，入口点={entryPoint}: {ex.Message}");
                    return null;
                }
            }
        }

        private void EnsureDynamicTileConstantBuffer(int sizeInBytes)
        {
            if (_dynamicTileConstantBuffer != null && _dynamicTileConstantBufferSize >= sizeInBytes) return;
            try { _dynamicTileConstantBuffer?.Dispose(); } catch { }
            var cbd = new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                SizeInBytes = sizeInBytes,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            };
            _dynamicTileConstantBuffer = _device.CreateBuffer(cbd);
            _dynamicTileConstantBufferSize = sizeInBytes;
        }

        private bool UpdateDynamicTileConstantBuffer(int[] data)
        {
            if (data == null) return false;
            var size = sizeof(int) * data.Length;
            EnsureDynamicTileConstantBuffer(size);
            if (_dynamicTileConstantBuffer == null) return false;
            var mapped = _context.Map(_dynamicTileConstantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(data, 0, mapped.DataPointer, data.Length);
            }
            finally
            {
                _context.Unmap(_dynamicTileConstantBuffer, 0);
            }
            return true;
        }

        // Attempt to reset the singleton instance when the D3D device is reported removed/hung
        private static void ResetInstanceForDeviceRemoval(string? reason = null)
        {
            lock (s_lock)
            {
                try
                {
                    s_instance?.Dispose();
                }
                catch { }
                s_instance = null;
                s_cachedAdapterDescription = null;
                s_cachedFeatureLevel = null;
                s_cachedMemoryString = null;
                if (!string.IsNullOrEmpty(reason))
                    s_lastInitError = "Device removed: " + reason;
            }
        }

        // Check device removed reason and try to reinitialize once. Returns true if recovered.
        private static bool TryRecoverDeviceOnce()
        {
            try
            {
                var dev = s_instance?._device;
                if (dev == null) return false;
                // Try a best-effort recovery: assume device is bad if we reached here
                try { Debug.WriteLine("GpuCompute: 正在尝试恢复设备（设备报告失败）"); } catch { }
                ResetInstanceForDeviceRemoval(null);
                try
                {
                    EnsureInitialized();
                    return s_instance != null;
                }
                catch { return false; }
            }
            catch { }
            return false;
        }

        // Create UAV with recovery path: if creation fails due to device removal, try to recover and retry once.
        private static ID3D11UnorderedAccessView? CreateUnorderedAccessViewWithRecovery(ID3D11Texture2D tex)
        {
            if (tex == null) return null;
            try
            {
                return s_instance!._device.CreateUnorderedAccessView(tex);
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                // Query device removed and attempt recovery once
                try
                {
                    TryRecoverDeviceOnce();
                    if (s_instance != null)
                    {
                        try { return s_instance._device.CreateUnorderedAccessView(tex); } catch { }
                    }
                }
                catch { }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterize the weave pattern into a GPU texture and return it (caller owns the texture).
        /// </summary>
        internal static ID3D11Texture2D? RasterizeWeaveToTexture(int size, int density, float brightness, float contrast, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.

                var cs = s_instance.GetOrCreateShader("CS_WeaveMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = CreateUnorderedAccessViewWithRecovery(gpuTex);
                    if (uav == null) throw new InvalidOperationException("CreateUnorderedAccessView failed");

                    // Pack params (Density and Invert are ints on the HLSL side)
                    constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { density, brightness, contrast, invert ? 1 : 0 });

                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                    var tgX = (size + 7) / 8;
                    var tgY = (size + 7) / 8;
                    s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Specialized GPU rasterizer that implements the Fibers node algorithm in HLSL.
        /// Returns an R32G32B32A32_Float texture containing the result (caller owns it).
        /// This mirrors the CPU implementation so GPU preview matches node library preview.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeFibersToTexture(int size, float density, float safeWidth, float brightness, float contrast, float cosA, float sinA, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.

                var cs = s_instance.GetOrCreateShader("CS_FibersMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = CreateUnorderedAccessViewWithRecovery(gpuTex);
                    if (uav == null) throw new InvalidOperationException("CreateUnorderedAccessView failed");

                    // Pack parameters in same order as HLSL cbuffer
                    constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { density, safeWidth, brightness, contrast, cosA, sinA, invert ? 1 : 0, 0, 0, 0 });

                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                    var tgX = (size + 7) / 8;
                    var tgY = (size + 7) / 8;
                    s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShader(null);

                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Run a convolution on the shared device/context and return a GPU texture containing the result.
        /// Caller owns and must dispose the returned texture.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeConvolutionToTexture(PixelBuffer input, float[] kernel, int kernelSize, float divisor, float strength, float mixRatio, bool preserveAlpha)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;

            try
            {
                var width = input.Width;
                var height = input.Height;
                if (width <= 0 || height <= 0) return null;

                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.

                var cs = s_instance.GetOrCreateShader("CS_ConvolutionMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var srcTex = s_instance._device.CreateTexture2D(texDesc);
                var stagingDesc = texDesc;
                stagingDesc.Usage = ResourceUsage.Staging;
                stagingDesc.BindFlags = BindFlags.None;
                stagingDesc.CpuAccessFlags = CpuAccessFlags.Write;
                using var staging = s_instance._device.CreateTexture2D(stagingDesc);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    input.CopyTo(mapped.DataPointer, mapped.RowPitch);
                }
                finally { s_instance._context.Unmap(staging, 0); }

                s_instance._context.CopyResource(srcTex, staging);

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);
                using var outTexLocal = s_instance._device.CreateTexture2D(texDesc);
                using var outUav = CreateUnorderedAccessViewWithRecovery(outTexLocal) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // D3D11 constant buffers must be a multiple of 16 bytes; round up to nearest 4 floats.
                var flat = new float[84];
                if (kernel != null)
                {
                    for (int i = 0; i < Math.Min(kernel.Length, kernelSize * kernelSize); i++) flat[i] = kernel[i];
                }

                var hk = GCHandle.Alloc(flat, GCHandleType.Pinned);
                ID3D11Buffer kbuf = null!;
                try
                {
                    var cbd = new BufferDescription { Usage = ResourceUsage.Default, SizeInBytes = sizeof(float) * flat.Length, BindFlags = BindFlags.ConstantBuffer, CpuAccessFlags = CpuAccessFlags.None, OptionFlags = ResourceOptionFlags.None, StructureByteStride = 0 };
                    var init = new SubresourceData(hk.AddrOfPinnedObject(), 0, 0);
                    kbuf = s_instance._device.CreateBuffer(cbd, init);
                }
                finally { hk.Free(); }

                // KernelSize and PreserveAlpha are ints in the HLSL; Width/Height too.
                ID3D11Buffer paramBuf = null!;
                paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { kernelSize, divisor, strength, mixRatio, preserveAlpha ? 1 : 0, width, height, 0 });

                try
                {
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                    s_instance._context.CSSetConstantBuffers(1, 1, new[] { kbuf });
                    s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { outUav }, new int[] { -1 });

                    var tgx = (width + 7) / 8;
                    var tgy = (height + 7) / 8;
                    s_instance._context.Dispatch(tgx, tgy, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });
                }
                finally
                {
                    try { kbuf?.Dispose(); } catch { }
                    try { paramBuf?.Dispose(); } catch { }
                }

                // Create a texture to return and copy results into it
                var returnTex = s_instance._device.CreateTexture2D(texDesc);
                s_instance._context.CopyResource(returnTex, outTexLocal);
                return returnTex;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Run convolution shader using an existing GPU source texture
        /// Caller owns and must dispose the returned texture.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeConvolutionFromTexture(ID3D11Texture2D srcTex, float[] kernel, int kernelSize, float divisor, float strength, float mixRatio, bool preserveAlpha)
        {
            EnsureInitialized();
            if (s_instance == null || srcTex == null) return null;

            try
            {
                var desc = srcTex.Description;
                var width = desc.Width;
                var height = desc.Height;

                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.

                var cs = s_instance.GetOrCreateShader("CS_ConvolutionMain");
                
                if (cs == null) return null;

                // Create output texture compatible with swap chain (BGRA) for presentation.
                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);
                using var outTexLocal = s_instance._device.CreateTexture2D(texDesc);
                using var outUav = CreateUnorderedAccessViewWithRecovery(outTexLocal) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // kernel CB — D3D11 constant buffers must be a multiple of 16 bytes; round up to nearest 4 floats.
                var flat = new float[84];
                if (kernel != null)
                {
                    for (int i = 0; i < Math.Min(kernel.Length, kernelSize * kernelSize); i++) flat[i] = kernel[i];
                }

                var hk = GCHandle.Alloc(flat, GCHandleType.Pinned);
                ID3D11Buffer kbuf = null!;
                try
                {
                    var cbd = new BufferDescription { Usage = ResourceUsage.Default, SizeInBytes = sizeof(float) * flat.Length, BindFlags = BindFlags.ConstantBuffer, CpuAccessFlags = CpuAccessFlags.None, OptionFlags = ResourceOptionFlags.None, StructureByteStride = 0 };
                    var init = new SubresourceData(hk.AddrOfPinnedObject(), 0, 0);
                    kbuf = s_instance._device.CreateBuffer(cbd, init);
                }
                finally { hk.Free(); }

                // KernelSize and PreserveAlpha are ints in the HLSL; Width/Height too.
                ID3D11Buffer paramBuf = null!;
                paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { kernelSize, divisor, strength, mixRatio, preserveAlpha ? 1 : 0, width, height, 0 });

                try
                {
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                    s_instance._context.CSSetConstantBuffers(1, 1, new[] { kbuf });
                    s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { outUav }, new int[] { -1 });

                    var tgx = (width + 7) / 8;
                    var tgy = (height + 7) / 8;
                    s_instance._context.Dispatch(tgx, tgy, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });
                }
                finally
                {
                    try { kbuf?.Dispose(); } catch { }
                    try { paramBuf?.Dispose(); } catch { }
                }

                // Create a texture to return and copy results into it
                var returnTex = s_instance._device.CreateTexture2D(texDesc);
                s_instance._context.CopyResource(returnTex, outTexLocal);
                return returnTex;
            }
            catch
            {
                return null;
            }
        }

        internal static ID3D11Texture2D? RasterizeColorAdjustToTexture(PixelBuffer input,
            float brightness, float contrast, float saturation, float hueShiftDegrees, float gamma,
            float colorTemp, float tintR, float tintG, float tintB,
            float shadowClip, float highlightClip, int paletteSteps, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;

            try
            {
                var width = input.Width;
                var height = input.Height;
                if (width <= 0 || height <= 0) return null;

                var cs = s_instance.GetOrCreateShader("CS_ColorAdjustMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var srcTex = s_instance._device.CreateTexture2D(texDesc);
                var stagingDesc = texDesc; stagingDesc.Usage = ResourceUsage.Staging; stagingDesc.BindFlags = BindFlags.None; stagingDesc.CpuAccessFlags = CpuAccessFlags.Write;
                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                var mapped = s_instance._context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                try { input.CopyTo(mapped.DataPointer, mapped.RowPitch); }
                finally { s_instance._context.Unmap(staging, 0); }
                s_instance._context.CopyResource(srcTex, staging);

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);
                using var outTexLocal = s_instance._device.CreateTexture2D(texDesc);
                using var outUav = s_instance._device.CreateUnorderedAccessView(outTexLocal);

                // HLSL layout: floats..., PaletteSteps(float), Invert(int), Width(int), Height(int)
                ID3D11Buffer paramBuf = null!;
                paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { brightness, contrast, saturation, hueShiftDegrees, gamma, colorTemp, tintR, tintG, tintB, shadowClip, highlightClip, (float)paletteSteps, invert ? 1 : 0, width, height, 0 });

                try
                {
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                    s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { outUav }, new int[] { -1 });

                    var tgX = (width + 7) / 8; var tgY = (height + 7) / 8; s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                    s_instance._context.CSSetShader(null);

                    var returnTex = s_instance._device.CreateTexture2D(texDesc);
                    s_instance._context.CopyResource(returnTex, outTexLocal);
                    return returnTex;
                }
                finally { try { paramBuf?.Dispose(); } catch { } }
            }
            catch { return null; }
        }

        /// <summary>
        /// Run color-adjust shader using an existing GPU source texture and return the resulting GPU texture.
        /// Caller owns and must dispose the returned texture.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeColorAdjustFromTexture(ID3D11Texture2D srcTex,
            float brightness, float contrast, float saturation, float hueShiftDegrees, float gamma,
            float colorTemp, float tintR, float tintG, float tintB,
            float shadowClip, float highlightClip, int paletteSteps, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null || srcTex == null) return null;

            try
            {
                var desc = srcTex.Description;
                var width = desc.Width;
                var height = desc.Height;

                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.
                var cs = s_instance.GetOrCreateShader("CS_ColorAdjustMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);
                using var outTexLocal = s_instance._device.CreateTexture2D(texDesc);
                using var outUav = s_instance._device.CreateUnorderedAccessView(outTexLocal);

                // HLSL layout: floats..., PaletteSteps(float), Invert(int), Width(int), Height(int)
                ID3D11Buffer paramBuf = null!;
                paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { brightness, contrast, saturation, hueShiftDegrees, gamma, colorTemp, tintR, tintG, tintB, shadowClip, highlightClip, (float)paletteSteps, invert ? 1 : 0, width, height, 0 });

                try
                {
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                    s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { outUav }, new int[] { -1 });

                    var tgX = (width + 7) / 8; var tgY = (height + 7) / 8; s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                    s_instance._context.CSSetShader(null);

                    var returnTex = s_instance._device.CreateTexture2D(texDesc);
                    s_instance._context.CopyResource(returnTex, outTexLocal);
                    return returnTex;
                }
                finally { try { paramBuf?.Dispose(); } catch { } }
            }
            catch { return null; }
        }

        /// <summary>
        /// GPU-accelerated post-process (brightness/contrast/threshold/invert) applied in-place.
        /// Returns true on success. Caller should execute under <see cref="GpuScheduler"/>.
        /// </summary>
        internal static bool PostProcessInPlace(PixelBuffer buffer,
            float brightness, float contrast, float threshLow, float threshHigh, bool invert, bool colorOutput)
        {
            EnsureInitialized();
            if (s_instance == null || buffer == null) return false;
            try
            {
                var tex = UploadPixelBufferToTexture(buffer);
                if (tex == null) return false;
                try
                {
                    var ok = DispatchInPlace("CS_PostProcessMain", tex,
                        new object[] { brightness, contrast, threshLow, threshHigh, invert ? 1 : 0, colorOutput ? 1 : 0, buffer.Width, buffer.Height });
                    if (!ok) return false;

                    var result = ReadTextureToPixelBuffer(tex);
                    if (result == null) return false;

                    result.AsSpan().CopyTo(buffer.AsSpan());
                    return true;
                }
                finally { try { tex.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.PostProcessInPlace 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// GPU-accelerated seamless blend. Returns result texture (caller owns it).
        /// Caller should execute under <see cref="GpuScheduler"/>.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeSeamlessBlendToTexture(PixelBuffer input,
            float blendWidth, int blendShape, int blendDirection, float blendStrength, bool showSeam)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;
            try
            {
                var srcTex = UploadPixelBufferToTexture(input);
                if (srcTex == null) return null;
                try
                {
                    int w = input.Width;
                    int h = input.Height;
                    var result = DispatchImageFilter("CS_SeamlessBlendMain", srcTex,
                        new object[] { blendWidth, blendShape, blendDirection, blendStrength, showSeam ? 1 : 0, w, h, 0 });
                    return result;
                }
                finally { try { srcTex.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.RasterizeSeamlessBlendToTexture 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// GPU-accelerated distortion. Returns result texture (caller owns it).
        /// Caller should execute under <see cref="GpuScheduler"/>.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeDistortToTexture(PixelBuffer input,
            int seed, int distortType, float strength, float frequency, int octaves,
            float xStrength, float yStrength, float angle, float centerX, float centerY)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;
            try
            {
                var srcTex = UploadPixelBufferToTexture(input);
                if (srcTex == null) return null;
                try
                {
                    int w = input.Width;
                    int h = input.Height;
                    var result = DispatchImageFilter("CS_DistortMain", srcTex,
                        new object[] { w, h, distortType, strength, frequency, octaves, xStrength, yStrength, angle, centerX, centerY, seed });
                    return result;
                }
                finally { try { srcTex.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.RasterizeDistortToTexture 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// GPU-accelerated pixelation. Returns result texture (caller owns it).
        /// Caller should execute under <see cref="GpuScheduler"/>.
        /// </summary>
        internal static ID3D11Texture2D? RasterizePixelateToTexture(PixelBuffer input,
            int blockSize, int sampleMode, int paletteSteps, int ditherMode)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;
            try
            {
                var srcTex = UploadPixelBufferToTexture(input);
                if (srcTex == null) return null;
                try
                {
                    int w = input.Width;
                    int h = input.Height;
                    var result = DispatchImageFilter("CS_PixelateMain", srcTex,
                        new object[] { blockSize, sampleMode, paletteSteps, ditherMode, w, h, 0, 0 });
                    return result;
                }
                finally { try { srcTex.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.RasterizePixelateToTexture 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Rasterize procedural noise into a GPU texture and return it (caller owns the texture).
        /// </summary>
        internal static ID3D11Texture2D? RasterizeNoiseToTexture(int size, int seed, float scale, int octaves, float persistence, float lacunarity, int noiseType, float brightness, float contrast, float offsetX, float offsetY, float threshLow, float threshHigh, bool invert, bool colorOutput)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            // Clamp inputs to avoid extremely heavy GPU workloads that trigger TDR (GPU timeout).
            // Large 'octaves' or huge 'scale' can make the shader do excessive work per-pixel.
            octaves = Math.Clamp(octaves, 1, 8);
            scale = Math.Clamp(scale, 0.01f, 512f);
            persistence = Math.Clamp(persistence, 0.01f, 1.0f);
            lacunarity = Math.Clamp(lacunarity, 1.0f, 4.0f);
            noiseType = Math.Clamp(noiseType, 0, 4);

            try
            {
                var cs = s_instance.GetOrCreateShader("CS_NoiseMain");
                if (cs == null) return null;

                // Use 32-bit float texture for UAV-compatible output. Some drivers
                // do not support UAVs on BGRA formats; using R32G32B32A32_Float
                // ensures unordered access views can be created reliably. The
                // returned float texture is copied to a staging float texture and
                // read back to a PixelBuffer.
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = CreateUnorderedAccessViewWithRecovery(gpuTex);
                    if (uav == null) throw new InvalidOperationException("CreateUnorderedAccessView failed");

                    // Tile the dispatch to avoid long-running single dispatches that can trigger TDR
                    const int tileSize = 256; // experimental safe tile size
                    for (int ty = 0; ty < size; ty += tileSize)
                    {
                        for (int tx = 0; tx < size; tx += tileSize)
                        {
                            int curW = Math.Min(tileSize, size - tx);
                            int curH = Math.Min(tileSize, size - ty);

                            // Build a 16-slot constant buffer where integer and float fields must
                            // be represented with their correct 32-bit bit patterns. HLSL reads
                            // some slots as ints and others as floats; pack into an int[] so
                            // we can store both integer values and float bits correctly.
                            var raw = new int[16];
                            raw[0] = seed; // int
                            raw[1] = BitConverter.SingleToInt32Bits(scale);
                            raw[2] = octaves; // int
                            raw[3] = BitConverter.SingleToInt32Bits(persistence);
                            raw[4] = BitConverter.SingleToInt32Bits(lacunarity);
                            raw[5] = noiseType; // int
                            raw[6] = BitConverter.SingleToInt32Bits(brightness);
                            raw[7] = BitConverter.SingleToInt32Bits(contrast);
                            raw[8] = BitConverter.SingleToInt32Bits(offsetX);
                            raw[9] = BitConverter.SingleToInt32Bits(offsetY);
                            raw[10] = BitConverter.SingleToInt32Bits(threshLow);
                            raw[11] = BitConverter.SingleToInt32Bits(threshHigh);
                            raw[12] = invert ? 1 : 0; // int
                            raw[13] = colorOutput ? 1 : 0; // int
                            raw[14] = tx; // TileOffsetX (int pixels)
                            raw[15] = ty; // TileOffsetY (int pixels)

                            // Update dynamic constant buffer once per tile and reuse it to avoid creating many small buffers
                            var tileBufData = raw; // int[16]
                            if (!s_instance.UpdateDynamicTileConstantBuffer(tileBufData))
                            {
                                throw new InvalidOperationException("Failed to update dynamic tile constant buffer");
                            }

                            s_instance._context.CSSetShader(cs);
                            s_instance._context.CSSetConstantBuffers(0, 1, new[] { s_instance._dynamicTileConstantBuffer });
                            s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                            var tgX = (curW + 7) / 8;
                            var tgY = (curH + 7) / 8;
                            s_instance._context.Dispatch(tgX, tgY, 1);

                            // Unbind per-tile
                            s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                            s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });
                            s_instance._context.CSSetShader(null);
                        }
                    }

                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterizes a checkerboard pattern on the GPU and returns the result as a <see cref="PixelBuffer"/>.
        /// Pixel-center sampling ensures output is identical to the CPU path.
        /// </summary>
        public static PixelBuffer? RasterizeCheckerboard(int size, int cells, float aR, float aG, float aB, float bR, float bG, float bB, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                var cs = s_instance.GetOrCreateShader("CS_CheckerboardMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex)
                    ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // cbuffer layout: int Cells, float AR/AG/AB, float BR/BG/BB, int Invert
                using var constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(
                    s_instance._device,
                    new object[] { cells, aR, aG, aB, bR, bG, bB, invert ? 1 : 0 });

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 7) / 8;
                var tgY = (size + 7) / 8;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);
                s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, gpuTex);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch;
                    if ((int)(rowPitch / sizeof(float)) == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIdx = 0;
                        for (int row = 0; row < size; row++)
                        {
                            Marshal.Copy(IntPtr.Add(mapped.DataPointer, row * rowPitch), temp, destIdx, size * 4);
                            destIdx += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterize a gradient into a GPU texture and return it (caller owns the texture).
        /// </summary>
        internal static ID3D11Texture2D? RasterizeGradientToTexture(int size, int mode, float r0, float g0, float b0, float r1, float g1, float b1, int repeat, float offset, float midpoint, float rotation, bool tiling, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.
                var cs = s_instance.GetOrCreateShader("CS_GradientMain");
                if (cs == null) return null;

                // Use BGRA UNorm for returned textures so they can be presented directly.
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = CreateUnorderedAccessViewWithRecovery(gpuTex);
                    if (uav == null) throw new InvalidOperationException("CreateUnorderedAccessView failed");

                    // Pack params: Mode/Repeat/Tiling/Invert are ints; colors and offsets are floats.
                    constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { mode, repeat, tiling ? 1 : 0, invert ? 1 : 0, r0, g0, b0, r1, g1, b1, offset, midpoint, rotation, 0, 0, 0 });

                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                    var tgX = (size + 7) / 8;
                    var tgY = (size + 7) / 8;
                    s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns a preferred D3D9 adapter ordinal that best matches the adapter used by the D3D11 device.
        /// Falls back to 0 if no match is found or if initialization hasn't provided a cached description.
        /// </summary>
        public static int GetPreferredAdapterOrdinal()
        {
            try
            {
                EnsureInitialized();
                if (string.IsNullOrEmpty(s_cachedAdapterDescription)) return 0;

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (int ai = 0; ; ai++)
                {
                    var enumRes = factory.EnumAdapters1(ai, out IDXGIAdapter1 adapter);
                    if (enumRes.Failure || adapter == null) break;
                    try
                    {
                        var d = adapter.Description1;
                        var desc = d.Description?.Trim() ?? string.Empty;
                        if (!string.IsNullOrEmpty(desc) && desc.IndexOf(s_cachedAdapterDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return ai;
                        }
                    }
                    finally { adapter.Dispose(); }
                }
            }
            catch { }
            return 0;
        }

        private static GpuCompute? s_instance;
        private static readonly object s_lock = new();
        private static string? s_lastInitError;

        // Packed constant-buffer helper moved to GpuBufferHelpers.CreatePackedConstantBuffer
        // Cached adapter info captured at initialization to avoid enumeration inconsistencies
        private static string? s_cachedAdapterDescription;
        private static string? s_cachedFeatureLevel;
        private static string? s_cachedMemoryString;

        public static string? GetLastInitializationError() => s_lastInitError;

        // Simple adapter info returned to the UI
        public sealed record AdapterInfo(string Description, string FeatureLevel, string DedicatedVideoMemory);

        // Benchmark result for advanced tests
        public sealed record BenchmarkResult(double ElapsedMs, double PixelsPerSecond, long Checksum, double PixelsProcessed);

        /// <summary>
        /// Returns human-readable adapter/device information. EnsureInitialized is called internally.
        /// </summary>
        public static AdapterInfo GetAdapterInfo()
        {
            EnsureInitialized();
            if (s_instance == null)
            {
                return new AdapterInfo("Unavailable", "-", "-");
            }

            // Debug: expose cached values to help diagnose initialization/order issues
            Debug.WriteLine($"GetAdapterInfo: 缓存的适配器描述={s_cachedAdapterDescription}");
            Debug.WriteLine($"GetAdapterInfo: 缓存的显存信息={s_cachedMemoryString}");
            Debug.WriteLine($"GetAdapterInfo: 缓存的功能级别={s_cachedFeatureLevel}");

            // If we captured adapter info during initialization, return that to avoid
            // re-enumeration differences (common on hybrid/driver edge cases).
            // Only trust the cached values if we have both description and memory populated.
            // (Previously we returned when only memory was set which could be "shared memory" prematurely.)
            if (!string.IsNullOrEmpty(s_cachedAdapterDescription) && !string.IsNullOrEmpty(s_cachedMemoryString))
            {
                return new AdapterInfo(s_cachedAdapterDescription ?? "Unknown adapter", s_cachedFeatureLevel ?? "-", s_cachedMemoryString ?? "-");
            }

            try
            {
                // Feature level
                var feat = s_instance._device.FeatureLevel;
                var featStr = feat.ToString();

                // Try to get adapter via the created D3D11 device (preferred) and fall back to enumerating adapters.
                string desc = "Unknown adapter";
                string memStr = "-";
                try
                {
                    // Enumerate adapters and pick the one with the largest dedicated memory. If none report
                    // dedicated memory, fall back to the first non-empty adapter description and mark as shared.
                    using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    ulong bestMem = 0UL;
                    string firstDesc = string.Empty;
                    for (int ai = 0; ; ai++)
                    {
                        var enumRes = factory.EnumAdapters1(ai, out IDXGIAdapter1 adapter);
                        if (enumRes.Failure || adapter == null) break;
                        try
                        {
                            var d = adapter.Description1;
                            var mem = 0UL;
                            try { mem = Convert.ToUInt64(d.DedicatedVideoMemory); } catch { mem = 0UL; }
                            var thisDesc = d.Description.Trim();
                            if (string.IsNullOrEmpty(firstDesc) && !string.IsNullOrEmpty(thisDesc) && thisDesc != "Unknown adapter")
                                firstDesc = thisDesc;

                            if (mem > bestMem)
                            {
                                bestMem = mem;
                                desc = thisDesc;
                                if (mem >= 1024UL * 1024UL * 1024UL)
                                    memStr = $"{mem / (1024UL * 1024UL * 1024UL):N0} GB";
                                else
                                    memStr = $"{mem / (1024UL * 1024UL):N0} MB";
                            }
                        }
                        finally { adapter.Dispose(); }
                    }

                    if (bestMem == 0UL && !string.IsNullOrEmpty(firstDesc))
                    {
                        desc = firstDesc;
                        memStr = "shared memory";
                    }
                }
                catch { }

                return new AdapterInfo(desc, featStr, memStr);
            }
            catch
            {
                return new AdapterInfo("Unknown", "-", "-");
            }
        }

        /// <summary>
        /// Rasterizes tileable procedural noise on the GPU.
        /// Supports a simple set of noise types via a compact HLSL implementation.
        /// </summary>
        public static PixelBuffer? RasterizeNoise(int size, int seed, float scale, int octaves, float persistence, float lacunarity, int noiseType, float brightness, float contrast, float offsetX, float offsetY, float threshLow, float threshHigh, bool invert, bool colorOutput)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            // Clamp inputs to avoid excessive per-pixel work that can cause GPU timeout (TDR).
            octaves = Math.Clamp(octaves, 1, 8);
            scale = Math.Clamp(scale, 0.01f, 512f);
            persistence = Math.Clamp(persistence, 0.01f, 1.0f);
            lacunarity = Math.Clamp(lacunarity, 1.0f, 4.0f);
            noiseType = Math.Clamp(noiseType, 0, 4);

            try
            {
                // Shader source consolidated into Core/Gpu/Shaders.hlsl; compile via GetOrCreateShader at runtime.
                var cs = s_instance.GetOrCreateShader("CS_NoiseMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    // Use float RGBA for UAV output to ensure UAV support and driver compatibility.
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // Dispatch in tiles to keep individual dispatch durations short and avoid TDR.
                const int tileSize = 256;
                for (int ty = 0; ty < size; ty += tileSize)
                {
                    for (int tx = 0; tx < size; tx += tileSize)
                    {
                        int curW = Math.Min(tileSize, size - tx);
                        int curH = Math.Min(tileSize, size - ty);

                        // Pack integer and float slots into a single int[] so HLSL receives
                        // proper 32-bit representations for ints and floats.
                        var raw = new int[16];
                        raw[0] = seed; // int
                        raw[1] = BitConverter.SingleToInt32Bits(scale);
                        raw[2] = octaves; // int
                        raw[3] = BitConverter.SingleToInt32Bits(persistence);
                        raw[4] = BitConverter.SingleToInt32Bits(lacunarity);
                        raw[5] = noiseType; // int
                        raw[6] = BitConverter.SingleToInt32Bits(brightness);
                        raw[7] = BitConverter.SingleToInt32Bits(contrast);
                        raw[8] = BitConverter.SingleToInt32Bits(offsetX);
                        raw[9] = BitConverter.SingleToInt32Bits(offsetY);
                        raw[10] = BitConverter.SingleToInt32Bits(threshLow);
                        raw[11] = BitConverter.SingleToInt32Bits(threshHigh);
                        raw[12] = invert ? 1 : 0; // int
                        raw[13] = colorOutput ? 1 : 0; // int
                        raw[14] = tx; // TileOffsetX
                        raw[15] = ty; // TileOffsetY

                        var h = GCHandle.Alloc(raw, GCHandleType.Pinned);
                        ID3D11Buffer tileBuf = null!;
                        try
                        {
                            var cbd = new BufferDescription
                            {
                                Usage = ResourceUsage.Default,
                                SizeInBytes = sizeof(int) * raw.Length,
                                BindFlags = BindFlags.ConstantBuffer,
                                CpuAccessFlags = CpuAccessFlags.None,
                                OptionFlags = ResourceOptionFlags.None,
                                StructureByteStride = 0
                            };
                            var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                            tileBuf = s_instance._device.CreateBuffer(cbd, init);

                            s_instance._context.CSSetShader(cs);
                            s_instance._context.CSSetConstantBuffers(0, 1, new[] { tileBuf });
                            s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                            var tgX = (curW + 7) / 8;
                            var tgY = (curH + 7) / 8;
                            s_instance._context.Dispatch(tgX, tgY, 1);

                            s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                            s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });
                            s_instance._context.CSSetShader(null);
                        }
                        finally
                        {
                            try { tileBuf?.Dispose(); } catch { }
                            h.Free();
                        }
                    }
                }

                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, gpuTex);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch;
                    var rowFloats = (int)(rowPitch / sizeof(float));
                    if (rowFloats == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIndex = 0;
                        for (int y = 0; y < size; y++)
                        {
                            var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            Marshal.Copy(src, temp, destIndex, size * 4);
                            destIndex += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        // Use shared GpuCompiler for shader compilation (P/Invoke and blob handling).

        public static bool IsSupported
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return s_instance != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (s_instance != null) return;
            lock (s_lock)
            {
                if (s_instance != null) return;

                // Try to preload consolidated HLSL source from embedded resource or deployed file
                if (string.IsNullOrEmpty(s_consolidatedShaderSource))
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.GetExecutingAssembly();
                        var resourceName = $"{typeof(GpuCompute).Namespace}.Shaders.hlsl";
                        using var rs = asm.GetManifestResourceStream(resourceName);
                        if (rs != null)
                        {
                            using var sr = new System.IO.StreamReader(rs);
                            s_consolidatedShaderSource = sr.ReadToEnd();
                        }
                        else
                        {
                            // fallback: try deployed location
                            try
                            {
                                var p = System.IO.Path.Combine(AppContext.BaseDirectory, "Core", "Gpu", "Shaders.hlsl");
                                if (System.IO.File.Exists(p))
                                {
                                    s_consolidatedShaderSource = System.IO.File.ReadAllText(p);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Create device with BGRA support so interop with WPF is possible.
                var creationFlags = DeviceCreationFlags.BgraSupport;
                ID3D11Device? device = null;
                ID3D11DeviceContext? context = null;
                FeatureLevel[] featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

                // Prefer creating the device on the adapter with the largest dedicated video memory (discrete GPU)
                // This helps ensure the app actually uses the discrete GPU on hybrid systems.
                try
                {
                    using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    IDXGIAdapter1? bestAdapter = null;
                    ulong bestMem = 0;
                    string? bestAdapterDesc = null;
                    for (int ai = 0; ; ai++)
                    {
                        var enumRes = factory.EnumAdapters1(ai, out IDXGIAdapter1 adapter);
                        if (enumRes.Failure || adapter == null) break;
                        try
                        {
                            var d = adapter.Description1;
                            Debug.WriteLine($"GpuCompute: 已枚举适配器 #{ai}: 描述='{d.Description}', VendorId={d.VendorId}, DeviceId={d.DeviceId}, 专用显存={d.DedicatedVideoMemory}");
                            try
                            {
                                ulong mem = 0UL;
                                try
                                {
                                    // DedicatedVideoMemory may be a SharpGen PointerSize; use its string form and parse to ulong.
                                    var memRaw = d.DedicatedVideoMemory.ToString();
                                    if (!ulong.TryParse(memRaw, out mem)) mem = 0UL;
                                }
                                catch { mem = 0UL; }
                                var thisDesc = d.Description.Trim();
                                if (mem > bestMem)
                                {
                                    bestMem = mem;
                                    // keep a reference to the adapter
                                    bestAdapter?.Dispose();
                                    bestAdapter = adapter;
                                    bestAdapterDesc = thisDesc;
                                    adapter = null!; // prevent disposing below
                                }
                            }
                            catch { }
                        }
                        finally
                        {
                            if (adapter != null) adapter.Dispose();
                        }
                    }

                    if (bestAdapter != null)
                    {
                        try
                        {
                            var tryHr = D3D11CreateDevice(bestAdapter, DriverType.Unknown, creationFlags, featureLevels, out device, out context);
                            Debug.WriteLine($"GpuCompute: D3D11CreateDevice(bestAdapter) 结果={tryHr}, device={(device!=null)}, context={(context!=null)}");
                            // If creation failed and the debug flag is set, retry without Debug. Some drivers/runtimes
                            // cannot create a device on a specific adapter when the Debug layer flag is present.
                            if (tryHr.Failure && (creationFlags & DeviceCreationFlags.Debug) != 0)
                            {
                                var altFlags = creationFlags & ~DeviceCreationFlags.Debug;
                                try
                                {
                                    var tryHr2 = D3D11CreateDevice(bestAdapter, DriverType.Unknown, altFlags, featureLevels, out device, out context);
                                    Debug.WriteLine($"GpuCompute: D3D11CreateDevice(bestAdapter) 去除 Debug 重试结果={tryHr2}, device={(device!=null)}, context={(context!=null)}");
                                    if (!tryHr2.Failure && device != null && context != null)
                                    {
                                        tryHr = tryHr2;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("GpuCompute: 在 bestAdapter 上去除 Debug 重试创建设备失败: " + ex.Message);
                                }
                            }

                            if (!tryHr.Failure && device != null && context != null)
                            {
                                // success using preferred adapter — cache description/memory so UI shows correct values
                                try
                                {
                                    s_cachedAdapterDescription = bestAdapterDesc ?? "Unknown adapter";
                                    if (bestMem >= 1024UL * 1024UL * 1024UL)
                                        s_cachedMemoryString = $"{bestMem / (1024UL * 1024UL * 1024UL):N0} GB";
                                    else if (bestMem > 0)
                                        s_cachedMemoryString = $"{bestMem / (1024UL * 1024UL):N0} MB";
                                    else
                                        s_cachedMemoryString = "shared memory";
                                    Debug.WriteLine($"GpuCompute: 已缓存来自 bestAdapter 的适配器: {s_cachedAdapterDescription}, 显存={s_cachedMemoryString}");
                                }
                                catch { }

                                bestAdapter.Dispose();
                                bestAdapter = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("GpuCompute: 在 bestAdapter 上创建设备失败: " + ex.Message);
                        }
                        finally
                        {
                            try { bestAdapter?.Dispose(); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("GpuCompute: 适配器枚举失败: " + ex.Message);
                }

                // If we didn't get a device from the preferred adapter, fall back to default hardware creation
                if (device == null || context == null)
                {
                    var hr = D3D11CreateDevice(null, DriverType.Hardware, creationFlags, featureLevels, out device, out context);
                    Debug.WriteLine($"GpuCompute: D3D11CreateDevice(null, Hardware) 结果={hr}, device={(device != null)}, context={(context != null)}");

                    if (hr.Failure || device == null || context == null)
                    {
                        Debug.WriteLine("GpuCompute: 使用默认适配器创建硬件设备失败，正在逐一枚举所有适配器");
                        // Try enumerating adapters and create device on each explicitly.
                        try
                        {
                            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                            for (int ai = 0; ; ai++)
                            {
                                var enumRes = factory.EnumAdapters1(ai, out IDXGIAdapter1 adapter);
                                if (enumRes.Failure || adapter == null) break;

                                try
                                {
                                    var desc = adapter.Description1;
                                    Debug.WriteLine($"GpuCompute: 正在尝试适配器 #{ai}: 描述='{desc.Description}', VendorId={desc.VendorId}, DeviceId={desc.DeviceId}, 专用显存={desc.DedicatedVideoMemory}");
                                    var tryHr = D3D11CreateDevice(adapter, DriverType.Unknown, creationFlags, featureLevels, out device, out context);
                                    Debug.WriteLine($"GpuCompute: D3D11CreateDevice(适配器 #{ai}) 结果={tryHr}, device={(device!=null)}, context={(context!=null)}");
                                    if (tryHr.Failure && (creationFlags & DeviceCreationFlags.Debug) != 0)
                                    {
                                        var altFlags = creationFlags & ~DeviceCreationFlags.Debug;
                                        try
                                        {
                                            var tryHr2 = D3D11CreateDevice(adapter, DriverType.Unknown, altFlags, featureLevels, out device, out context);
                                            Debug.WriteLine($"GpuCompute: D3D11CreateDevice(适配器 #{ai}) 去除 Debug 重试结果={tryHr2}, device={(device!=null)}, context={(context!=null)}");
                                            if (!tryHr2.Failure && device != null && context != null)
                                            {
                                                tryHr = tryHr2;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine("GpuCompute: 在适配器上重试创建设备失败: " + ex.Message);
                                        }
                                    }

                                    if (!tryHr.Failure && device != null && context != null)
                                    {
                                        hr = tryHr;
                                        break;
                                    }
                                }
                                finally
                                {
                                    adapter.Dispose();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("GpuCompute: 适配器枚举失败: " + ex.Message);
                        }

                        if (hr.Failure || device == null || context == null)
                        {
                            Debug.WriteLine("GpuCompute: 适配器枚举未能产生可用设备，正在尝试 WARP 回退");
                            // try WARP fallback
                            hr = D3D11CreateDevice(null, DriverType.Warp, creationFlags, featureLevels, out device, out context);
                            Debug.WriteLine($"GpuCompute: D3D11CreateDevice(null, WARP) 结果={hr}, device={(device != null)}, context={(context != null)}");
                            if (hr.Failure || device == null || context == null)
                            {
                                Debug.WriteLine("GpuCompute: 设备创建失败（已尝试所有适配器及 WARP）");
                                s_lastInitError = "D3D11CreateDevice failed for Hardware (default + enumerated adapters) and WARP. See Debug output for details.";
                                return;
                            }
                        }
                    }
                }

                // Shape rasterization HLSL moved to Core/Gpu/Shaders.hlsl and compiled on-demand via GetOrCreateShader.

                // Do not compile shaders at initialization to avoid blocking the UI on first use.
                // Shader compilation will be performed on-demand and cached by GetOrCreateShader.

                // Cache adapter/feature info from the created device to avoid later
                // enumeration inconsistencies (hybrid GPUs, driver variations).
                try
                {
                    s_cachedFeatureLevel = device.FeatureLevel.ToString();
                    // Fallback: enumerate adapters via DXGI factory and pick the one with the largest
                    // dedicated video memory (same logic as elsewhere). This is safer across drivers.
                    try
                    {
                        using var factory2 = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                        ulong bestMem = 0UL;
                        string firstDesc = string.Empty;
                        for (int ai = 0; ; ai++)
                        {
                            var enumRes = factory2.EnumAdapters1(ai, out IDXGIAdapter1 adapter);
                            if (enumRes.Failure || adapter == null) break;
                            try
                            {
                                var d = adapter.Description1;
                                var mem = 0UL;
                            try
                            {
                                var memRaw = d.DedicatedVideoMemory.ToString();
                                if (!ulong.TryParse(memRaw, out mem)) mem = 0UL;
                            }
                            catch { mem = 0UL; }
                                var thisDesc = d.Description.Trim();
                                if (string.IsNullOrEmpty(firstDesc) && !string.IsNullOrEmpty(thisDesc) && thisDesc != "Unknown adapter")
                                    firstDesc = thisDesc;

                                if (mem > bestMem)
                                {
                                    bestMem = mem;
                                    s_cachedAdapterDescription = thisDesc;
                                    if (mem >= 1024UL * 1024UL * 1024UL)
                                        s_cachedMemoryString = $"{mem / (1024UL * 1024UL * 1024UL):N0} GB";
                                    else
                                        s_cachedMemoryString = $"{mem / (1024UL * 1024UL):N0} MB";
                                }
                            }
                            finally { adapter.Dispose(); }
                        }

                        if (bestMem == 0UL && !string.IsNullOrEmpty(firstDesc))
                        {
                            s_cachedAdapterDescription = firstDesc;
                            s_cachedMemoryString = "shared memory";
                        }
                        Debug.WriteLine($"GpuCompute: 已缓存来自枚举的适配器: {s_cachedAdapterDescription}, 显存={s_cachedMemoryString}");
                    }
                    catch { }
                }
                catch { }

                s_instance = new GpuCompute(device, context, null);
            }
        }

        private PixelBuffer? RasterizeBatchImpl(int size, ShapeParams[] shapes)
        {
            ID3D11Buffer sbuf = null!;
            ID3D11Buffer constBuf = null!;
            try
            {
                if (shapes == null || shapes.Length == 0) return null;

                // Create GPU texture (R32G32B32A32_Float) as accumulator
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    // Mark shared so interop path can be implemented later
                    OptionFlags = ResourceOptionFlags.Shared
                };
                using var gpuTex = _device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // Create structured buffer with shape data
                var count = shapes.Length;
                var stride = sizeof(float) * 17; // fields in ShapeParams packed as floats (extended with gapDepth, edgeRoughness, wear, seed)
                var bufDesc = new BufferDescription
                {
                    Usage = ResourceUsage.Default,
                    SizeInBytes = stride * count,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.BufferStructured,
                    StructureByteStride = stride
                };

                // Flatten shapes into float array
                var flat = new float[count * 17];
                for (int i = 0; i < count; i++)
                {
                    var s = shapes[i];
                    var baseIdx = i * 17;
                    flat[baseIdx + 0] = s.ShapeType;
                    flat[baseIdx + 1] = s.SizeParam * (size * 0.5f); // convert normalized radius to pixels like before
                    // center in pixel coordinates
                    flat[baseIdx + 2] = (size * 0.5f) + s.OffsetX * size;
                    flat[baseIdx + 3] = (size * 0.5f) + s.OffsetY * size;
                    flat[baseIdx + 4] = s.Rotation;
                    flat[baseIdx + 5] = s.ScaleX;
                    flat[baseIdx + 6] = s.ScaleY;
                    flat[baseIdx + 7] = s.R;
                    flat[baseIdx + 8] = s.G;
                    flat[baseIdx + 9] = s.B;
                    flat[baseIdx + 10] = 0f; // padding
                    flat[baseIdx + 11] = s.Hardness;
                    flat[baseIdx + 12] = s.Invert ? 1f : 0f;
                    flat[baseIdx + 13] = s.GapDepth;
                    flat[baseIdx + 14] = s.EdgeRoughness;
                    flat[baseIdx + 15] = s.Wear;
                    flat[baseIdx + 16] = s.Seed;
                }

                GCHandle h = GCHandle.Alloc(flat, GCHandleType.Pinned);
                try
                {
                    var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                    sbuf = _device.CreateBuffer(bufDesc, init);
                }
                finally { h.Free(); }

                using var srv = _device.CreateShaderResourceView(sbuf);

                // Constant buffer for shape count
                var cbData = new int[4]; cbData[0] = count;
                GCHandle hcb = GCHandle.Alloc(cbData, GCHandleType.Pinned);
                try
                {
                    var cbd = new BufferDescription
                    {
                        Usage = ResourceUsage.Default,
                        SizeInBytes = sizeof(int) * cbData.Length,
                        BindFlags = BindFlags.ConstantBuffer,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None,
                        StructureByteStride = 0
                    };
                    var init = new SubresourceData(hcb.AddrOfPinnedObject(), 0, 0);
                    constBuf = _device.CreateBuffer(cbd, init);
                }
                finally { hcb.Free(); }

                // Bind resources and dispatch
                var cs = GetOrCreateShader("CS_ShapeMain");
                if (cs == null) throw new InvalidOperationException($"Shape shader unavailable. Shader init error: {GetLastInitializationError()}");
                _context.CSSetShader(cs);
                _context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                _context.CSSetShaderResources(0, 1, new[] { srv });
                _context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 15) / 16;
                var tgY = (size + 15) / 16;
                _context.Dispatch(tgX, tgY, 1);

                // Unbind
                _context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                _context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                _context.CSSetShader(null);
                _context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                // Copy to staging and read back (single readback)
                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = _device.CreateTexture2D(stagingDesc);
                _context.CopyResource(staging, gpuTex);

                var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch; // bytes
                    var rowFloats = (int)(rowPitch / sizeof(float));

                    if (rowFloats == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIndex = 0;
                        for (int y = 0; y < size; y++)
                        {
                            var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            Marshal.Copy(src, temp, destIndex, size * 4);
                            destIndex += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    _context.Unmap(staging, 0);
                    try { sbuf?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                try { sbuf?.Dispose(); } catch { }
                try { constBuf?.Dispose(); } catch { }
                return null;
            }
        }

        public static PixelBuffer? RasterizeShapeSimple(int size, int shapeType, float sizeParam, float scaleX, float scaleY, float rotation, float offsetX, float offsetY, float r, float g, float b, float hardness, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            // Forward to the new batch API for efficiency (single-shape batch)
            // Default extra parameters (gapDepth, edgeRoughness, wear, seed) set to 0
            var sp = new ShapeParams(shapeType, sizeParam, scaleX, scaleY, rotation, offsetX, offsetY, r, g, b, 0f, hardness, invert, 0f, 0f, 0f, 0f);
            return RasterizeBatch(size, new[] { sp });
        }

        // Public helper for callers to describe a shape when batching multiple shapes into a
        // single GPU dispatch. Using a batch avoids per-shape GPU->CPU readbacks which were the
        // primary cause of severe FPS drops when many shapes are produced per frame.
        // Extended shape params: additional floats at end are gapDepth, edgeRoughness, wear, seed
        public readonly record struct ShapeParams(int ShapeType, float SizeParam, float ScaleX, float ScaleY, float Rotation, float OffsetX, float OffsetY, float R, float G, float B, float Padding0, float Hardness, bool Invert, float GapDepth, float EdgeRoughness, float Wear, float Seed);

        public static PixelBuffer? RasterizeBatch(int size, ShapeParams[] shapes)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            return s_instance.RasterizeBatchImpl(size, shapes);
        }

        /// <summary>
        /// Rasterize a batch of shapes on the GPU and return the GPU texture containing the result.
        /// The returned <see cref="ID3D11Texture2D"/> is owned by the caller and must be disposed
        /// when no longer needed. This avoids an immediate GPU->CPU readback and enables true
        /// GPU-native pipelines.
        /// </summary>
        internal static ID3D11Texture2D? RasterizeBatchToTexture(int size, ShapeParams[] shapes)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            try
            {
                if (shapes == null || shapes.Length == 0) return null;

                // Create GPU texture (R32G32B32A32_Float) as accumulator
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    // Not shared by default
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11ShaderResourceView? srv = null;
                ID3D11Buffer? sbuf = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = s_instance._device.CreateUnorderedAccessView(gpuTex);

                    // Create structured buffer with shape data
                    var count = shapes.Length;
                    var stride = sizeof(float) * 17; // fields in ShapeParams packed as floats (extended)
                    var bufDesc = new BufferDescription
                    {
                        Usage = ResourceUsage.Default,
                        SizeInBytes = stride * count,
                        BindFlags = BindFlags.ShaderResource,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.BufferStructured,
                        StructureByteStride = stride
                    };

                    var flat = new float[count * 17];
                    for (int i = 0; i < count; i++)
                    {
                        var s = shapes[i];
                        var baseIdx = i * 17;
                        flat[baseIdx + 0] = s.ShapeType;
                        flat[baseIdx + 1] = s.SizeParam * (size * 0.5f);
                        flat[baseIdx + 2] = (size * 0.5f) + s.OffsetX * size;
                        flat[baseIdx + 3] = (size * 0.5f) + s.OffsetY * size;
                        flat[baseIdx + 4] = s.Rotation;
                        flat[baseIdx + 5] = s.ScaleX;
                        flat[baseIdx + 6] = s.ScaleY;
                        flat[baseIdx + 7] = s.R;
                        flat[baseIdx + 8] = s.G;
                        flat[baseIdx + 9] = s.B;
                        flat[baseIdx + 10] = 0f;
                        flat[baseIdx + 11] = s.Hardness;
                        flat[baseIdx + 12] = s.Invert ? 1f : 0f;
                        flat[baseIdx + 13] = s.GapDepth;
                        flat[baseIdx + 14] = s.EdgeRoughness;
                        flat[baseIdx + 15] = s.Wear;
                        flat[baseIdx + 16] = s.Seed;
                    }

                    var h = System.Runtime.InteropServices.GCHandle.Alloc(flat, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                        sbuf = s_instance._device.CreateBuffer(bufDesc, init);
                    }
                    finally { h.Free(); }

                    srv = s_instance._device.CreateShaderResourceView(sbuf);

                    // Constant buffer for shape count
                    var cbData = new int[4]; cbData[0] = shapes.Length;
                    var hcb = System.Runtime.InteropServices.GCHandle.Alloc(cbData, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        var cbd = new BufferDescription
                        {
                            Usage = ResourceUsage.Default,
                            SizeInBytes = sizeof(int) * cbData.Length,
                            BindFlags = BindFlags.ConstantBuffer,
                            CpuAccessFlags = CpuAccessFlags.None,
                            OptionFlags = ResourceOptionFlags.None,
                            StructureByteStride = 0
                        };
                        var init = new SubresourceData(hcb.AddrOfPinnedObject(), 0, 0);
                        constBuf = s_instance._device.CreateBuffer(cbd, init);
                    }
                    finally { hcb.Free(); }

                    // Bind resources and dispatch (use cached shape shader entry)
                    var cs = s_instance.GetOrCreateShader("CS_ShapeMain");
                    if (cs == null) throw new InvalidOperationException($"Shape shader unavailable. Shader init error: {GetLastInitializationError()}");
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                    var tgX = (size + 15) / 16;
                    var tgY = (size + 15) / 16;
                    s_instance._context.Dispatch(tgX, tgY, 1);

                    // Unbind
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                    // Return the GPU texture (caller owns it)
                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { srv?.Dispose(); } catch { }
                    try { sbuf?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterize a solid color into a GPU texture and return it (caller owns the texture).
        /// </summary>
        internal static ID3D11Texture2D? RasterizeSolidColorToTexture(int size, float r, float g, float b, float a)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                var cs = s_instance.GetOrCreateShader("CS_SolidColorMain");
                if (cs == null) return null;

                // Create BGRA UNorm texture so GPU-native nodes can present directly.
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                ID3D11UnorderedAccessView? uav = null;
                ID3D11Buffer? constBuf = null;
                try
                {
                    uav = s_instance._device.CreateUnorderedAccessView(gpuTex);

                    var cb = new float[4] { r, g, b, a };
                    var h = System.Runtime.InteropServices.GCHandle.Alloc(cb, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        var cbd = new BufferDescription
                        {
                            Usage = ResourceUsage.Default,
                            SizeInBytes = sizeof(float) * cb.Length,
                            BindFlags = BindFlags.ConstantBuffer,
                            CpuAccessFlags = CpuAccessFlags.None,
                            OptionFlags = ResourceOptionFlags.None,
                            StructureByteStride = 0
                        };
                        var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                        constBuf = s_instance._device.CreateBuffer(cbd, init);
                    }
                    finally { h.Free(); }

                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                    var tgX = (size + 15) / 16;
                    var tgY = (size + 15) / 16;
                    s_instance._context.Dispatch(tgX, tgY, 1);

                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                    return gpuTex;
                }
                catch
                {
                    try { gpuTex.Dispose(); } catch { }
                    throw;
                }
                finally
                {
                    try { uav?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        // Ensure reusable render targets and buffers exist for given size.
        public static void EnsureRenderResources(int size)
        {
            EnsureInitialized();
            if (s_instance == null) return;
            s_instance.EnsureRenderResourcesImpl(size);
        }

        // Render shapes into one of the internal render targets (double-buffered) and return a shared handle to the texture.
        // Caller can use this shared handle for interop (D3D9/D3DImage) if supported by the driver.
        public static IntPtr RenderToRenderTarget(int index, int size, ShapeParams[]? shapes)
        {
            EnsureInitialized();
            if (s_instance == null) return IntPtr.Zero;
            try
            {
                shapes ??= Array.Empty<ShapeParams>();
                Debug.WriteLine($"GpuCompute.RenderToRenderTarget: 请求渲染 index={index}, size={size}");
                try { System.Diagnostics.Trace.TraceInformation($"[调试] GpuCompute.RenderToRenderTarget 请求渲染 index={index}, size={size}, shapesCount={shapes.Length}"); } catch { }
                var h = s_instance.RenderToRenderTargetImpl(index, size, shapes);
                Debug.WriteLine($"GpuCompute.RenderToRenderTarget: 返回共享句柄=0x{h.ToString("X")}");
                try { System.Diagnostics.Trace.TraceInformation($"[调试] GpuCompute.RenderToRenderTarget 返回共享句柄=0x{h.ToString("X")}"); } catch { }
                if (h == IntPtr.Zero) 
                {
                    Trace.TraceWarning($"GpuCompute.RenderToRenderTarget: returned zero handle for index={index}, size={size}");
                    try { System.Diagnostics.Trace.TraceWarning($"[调试] GpuCompute.RenderToRenderTarget: 返回空句柄 index={index}, size={size}"); } catch { }
                }
                return h;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GpuCompute.RenderToRenderTarget: exception: {ex}");
                try { System.Diagnostics.Trace.TraceError($"[调试] GpuCompute.RenderToRenderTarget 异常: {ex.Message}"); } catch { }
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Render into the internal BGRA render target and copy the pixels to a CPU-accessible
        /// byte[] in BGRA order. This is a fast fallback for presentation when shared-handle
        /// interop is not available.
        /// </summary>
        public static byte[]? RenderToBgraByteArray(int index, int size, ShapeParams[] shapes)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            try
            {
                // Render into the internal render target (dispatch compute shader)
                try { System.Diagnostics.Trace.TraceInformation($"[调试] GpuCompute.RenderToBgraByteArray 请求渲染 index={index}, size={size}"); } catch { }
                _ = s_instance.RenderToRenderTargetImpl(index, size, shapes);

                // Copy the render target to a staging BGRA texture and map it for CPU read
                var target = s_instance._renderTextures![index];
                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, target);
                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var rowPitch = mapped.RowPitch; // bytes
                    var rowBytes = size * 4;
                    var result = new byte[size * size * 4];
                    for (int y = 0; y < size; y++)
                    {
                        var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                        Marshal.Copy(src, result, y * rowBytes, rowBytes);
                    }
                    try { System.Diagnostics.Trace.TraceInformation($"[调试] GpuCompute.RenderToBgraByteArray: 成功拷贝 {size}x{size} 像素到 CPU 缓冲"); } catch { }
                    return result;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        private void EnsureRenderResourcesImpl(int size)
        {
            // create arrays
            _renderTextures ??= new ID3D11Texture2D[2];
            _renderUavs ??= new ID3D11UnorderedAccessView[2];
            _renderSrvs ??= new ID3D11ShaderResourceView[2];

            for (int i = 0; i < 2; i++)
            {
                var tex = _renderTextures[i];
                bool recreate = false;
                if (tex == null) recreate = true;
                else
                {
                    var desc = tex.Description;
                    if (desc.Width != size || desc.Height != size) recreate = true;
                }

                if (recreate)
                {
                    try { _renderUavs[i]?.Dispose(); } catch { }
                    try { _renderSrvs[i]?.Dispose(); } catch { }
                    try { _renderTextures[i]?.Dispose(); } catch { }

                    var texDesc = new Texture2DDescription
                    {
                        Width = size,
                        Height = size,
                        MipLevels = 1,
                        ArraySize = 1,
                        // Use BGRA UNORM for shared render targets so interop with D3D9 (WPF D3DImage)
                        // is possible. D3D9 expects A8R8G8B8 layout which maps to DXGI_FORMAT_B8G8R8A8_UNORM.
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.Shared
                    };

                    var newTex = _device.CreateTexture2D(texDesc);
                    _renderTextures[i] = newTex;
                    _renderUavs[i] = CreateUnorderedAccessViewWithRecovery(newTex)!;
                    if (_renderUavs[i] == null)
                    {
                        // If UAV creation failed even after recovery, dispose texture and continue
                        try { newTex.Dispose(); } catch { }
                        _renderTextures[i] = null!;
                        _renderUavs[i] = null!;
                        _renderSrvs[i] = null!;
                        continue;
                    }
                    _renderSrvs[i] = _device.CreateShaderResourceView(newTex);
                    // Attempt to cache the shared handle for this render texture so callers
                    // can present without querying the DXGI resource every frame.
                    try
                    {
                        _renderSharedHandles ??= new IntPtr[2];
                        _renderSharedHandles[i] = GetSharedHandleForTexture(newTex);
                    }
                    catch { _renderSharedHandles ??= new IntPtr[2]; }
                }
            }
        }

        private IntPtr GetSharedHandleForTexture(ID3D11Texture2D tex)
        {
            if (tex == null) return IntPtr.Zero;
            try
            {
                var dxgiRes = tex.QueryInterfaceOrNull<IDXGIResource>();
                if (dxgiRes == null) return IntPtr.Zero;
                try
                {
                    // Try property first
                    try
                    {
                        var prop = dxgiRes.GetType().GetProperty("SharedHandle");
                        if (prop != null)
                        {
                            var val = prop.GetValue(dxgiRes);
                            if (val is IntPtr h && h != IntPtr.Zero) return h;
                        }
                    }
                    catch { }

                    // Try method overload(s)
                    try
                    {
                        var method = dxgiRes.GetType().GetMethod("GetSharedHandle");
                        if (method != null)
                        {
                            var parms = method.GetParameters();
                            if (parms.Length == 1)
                            {
                                var args = new object[] { IntPtr.Zero };
                                method.Invoke(dxgiRes, args);
                                if (args[0] is IntPtr h2 && h2 != IntPtr.Zero) return h2;
                            }
                            else if (method.ReturnType == typeof(IntPtr))
                            {
                                var ret = method.Invoke(dxgiRes, null);
                                if (ret is IntPtr h3 && h3 != IntPtr.Zero) return h3;
                            }
                        }
                    }
                    catch { }
                }
                finally { try { dxgiRes.Dispose(); } catch { } }
            }
            catch { }
            return IntPtr.Zero;
        }

        private IntPtr RenderToRenderTargetImpl(int index, int size, ShapeParams[] shapes)
        {
            ID3D11Buffer sbuf = null!;
            ID3D11Buffer constBuf = null!;
            try
            {
                if (index < 0 || index > 1) index = 0;
                EnsureRenderResourcesImpl(size);
                var target = _renderTextures![index];
                var uav = _renderUavs![index];

                try
                {
                    var ddesc = target.Description;
                    Debug.WriteLine($"GpuCompute.RenderToRenderTargetImpl: index={index}, size={size}, 目标纹理格式={ddesc.Format}, 用途={ddesc.Usage}");
                }
                catch { }

                // Prepare structured buffer like RasterizeBatchImpl (17 floats per shape)
                var count = shapes.Length;
                var stride = sizeof(float) * 17;
                var bufDesc = new BufferDescription
                {
                    Usage = ResourceUsage.Default,
                    SizeInBytes = stride * count,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.BufferStructured,
                    StructureByteStride = stride
                };

                var flat = new float[count * 17];
                for (int i = 0; i < count; i++)
                {
                    var s = shapes[i];
                    var baseIdx = i * 17;
                    flat[baseIdx + 0] = s.ShapeType;
                    flat[baseIdx + 1] = s.SizeParam * (size * 0.5f);
                    flat[baseIdx + 2] = (size * 0.5f) + s.OffsetX * size;
                    flat[baseIdx + 3] = (size * 0.5f) + s.OffsetY * size;
                    flat[baseIdx + 4] = s.Rotation;
                    flat[baseIdx + 5] = s.ScaleX;
                    flat[baseIdx + 6] = s.ScaleY;
                    flat[baseIdx + 7] = s.R;
                    flat[baseIdx + 8] = s.G;
                    flat[baseIdx + 9] = s.B;
                    flat[baseIdx + 10] = 0f;
                    flat[baseIdx + 11] = s.Hardness;
                    flat[baseIdx + 12] = s.Invert ? 1f : 0f;
                    flat[baseIdx + 13] = s.GapDepth;
                    flat[baseIdx + 14] = s.EdgeRoughness;
                    flat[baseIdx + 15] = s.Wear;
                    flat[baseIdx + 16] = s.Seed;
                }

                GCHandle h = GCHandle.Alloc(flat, GCHandleType.Pinned);
                try
                {
                    var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                    sbuf = _device.CreateBuffer(bufDesc, init);
                }
                finally { h.Free(); }

                using var srv = _device.CreateShaderResourceView(sbuf);

                // Constant buffer for shape count
                var cbData = new int[4]; cbData[0] = count;
                GCHandle hcb = GCHandle.Alloc(cbData, GCHandleType.Pinned);
                try
                {
                    var cbd = new BufferDescription
                    {
                        Usage = ResourceUsage.Default,
                        SizeInBytes = sizeof(int) * cbData.Length,
                        BindFlags = BindFlags.ConstantBuffer,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None,
                        StructureByteStride = 0
                    };
                    var init = new SubresourceData(hcb.AddrOfPinnedObject(), 0, 0);
                    constBuf = _device.CreateBuffer(cbd, init);
                }
                finally { hcb.Free(); }

                // Bind and dispatch
                var cs = GetOrCreateShader("CS_ShapeMain");
                if (cs == null) throw new InvalidOperationException("Shape shader unavailable");
                _context.CSSetShader(cs);
                _context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                _context.CSSetShaderResources(0, 1, new[] { srv });
                _context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 15) / 16;
                var tgY = (size + 15) / 16;
                _context.Dispatch(tgX, tgY, 1);

                // Unbind
                _context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                _context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                _context.CSSetShader(null);
                _context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                // Dispose GPU buffers after dispatch completes; they are no longer needed by the pipeline.
                try
                {
                    // If we cached shared handles during resource creation, return it directly which
                    // avoids expensive per-frame DXGI queries.
                    try
                    {
                        try { _context.Flush(); } catch { }
                        if (_renderSharedHandles != null && _renderSharedHandles.Length > index && _renderSharedHandles[index] != IntPtr.Zero)
                        {
                            return _renderSharedHandles[index];
                        }
                    }
                    catch { }

                    // Fallback: attempt a best-effort query similar to legacy behavior
                    try
                    {
                        try { _context.Flush(); } catch { }
                        var dxgiRes = target.QueryInterfaceOrNull<IDXGIResource>();
                        if (dxgiRes != null)
                        {
                            try
                            {
                                var prop = dxgiRes.GetType().GetProperty("SharedHandle");
                                if (prop != null)
                                {
                                    var val = prop.GetValue(dxgiRes);
                                    if (val is IntPtr h3 && h3 != IntPtr.Zero) return h3;
                                }

                                var method = dxgiRes.GetType().GetMethod("GetSharedHandle");
                                if (method != null)
                                {
                                    var parms = method.GetParameters();
                                    if (parms.Length == 1)
                                    {
                                        var args = new object[] { IntPtr.Zero };
                                        method.Invoke(dxgiRes, args);
                                        if (args[0] is IntPtr h2 && h2 != IntPtr.Zero) return h2;
                                    }
                                    else if (method.ReturnType == typeof(IntPtr))
                                    {
                                        var ret = method.Invoke(dxgiRes, null);
                                        if (ret is IntPtr h4 && h4 != IntPtr.Zero) return h4;
                                    }
                                }
                            }
                            finally { try { dxgiRes.Dispose(); } catch { } }
                        }
                    }
                    catch { }

                    return IntPtr.Zero;
                }
                finally
                {
                    try { sbuf?.Dispose(); } catch { }
                    try { constBuf?.Dispose(); } catch { }
                }
            }
            catch
            {
                try { sbuf?.Dispose(); } catch { }
                try { constBuf?.Dispose(); } catch { }
                return IntPtr.Zero;
            }
        }


        /// <summary>
        /// Runs a simple benchmark by dispatching the existing compute shader multiple times and measuring elapsed time.
        /// Returns elapsed ms, pixels/sec and a lightweight checksum of output data.
        /// </summary>
        public static BenchmarkResult RunComputeBenchmark(int size, int iterations)
        {
            // New GPU-only benchmark path: dispatch a simple compute shader repeatedly
            // without performing GPU->CPU readbacks. This measures GPU dispatch throughput
            // and avoids the large CPU stalls caused by staging readbacks used previously.
            EnsureInitialized();
            if (s_instance == null) return new BenchmarkResult(0, 0, 0, 0);

            try
            {
                // Simple fill shader that writes a constant color to the UAV. Keep threadsize aligned
                // with common group sizes so dispatching is efficient.
                var cs = s_instance.GetOrCreateShader("CS_BenchmarkMain");
                if (cs == null) return new BenchmarkResult(0, 0, 0, 0);

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // constant buffer for width/height
                var cb = new int[4] { size, size, 0, 0 };
                GCHandle h = GCHandle.Alloc(cb, GCHandleType.Pinned);
                ID3D11Buffer constBuf;
                try
                {
                    var cbd = new BufferDescription
                    {
                        Usage = ResourceUsage.Default,
                        SizeInBytes = sizeof(int) * cb.Length,
                        BindFlags = BindFlags.ConstantBuffer,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None,
                        StructureByteStride = 0
                    };
                    var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                    constBuf = s_instance._device.CreateBuffer(cbd, init);
                }
                finally { h.Free(); }

                var tgX = (size + 15) / 16;
                var tgY = (size + 15) / 16;

                // Dispatch iterations without forcing a blocking CPU wait per-iteration.
                // After all dispatches submit a GPU->CPU staging copy and Map which will
                // ensure the GPU has completed the work. This measures true GPU time
                // instead of only CPU dispatch overhead.
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    s_instance._context.CSSetShader(cs);
                    s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });
                    s_instance._context.Dispatch(tgX, tgY, 1);
                    // Unbind resources for next iteration (do not Flush here)
                    s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                    s_instance._context.CSSetShader(null);
                    s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });
                }

                // Create a staging texture and copy GPU output to it; Map will block until
                // the GPU work is finished which gives an accurate elapsed time measurement.
                try
                {
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = size,
                        Height = size,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.R32G32B32A32_Float,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                    // Copy the last-dispatched GPU texture to staging. This implicitly ensures
                    // the GPU completes the previously queued dispatches before Map returns.
                    s_instance._context.CopyResource(staging, gpuTex);
                    var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try { /* just map/unmap to wait for GPU completion */ }
                    finally { s_instance._context.Unmap(staging, 0); }
                }
                catch { /* best-effort staging; if it fails fall back to elapsed time measured so far */ }

                sw.Stop();

                double elapsedMs = sw.Elapsed.TotalMilliseconds;
                double pixels = (double)size * size * iterations;
                double pxPerSec = pixels / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 1.0);
                // lightweight checksum: use size*iterations as a proxy (avoid readback)
                long checksum = (long)size ^ iterations;
                return new BenchmarkResult(elapsedMs, pxPerSec, checksum, pixels);
            }
            catch
            {
                return new BenchmarkResult(0, 0, 0, 0);
            }
        }

        // Internal helpers for interop: expose device and render target textures for use by
        // a native D3D11 swap-chain presenter. These return the underlying Vortice objects
        // so they can be used directly when presenting on the same D3D11 device.
        internal static Vortice.Direct3D11.ID3D11Device? GetD3D11DeviceForInterop()
        {
            EnsureInitialized();
            return s_instance?._device;
        }

        internal static Vortice.Direct3D11.ID3D11Texture2D? GetRenderTextureForInterop(int index)
        {
            EnsureInitialized();
            try
            {
                if (s_instance == null) return null;
                if (index < 0 || index >= (s_instance._renderTextures?.Length ?? 0)) return null;
                return s_instance._renderTextures?[index];
            }
            catch { return null; }
        }

        /// <summary>
        /// Upload a <see cref="PixelBuffer"/> to a new GPU texture (R32G32B32A32_Float).
        /// The returned texture has ShaderResource | UnorderedAccess bind flags and is
        /// owned by the caller. Returns null on failure.
        /// Caller should execute this under <see cref="GpuScheduler"/> to serialize access.
        /// </summary>
        internal static ID3D11Texture2D? UploadPixelBufferToTexture(PixelBuffer input)
        {
            EnsureInitialized();
            if (s_instance == null || input == null) return null;
            try
            {
                var width = input.Width;
                var height = input.Height;
                if (width <= 0 || height <= 0) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                var gpuTex = s_instance._device.CreateTexture2D(texDesc);

                var stagingDesc = texDesc;
                stagingDesc.Usage = ResourceUsage.Staging;
                stagingDesc.BindFlags = BindFlags.None;
                stagingDesc.CpuAccessFlags = CpuAccessFlags.Write;
                using var staging = s_instance._device.CreateTexture2D(stagingDesc);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    input.CopyTo(mapped.DataPointer, mapped.RowPitch);
                }
                finally { s_instance._context.Unmap(staging, 0); }

                s_instance._context.CopyResource(gpuTex, staging);
                return gpuTex;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Dispatch a compute shader that reads from a source SRV and writes to an output UAV.
        /// Creates the output texture, dispatches, unbinds, copies the result, and returns
        /// a standalone texture that the caller owns. Returns null on failure.
        /// Caller should execute this under <see cref="GpuScheduler"/> to serialize access.
        /// </summary>
        internal static ID3D11Texture2D? DispatchImageFilter(
            string shaderEntryPoint,
            ID3D11Texture2D srcTex,
            object[] constantBufferSlots,
            int threadGroupSize = 8)
        {
            EnsureInitialized();
            if (s_instance == null || srcTex == null) return null;
            try
            {
                var cs = s_instance.GetOrCreateShader(shaderEntryPoint);
                if (cs == null) return null;

                var desc = srcTex.Description;
                var width = desc.Width;
                var height = desc.Height;

                var outDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);
                using var outTex = s_instance._device.CreateTexture2D(outDesc);
                using var outUav = s_instance._device.CreateUnorderedAccessView(outTex);
                using var paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, constantBufferSlots);

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { outUav }, new int[] { -1 });

                var tgX = (width + threadGroupSize - 1) / threadGroupSize;
                var tgY = (height + threadGroupSize - 1) / threadGroupSize;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                s_instance._context.CSSetShader(null);

                var returnTex = s_instance._device.CreateTexture2D(outDesc);
                s_instance._context.CopyResource(returnTex, outTex);
                return returnTex;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.DispatchImageFilter({shaderEntryPoint}) 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Dispatch a compute shader that operates in-place on a single UAV texture.
        /// Binds the texture as UAV, dispatches, unbinds, and returns true on success.
        /// Caller should execute this under <see cref="GpuScheduler"/> to serialize access.
        /// </summary>
        internal static bool DispatchInPlace(
            string shaderEntryPoint,
            ID3D11Texture2D tex,
            object[] constantBufferSlots,
            int threadGroupSize = 8)
        {
            EnsureInitialized();
            if (s_instance == null || tex == null) return false;
            try
            {
                var cs = s_instance.GetOrCreateShader(shaderEntryPoint);
                if (cs == null) return false;

                var desc = tex.Description;
                var width = desc.Width;
                var height = desc.Height;

                using var uav = s_instance._device.CreateUnorderedAccessView(tex);
                using var paramBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, constantBufferSlots);

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { paramBuf });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (width + threadGroupSize - 1) / threadGroupSize;
                var tgY = (height + threadGroupSize - 1) / threadGroupSize;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GpuCompute.DispatchInPlace({shaderEntryPoint}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read a GPU texture into a managed <see cref="PixelBuffer"/>.
        /// This performs a GPU copy to a staging texture and maps it for CPU read.
        /// Caller should execute this under <see cref="GpuScheduler"/> to serialize access.
        /// </summary>
        internal static PixelBuffer? ReadTextureToPixelBuffer(ID3D11Texture2D? src)
        {
            EnsureInitialized();
            if (s_instance == null || src == null) return null;
            try
            {
                var desc = src.Description;
                var sizeX = desc.Width;
                var sizeY = desc.Height;

                // Handle common formats: float RGBA and BGRA UNorm (used for interop/presentation).
                if (desc.Format == Format.R32G32B32A32_Float)
                {
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = sizeX,
                        Height = sizeY,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.R32G32B32A32_Float,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                    s_instance._context.CopyResource(staging, src);

                    var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var buffer = PixelBufferPool.Borrow(sizeX, sizeY);
                        buffer.CopyFrom(mapped.DataPointer, mapped.RowPitch);
                        return buffer;
                    }
                    finally
                    {
                        s_instance._context.Unmap(staging, 0);
                    }
                }
                else if (desc.Format == Format.B8G8R8A8_UNorm || desc.Format == Format.B8G8R8A8_UNorm_SRgb)
                {
                    // Read as BGRA bytes and convert to float PixelBuffer
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = sizeX,
                        Height = sizeY,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                    s_instance._context.CopyResource(staging, src);

                    var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var rowPitch = mapped.RowPitch; // bytes
                        var rowBytes = sizeX * 4;
                        var bytes = new byte[sizeX * sizeY * 4];
                        for (int y = 0; y < sizeY; y++)
                        {
                            var srcPtr = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            System.Runtime.InteropServices.Marshal.Copy(srcPtr, bytes, y * rowBytes, rowBytes);
                        }

                        var buffer = PixelBufferPool.Borrow(sizeX, sizeY);
                        var data = buffer.AsSpan();
                        // Convert BGRA bytes -> float RGBA
                        for (int i = 0, di = 0; i < bytes.Length; i += 4, di += 4)
                        {
                            var b = bytes[i];
                            var g = bytes[i + 1];
                            var r = bytes[i + 2];
                            var a = bytes[i + 3];
                            data[di] = r / 255f;
                            data[di + 1] = g / 255f;
                            data[di + 2] = b / 255f;
                            data[di + 3] = a / 255f;
                        }

                        return buffer;
                    }
                    finally
                    {
                        s_instance._context.Unmap(staging, 0);
                    }
                }
                else
                {
                    // Fallback: try to copy into a float staging texture using GPU conversion if possible.
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = sizeX,
                        Height = sizeY,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.R32G32B32A32_Float,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None
                    };

                    using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                    try
                    {
                        s_instance._context.CopyResource(staging, src);
                    }
                    catch
                    {
                        return null;
                    }

                    var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var buffer = PixelBufferPool.Borrow(sizeX, sizeY);
                        buffer.CopyFrom(mapped.DataPointer, mapped.RowPitch);
                        return buffer;
                    }
                    finally
                    {
                        s_instance._context.Unmap(staging, 0);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private PixelBuffer? Rasterize(int size, int shapeType, float sizeParam, float scaleX, float scaleY, float rotation, float offsetX, float offsetY, float r, float g, float b, float hardness, bool invert)
        {
            try
            {
                Debug.WriteLine($"GpuCompute.Rasterize: size={size}, shapeType={shapeType}, sizeParam={sizeParam}, scaleX={scaleX}, scaleY={scaleY}, rotation={rotation}, offsetX={offsetX}, offsetY={offsetY}, 颜色=({r},{g},{b}), hardness={hardness}, invert={invert}");
                // Create GPU texture (R32G32B32A32_Float)
                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = _device.CreateTexture2D(texDesc);
                using var uav = _device.CreateUnorderedAccessView(gpuTex);

                // Prepare constant buffer
                var halfSize = size * 0.5f;
                var radius = halfSize * sizeParam;
                var centerX = halfSize + offsetX * size;
                var centerY = halfSize + offsetY * size;

                var cb = new float[16];
                cb[0] = shapeType;
                cb[1] = radius;
                cb[2] = centerX;
                cb[3] = centerY;
                cb[4] = rotation;
                cb[5] = scaleX;
                cb[6] = scaleY;
                cb[7] = r;
                cb[8] = g;
                cb[9] = b;
                cb[10] = 0f; // padding
                cb[11] = hardness;
                cb[12] = invert ? 1f : 0f;

                Debug.WriteLine($"GpuCompute: 常量缓冲区内容[0..12]={string.Join(",", cb.Take(13))}");

                var cbDesc = new BufferDescription
                {
                    Usage = ResourceUsage.Default,
                    SizeInBytes = sizeof(float) * cb.Length,
                    BindFlags = BindFlags.ConstantBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                };

                GCHandle h = GCHandle.Alloc(cb, GCHandleType.Pinned);
                ID3D11Buffer constBuf;
                try
                {
                    var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                    constBuf = _device.CreateBuffer(cbDesc, init);
                }
                finally
                {
                    h.Free();
                }

                // Bind and dispatch
                _context.CSSetShader(_computeShader);
                _context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                _context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 15) / 16;
                var tgY = (size + 15) / 16;
                _context.Dispatch(tgX, tgY, 1);

                // Unbind
                _context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                _context.CSSetShader(null);
                _context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                // Copy to staging and read back
                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = _device.CreateTexture2D(stagingDesc);
                _context.CopyResource(staging, gpuTex);

                var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch; // bytes
                    var rowFloats = (int)(rowPitch / sizeof(float));

                    if (rowFloats == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIndex = 0;
                        for (int y = 0; y < size; y++)
                        {
                            var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            Marshal.Copy(src, temp, destIndex, size * 4);
                            destIndex += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());

                    // Diagnostic: compute max alpha and log a few pixels
                    try
                    {
                        float maxA = 0f;
                        for (int i = 3; i < temp.Length; i += 4) maxA = Math.Max(maxA, temp[i]);
                        Debug.WriteLine($"GpuCompute: 回读最大 alpha={maxA}");
                        // log center pixel RGBA
                        var cx = Math.Clamp(size / 2, 0, size - 1);
                        var cy = Math.Clamp(size / 2, 0, size - 1);
                        var cidx = (cy * size + cx) * 4;
                        if (cidx + 3 < temp.Length)
                        {
                            Debug.WriteLine($"GpuCompute: 中心像素 RGBA={temp[cidx]},{temp[cidx+1]},{temp[cidx+2]},{temp[cidx+3]}");
                        }
                    }
                    catch { }

                    return buffer;
                }
                finally
                {
                    _context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                foreach (var kv in _shaderCache)
                {
                    try { kv.Value?.Dispose(); } catch { }
                }
                _shaderCache.Clear();
            }
            catch { }

            // Dispose render target resources (double-buffered for interop rendering).
            if (_renderTextures != null)
            {
                for (int i = 0; i < _renderTextures.Length; i++)
                {
                    try { _renderUavs?[i]?.Dispose(); } catch { }
                    try { _renderSrvs?[i]?.Dispose(); } catch { }
                    try { _renderTextures[i]?.Dispose(); } catch { }
                }
            }

            try { _dynamicTileConstantBuffer?.Dispose(); } catch { }
            try { _computeShader?.Dispose(); } catch { }
            try { _context?.Dispose(); } catch { }
            try { _device?.Dispose(); } catch { }
        }

        /// <summary>
        /// Simple GPU helper: fills a texture with a constant color using a compute shader.
        /// Returns a PixelBuffer on success or null on failure.
        /// </summary>
        public static PixelBuffer? RasterizeSolidColor(int size, float r, float g, float b, float a)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                // Use the precompiled/embedded solid-color compute shader entry
                var cs = s_instance.GetOrCreateShader("CS_SolidColorMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex) ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                // constant buffer
                var cb = new float[4] { r, g, b, a };
                GCHandle h = GCHandle.Alloc(cb, GCHandleType.Pinned);
                ID3D11Buffer constBuf;
                try
                {
                    var cbd = new BufferDescription
                    {
                        Usage = ResourceUsage.Default,
                        SizeInBytes = sizeof(float) * cb.Length,
                        BindFlags = BindFlags.ConstantBuffer,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None,
                        StructureByteStride = 0
                    };
                    var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                    constBuf = s_instance._device.CreateBuffer(cbd, init);
                }
                finally { h.Free(); }

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 15) / 16;
                var tgY = (size + 15) / 16;
                s_instance._context.Dispatch(tgX, tgY, 1);

                // unbind
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);
                s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                // readback
                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, gpuTex);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch;
                    var rowFloats = (int)(rowPitch / sizeof(float));
                    if (rowFloats == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIndex = 0;
                        for (int y = 0; y < size; y++)
                        {
                            var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            Marshal.Copy(src, temp, destIndex, size * 4);
                            destIndex += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterizes a simple gradient on GPU. Supports horizontal, vertical and radial modes.
        /// mode: 0=horizontal,1=vertical,2=radial
        /// </summary>
        public static PixelBuffer? RasterizeGradient(int size, int mode, float r0, float g0, float b0, float r1, float g1, float b1, int repeat, float offset, float midpoint, float rotation, bool tiling, bool invert)
        {
            EnsureInitialized();
            if (s_instance == null) return null;
            try
            {
                var cs = s_instance.GetOrCreateShader("CS_GradientMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = s_instance._device.CreateUnorderedAccessView(gpuTex);

                // Pack params in HLSL GradientCB order: ints first (mode, repeat, tiling, invert),
                // then floats (r0,g0,b0,r1,g1,b1,offset,midpoint,rotation), then padding.
                // See Shaders.hlsl GradientCB struct.
                using var constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device, new object[] { mode, repeat, tiling ? 1 : 0, invert ? 1 : 0, r0, g0, b0, r1, g1, b1, offset, midpoint, rotation, 0, 0, 0 });

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 7) / 8;
                var tgY = (size + 7) / 8;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);
                s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, gpuTex);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch;
                    var rowFloats = (int)(rowPitch / sizeof(float));
                    if (rowFloats == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIndex = 0;
                        for (int y = 0; y < size; y++)
                        {
                            var src = IntPtr.Add(mapped.DataPointer, y * rowPitch);
                            Marshal.Copy(src, temp, destIndex, size * 4);
                            destIndex += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generic dispatcher for simple per-pixel compute shaders that write to a single float4 UAV.
        /// Creates an RGBA32F texture, runs the shader with the given constant buffer data, and returns the result.
        /// </summary>
        private static PixelBuffer? DispatchComputeShader(string entryPoint, int size, object[] cbufferFields)
        {
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                var cs = s_instance.GetOrCreateShader(entryPoint);
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var gpuTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(gpuTex)
                    ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                using var constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(
                    s_instance._device, cbufferFields);

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 7) / 8;
                var tgY = (size + 7) / 8;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);
                s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                var stagingDesc = new Texture2DDescription
                {
                    Width = size,
                    Height = size,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var staging = s_instance._device.CreateTexture2D(stagingDesc);
                s_instance._context.CopyResource(staging, gpuTex);

                var mapped = s_instance._context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = mapped.RowPitch;
                    if (rowPitch / sizeof(float) == size * 4)
                    {
                        Marshal.Copy(mapped.DataPointer, temp, 0, floatCount);
                    }
                    else
                    {
                        var destIdx = 0;
                        for (int row = 0; row < size; row++)
                        {
                            Marshal.Copy(IntPtr.Add(mapped.DataPointer, row * rowPitch), temp, destIdx, size * 4);
                            destIdx += size * 4;
                        }
                    }

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally
                {
                    s_instance._context.Unmap(staging, 0);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// GPU-accelerated lattice pattern generation.
        /// </summary>
        public static PixelBuffer? RasterizeLattice(int size, float scale, float thickness, float rotation, float softness, bool invert)
        {
            return DispatchComputeShader("CS_LatticeMain", size, new object[]
            {
                scale, thickness, rotation, softness, invert ? 1 : 0, size, size, 0
            });
        }

        /// <summary>
        /// GPU-accelerated concentric ring pattern generation.
        /// </summary>
        public static PixelBuffer? RasterizeConcentric(int size, float count, float thickness, float distortion, float cx, float cy, bool invert, bool smooth)
        {
            return DispatchComputeShader("CS_ConcentricMain", size, new object[]
            {
                count, thickness, distortion, cx, cy, invert ? 1 : 0, size, size, smooth ? 1 : 0
            });
        }

        /// <summary>
        /// GPU-accelerated spiral pattern generation.
        /// </summary>
        public static PixelBuffer? RasterizeSpiral(int size, float arms, float turns, float lineWidth, float distortion, int type, bool invert, int seed)
        {
            return DispatchComputeShader("CS_SpiralMain", size, new object[]
            {
                arms, turns, lineWidth, distortion, type, invert ? 1 : 0, size, size, seed
            });
        }

        /// <summary>
        /// GPU-accelerated normal map generation from heightmap.
        /// Uploads the heightmap to a GPU texture, runs Sobel-based normal map shader, reads back result.
        /// </summary>
        public static PixelBuffer? RasterizeNormalMap(PixelBuffer heightmap, float strength, bool flipX, bool flipY)
        {
            var size = heightmap.Width;
            EnsureInitialized();
            if (s_instance == null) return null;

            try
            {
                var cs = s_instance.GetOrCreateShader("CS_NormalMapMain");
                if (cs == null) return null;

                var texDesc = new Texture2DDescription
                {
                    Width = size, Height = size, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                // Upload heightmap to GPU
                using var srcTex = s_instance._device.CreateTexture2D(texDesc);
                var stagingDesc = texDesc;
                stagingDesc.Usage = ResourceUsage.Staging;
                stagingDesc.BindFlags = BindFlags.None;
                stagingDesc.CpuAccessFlags = CpuAccessFlags.Write;
                using var staging = s_instance._device.CreateTexture2D(stagingDesc);

                var pixelData = heightmap.AsSpan().ToArray();
                var mapped = s_instance._context.Map(staging, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    for (int y = 0; y < size; y++)
                    {
                        var destPtr = IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch);
                        Marshal.Copy(pixelData, y * size * 4, destPtr, size * 4);
                    }
                }
                finally { s_instance._context.Unmap(staging, 0); }
                s_instance._context.CopyResource(srcTex, staging);

                using var srv = s_instance._device.CreateShaderResourceView(srcTex);

                // Output texture
                using var outTex = s_instance._device.CreateTexture2D(texDesc);
                using var uav = CreateUnorderedAccessViewWithRecovery(outTex)
                    ?? throw new InvalidOperationException("CreateUnorderedAccessView failed");

                using var constBuf = GpuBufferHelpers.CreatePackedConstantBuffer(s_instance._device,
                    new object[] { strength, flipX ? 1 : 0, flipY ? 1 : 0, size, size, 0, 0, 0 });

                s_instance._context.CSSetShader(cs);
                s_instance._context.CSSetConstantBuffers(0, 1, new[] { constBuf });
                s_instance._context.CSSetShaderResources(0, 1, new[] { srv });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new[] { uav }, new int[] { -1 });

                var tgX = (size + 7) / 8;
                var tgY = (size + 7) / 8;
                s_instance._context.Dispatch(tgX, tgY, 1);

                s_instance._context.CSSetShaderResources(0, 1, new ID3D11ShaderResourceView[] { null! });
                s_instance._context.CSSetUnorderedAccessViews(0, 1, new ID3D11UnorderedAccessView[] { null! }, new int[] { -1 });
                s_instance._context.CSSetShader(null);
                s_instance._context.CSSetConstantBuffers(0, 1, new ID3D11Buffer[] { null! });

                // Read back
                var readbackDesc = new Texture2DDescription
                {
                    Width = size, Height = size, MipLevels = 1, ArraySize = 1,
                    Format = Format.R32G32B32A32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
                };

                using var readback = s_instance._device.CreateTexture2D(readbackDesc);
                s_instance._context.CopyResource(readback, outTex);

                var rmapped = s_instance._context.Map(readback, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var floatCount = size * size * 4;
                    var temp = new float[floatCount];
                    var rowPitch = rmapped.RowPitch;
                    if (rowPitch / sizeof(float) == size * 4)
                        Marshal.Copy(rmapped.DataPointer, temp, 0, floatCount);
                    else
                        for (int row = 0; row < size; row++)
                            Marshal.Copy(IntPtr.Add(rmapped.DataPointer, row * rowPitch), temp, row * size * 4, size * 4);

                    var buffer = PixelBufferPool.Borrow(size, size);
                    temp.AsSpan().CopyTo(buffer.AsSpan());
                    return buffer;
                }
                finally { s_instance._context.Unmap(readback, 0); }
            }
            catch { return null; }
        }

        /// <summary>
        /// GPU-accelerated wood texture generation.
        /// </summary>
        public static PixelBuffer? RasterizeWood(int size, float density, float distortion, float sharpness,
            float r1, float g1, float b1, float r2, float g2, float b2, bool invert, int seed)
        {
            return DispatchComputeShader("CS_WoodMain", size, new object[]
            {
                density, distortion, sharpness, r1, g1, b1, r2, g2, b2,
                invert ? 1 : 0, size, size, seed
            });
        }

        /// <summary>
        /// GPU-accelerated cloud texture generation.
        /// </summary>
        public static PixelBuffer? RasterizeCloud(int size, float scale, float density, float sharpness, float coverage,
            float detail, int octaves, float skyR, float skyG, float skyB, float cloudR, float cloudG, float cloudB,
            bool invert, int seed)
        {
            return DispatchComputeShader("CS_CloudMain", size, new object[]
            {
                scale, density, sharpness, coverage, detail, octaves,
                skyR, skyG, skyB, cloudR, cloudG, cloudB,
                invert ? 1 : 0, size, size, seed
            });
        }

        /// <summary>
        /// GPU-accelerated marble texture generation.
        /// </summary>
        public static PixelBuffer? RasterizeMarble(int size, float scale, float freq, float sharpness, float distortion,
            int octaves, float r1, float g1, float b1, float r2, float g2, float b2, bool invert, int seed)
        {
            return DispatchComputeShader("CS_MarbleMain", size, new object[]
            {
                scale, freq, sharpness, distortion, octaves,
                r1, g1, b1, r2, g2, b2,
                invert ? 1 : 0, size, size, seed
            });
        }

        /// <summary>
        /// GPU-accelerated honeycomb pattern generation.
        /// </summary>
        public static PixelBuffer? RasterizeHoneycomb(int size, float scale, float wallThick, float bevel,
            float r, float g, float b, float wallR, float wallG, float wallB, bool invert)
        {
            return DispatchComputeShader("CS_HoneycombMain", size, new object[]
            {
                scale, wallThick, bevel, r, g, b, wallR, wallG, wallB,
                invert ? 1 : 0, size, size
            });
        }

        /// <summary>
        /// GPU-accelerated wave pattern generation.
        /// </summary>
        public static PixelBuffer? RasterizeWave(int size, int type, float freqX, float freqY, float amp,
            float phase, float sharpness, bool invert, int seed)
        {
            return DispatchComputeShader("CS_WaveMain", size, new object[]
            {
                type, freqX, freqY, amp, phase, sharpness,
                invert ? 1 : 0, size, size, seed
            });
        }
    }
}
#endif
