using System;
using System.Threading;
using System.Threading.Tasks;

namespace PixelAssetGenerator.Core.Gpu;

/// <summary>
/// Centralized GPU scheduler / resource pool wrapper around low-level GpuCompute.
/// Serializes access to the underlying D3D11 device/context to avoid driver issues
/// and provides convenience helpers for nodes to call GPU operations without
/// duplicating synchronization or error-handling logic.
/// </summary>
internal sealed class GpuScheduler
{
    private static readonly Lazy<GpuScheduler> s_lazy = new(() => new GpuScheduler());
    public static GpuScheduler Instance => s_lazy.Value;

    // Semaphore to serialize GPU dispatches because the underlying D3D11 context
    // in GpuCompute is shared and not designed for concurrent dispatches from
    // multiple threads.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private GpuScheduler() { }

    /// <summary>
    /// Ensures GPU subsystem is initialized and ready.
    /// </summary>
    public bool IsSupported => GpuCompute.IsSupported;

    /// <summary>
    /// Ensures internal render resources for a given size are created (no-op if already present).
    /// Safe to call from any thread.
    /// </summary>
    public void EnsureRenderResources(int size)
    {
        if (!IsSupported) return;
        // Serialize to avoid races during resource creation.
        _semaphore.Wait();
        try
        {
            GpuCompute.EnsureRenderResources(size);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Synchronous call that rasterizes a single shape on the GPU.
    /// This method serializes access to the GPU and returns the resulting PixelBuffer
    /// or null on failure.
    /// </summary>
    public PixelBuffer? RasterizeShape(int size, int shapeType, float sizeParam, float scaleX, float scaleY, float rotation, float offsetX, float offsetY, float r, float g, float b, float hardness, bool invert)
    {
        if (!IsSupported) return null;
        _semaphore.Wait();
        try
        {
            return GpuCompute.RasterizeShapeSimple(size, shapeType, sizeParam, scaleX, scaleY, rotation, offsetX, offsetY, r, g, b, hardness, invert);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Asynchronous form for rasterizing a shape.
    /// </summary>
    public async Task<PixelBuffer?> RasterizeShapeAsync(int size, int shapeType, float sizeParam, float scaleX, float scaleY, float rotation, float offsetX, float offsetY, float r, float g, float b, float hardness, bool invert)
    {
        if (!IsSupported) return null;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return GpuCompute.RasterizeShapeSimple(size, shapeType, sizeParam, scaleX, scaleY, rotation, offsetX, offsetY, r, g, b, hardness, invert);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Rasterize a batch of shapes into a PixelBuffer on the GPU.
    /// </summary>
    public PixelBuffer? RasterizeBatch(int size, GpuCompute.ShapeParams[] shapes)
    {
        if (!IsSupported) return null;
        _semaphore.Wait();
        try
        {
            return GpuCompute.RasterizeBatch(size, shapes);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Async batch rasterize.
    /// </summary>
    public async Task<PixelBuffer?> RasterizeBatchAsync(int size, GpuCompute.ShapeParams[] shapes)
    {
        if (!IsSupported) return null;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return GpuCompute.RasterizeBatch(size, shapes);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Returns a human friendly adapter info via the underlying GpuCompute helper.
    /// </summary>
    public GpuCompute.AdapterInfo GetAdapterInfo()
    {
        // GpuCompute handles initialization internally.
        return GpuCompute.GetAdapterInfo();
    }

    /// <summary>
    /// Rasterize a solid color via GPU.
    /// </summary>
    public PixelBuffer? RasterizeSolid(int size, float r, float g, float b, float a)
    {
        if (!IsSupported) return null;
        _semaphore.Wait();
        try
        {
            return GpuCompute.RasterizeSolidColor(size, r, g, b, a);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<PixelBuffer?> RasterizeSolidAsync(int size, float r, float g, float b, float a)
    {
        if (!IsSupported) return null;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return GpuCompute.RasterizeSolidColor(size, r, g, b, a);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Rasterize a gradient via GPU.
    /// </summary>
    public PixelBuffer? RasterizeGradient(int size, int mode, float r0, float g0, float b0, float r1, float g1, float b1, int repeat, float offset, float midpoint, float rotation, bool tiling, bool invert)
    {
        if (!IsSupported) return null;
        _semaphore.Wait();
        try
        {
            return GpuCompute.RasterizeGradient(size, mode, r0, g0, b0, r1, g1, b1, repeat, offset, midpoint, rotation, tiling, invert);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Rasterize procedural noise via GPU.
    /// </summary>
    public PixelBuffer? RasterizeNoise(int size, int seed, float scale, int octaves, float persistence, float lacunarity, int noiseType, float brightness, float contrast, float offsetX, float offsetY, float threshLow, float threshHigh, bool invert, bool colorOutput)
    {
        if (!IsSupported) return null;
        _semaphore.Wait();
        try
        {
            return GpuCompute.RasterizeNoise(size, seed, scale, octaves, persistence, lacunarity, noiseType, brightness, contrast, offsetX, offsetY, threshLow, threshHigh, invert, colorOutput);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<PixelBuffer?> RasterizeGradientAsync(int size, int mode, float r0, float g0, float b0, float r1, float g1, float b1, int repeat, float offset, float midpoint, float rotation, bool tiling, bool invert)
    {
        if (!IsSupported) return null;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return GpuCompute.RasterizeGradient(size, mode, r0, g0, b0, r1, g1, b1, repeat, offset, midpoint, rotation, tiling, invert);
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an arbitrary GPU-bound function while serializing access to the device.
    /// Callers can pass any function that performs work using GpuCompute (for example
    /// rendering to a shared texture) and receive its return value. Exceptions are
    /// swallowed and a default(TResult) is returned on failure.
    /// </summary>
    public TResult Execute<TResult>(Func<TResult> gpuAction)
    {
        if (!IsSupported) return default!;
        _semaphore.Wait();
        try
        {
            return gpuAction();
        }
        catch
        {
            return default!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Asynchronous form of Execute. Awaits the semaphore and runs the supplied
    /// GPU function, returning its result or default(TResult) on error.
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> gpuAction)
    {
        if (!IsSupported) return default!;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await gpuAction().ConfigureAwait(false);
        }
        catch
        {
            return default!;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

