using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator.Core;

/// <summary>
/// High-precision float-based RGBA pixel buffer used for internal node computation.
/// All channels are stored as float in [0,1] range, avoiding precision loss during multi-step processing.
/// </summary>
public sealed class PixelBuffer : IDisposable
{
    private readonly float[] _data; // interleaved R,G,B,A
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

    public PixelBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _data = new float[width * height * 4];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PixelBufferPool.Return(this);
    }

    /// <summary>
    /// Creates a buffer filled with a solid color.
    /// </summary>
    public static PixelBuffer CreateSolid(int width, int height, float r, float g, float b, float a = 1f)
    {
        var buffer = PixelBufferPool.Borrow(width, height);
        var data = buffer._data;
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
        return buffer;
    }

    /// <summary>
    /// Creates a single-channel grayscale buffer (R=G=B=value, A=1).
    /// </summary>
    public static PixelBuffer CreateGrayscale(int width, int height, float value)
    {
        return CreateSolid(width, height, value, value, value, 1f);
    }

    /// <summary>
    /// Creates a grayscale visualization of a mask buffer, A=1.
    /// Used by PreviewNode to correctly display a mask connected via an Any port.
    /// When the source buffer has varying alpha (coverage-style masks, e.g. ShapeNode),
    /// the alpha channel is used as the mask value. When all pixels are fully opaque
    /// (pattern-style masks, e.g. BrickNode or SplatterCircularNode), the RGB luminance
    /// is used instead — consistent with how MaskBlendNode reads masks by default.
    /// </summary>
    public static PixelBuffer CreateMaskView(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Decide which channel encodes the mask by scanning the alpha channel once.
        // If any pixel has alpha < 1 the buffer uses alpha for coverage (ShapeNode convention).
        // If all pixels are fully opaque, the mask pattern is carried in RGB luminance
        // (BrickNode / SplatterCircularNode convention, where A is always 1).
        bool useAlpha = false;
        var span = source.AsReadOnlySpan();
        for (var i = 3; i < span.Length; i += 4)
        {
            if (span[i] < 0.99f) { useAlpha = true; break; }
        }

        var result = PixelBufferPool.Borrow(source.Width, source.Height);
        result.ForEachPixel((x, y) =>
        {
            var (r, g, b, a) = source.GetPixel(x, y);
            float v = useAlpha
                ? a
                : 0.2126f * r + 0.7152f * g + 0.0722f * b;
            result.SetPixel(x, y, v, v, v, 1f);
        });
        return result;
    }

    public void SetPixel(int x, int y, float r, float g, float b, float a)
    {
        var idx = (y * Width + x) * 4;
        _data[idx] = r;
        _data[idx + 1] = g;
        _data[idx + 2] = b;
        _data[idx + 3] = a;
    }

    public (float R, float G, float B, float A) GetPixel(int x, int y)
    {
        var idx = (y * Width + x) * 4;
        return (_data[idx], _data[idx + 1], _data[idx + 2], _data[idx + 3]);
    }

    /// <summary>
    /// Gets pixel with tileable wrapping (coordinates are wrapped to [0, Width) and [0, Height)).
    /// </summary>
    /// <summary>
    /// Sets pixel with tileable wrapping (coordinates are wrapped to [0, Width) and [0, Height)).
    /// </summary>
    public void SetPixelWrapped(int x, int y, float r, float g, float b, float a)
    {
        x = ((x % Width) + Width) % Width;
        y = ((y % Height) + Height) % Height;
        SetPixel(x, y, r, g, b, a);
    }

    public (float R, float G, float B, float A) GetPixelWrapped(int x, int y)
    {
        x = ((x % Width) + Width) % Width;
        y = ((y % Height) + Height) % Height;
        return GetPixel(x, y);
    }

    /// <summary>
    /// Gets the single-channel value (R channel) at a pixel — useful for grayscale/mask buffers.
    /// </summary>
    public float GetValue(int x, int y)
    {
        return _data[(y * Width + x) * 4];
    }

    /// <summary>
    /// Gets the single-channel value with tileable wrapping.
    /// </summary>
    public float GetValueWrapped(int x, int y)
    {
        x = ((x % Width) + Width) % Width;
        y = ((y % Height) + Height) % Height;
        return _data[(y * Width + x) * 4];
    }

    /// <summary>
    /// Provides direct access to the underlying float data for high-performance operations.
    /// Layout: interleaved RGBA, row-major, length = Width * Height * 4.
    /// </summary>
    public Span<float> AsSpan() => _data.AsSpan();

    /// <summary>
    /// Provides read-only access to the underlying data.
    /// </summary>
    public ReadOnlySpan<float> AsReadOnlySpan() => _data.AsSpan();

    /// <summary>
    /// Executes a per-pixel action in parallel for performance on large buffers.
    /// Falls back to serial for small buffers (e.g. thumbnails) to avoid Parallel.For overhead.
    /// </summary>
    public void ForEachPixel(Action<int, int> action, bool parallel = true)
    {
        if (parallel && Width * Height >= 128 * 128)
        {
            Parallel.For(0, Height, y =>
            {
                for (var x = 0; x < Width; x++)
                    action(x, y);
            });
        }
        else
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    action(x, y);
        }
    }

    /// <summary>
    /// Creates a deep copy of this buffer.
    /// </summary>
    public PixelBuffer Clone()
    {
        var copy = PixelBufferPool.Borrow(Width, Height);
        _data.AsSpan().CopyTo(copy._data);
        return copy;
    }

    /// <summary>
    /// Copies float data from an unmanaged memory pointer directly into this buffer.
    /// Used by GPU interop to avoid extra float[] allocations.
    /// The source is assumed to be float RGBA interleaved, row-major, size = Width * Height * 4.
    /// </summary>
    public unsafe void CopyFrom(IntPtr source, int rowPitchBytes)
    {
        var srcRowBytes = rowPitchBytes;
        var destRowBytes = Width * 4 * sizeof(float);
        var copyBytes = Width * 4 * sizeof(float);
        fixed (float* pDest = _data)
        {
            byte* pDestBytes = (byte*)pDest;
            byte* pSrcBytes = (byte*)source;
            if (srcRowBytes == destRowBytes)
            {
                System.Buffer.MemoryCopy(pSrcBytes, pDestBytes, _data.Length * sizeof(float), _data.Length * sizeof(float));
            }
            else
            {
                for (int y = 0; y < Height; y++)
                    System.Buffer.MemoryCopy(pSrcBytes + y * srcRowBytes, pDestBytes + y * destRowBytes, copyBytes, copyBytes);
            }
        }
    }

    /// <summary>
    /// Copies float data from this buffer into an unmanaged memory destination.
    /// Used by GPU interop to avoid extra float[] allocations.
    /// </summary>
    public unsafe void CopyTo(IntPtr destination, int rowPitchBytes)
    {
        var srcRowBytes = Width * 4 * sizeof(float);
        var destRowBytes = rowPitchBytes;
        var copyBytes = Width * 4 * sizeof(float);
        fixed (float* pSrc = _data)
        {
            byte* pSrcBytes = (byte*)pSrc;
            byte* pDestBytes = (byte*)destination;
            if (srcRowBytes == destRowBytes)
            {
                System.Buffer.MemoryCopy(pSrcBytes, pDestBytes, _data.Length * sizeof(float), _data.Length * sizeof(float));
            }
            else
            {
                for (int y = 0; y < Height; y++)
                    System.Buffer.MemoryCopy(pSrcBytes + y * srcRowBytes, pDestBytes + y * destRowBytes, copyBytes, copyBytes);
            }
        }
    }

    /// <summary>
    /// Converts to a WPF Bgra32 BitmapSource for display.
    /// Clamps all values to [0,1] during conversion.
    /// </summary>
    public BitmapSource ToBitmapSource()
    {
        var pixels = new byte[Width * Height * 4];
        var height = Height;
        var width = Width;

        // Parallel.For overhead outweighs benefit for small buffers; use serial loop below threshold.
        if (width * height >= 128 * 128)
        {
            System.Threading.Tasks.Parallel.For(0, height, y =>
            {
                var rowBase = y * width * 4;
                var dataRowBase = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var di = dataRowBase + x * 4;
                    var pi = rowBase + x * 4;

                    var r = Math.Clamp(_data[di], 0f, 1f);
                    var g = Math.Clamp(_data[di + 1], 0f, 1f);
                    var b = Math.Clamp(_data[di + 2], 0f, 1f);
                    var a = Math.Clamp(_data[di + 3], 0f, 1f);

                    pixels[pi] = (byte)(b * 255f);
                    pixels[pi + 1] = (byte)(g * 255f);
                    pixels[pi + 2] = (byte)(r * 255f);
                    pixels[pi + 3] = (byte)(a * 255f);
                }
            });
        }
        else
        {
            for (var i = 0; i < width * height; i++)
            {
                var di = i * 4;
                var pi = i * 4;
                var r = Math.Clamp(_data[di], 0f, 1f);
                var g = Math.Clamp(_data[di + 1], 0f, 1f);
                var b = Math.Clamp(_data[di + 2], 0f, 1f);
                var a = Math.Clamp(_data[di + 3], 0f, 1f);
                pixels[pi] = (byte)(b * 255f);
                pixels[pi + 1] = (byte)(g * 255f);
                pixels[pi + 2] = (byte)(r * 255f);
                pixels[pi + 3] = (byte)(a * 255f);
            }
        }

        var bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), pixels, Width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Creates a PixelBuffer from a WPF BitmapSource.
    /// </summary>
    public static PixelBuffer FromBitmapSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var buffer = PixelBufferPool.Borrow(width, height);

        // Convert to Bgra32 if needed
        var converted = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var bytes = new byte[width * height * 4];
        converted.CopyPixels(bytes, width * 4, 0);

        var data = buffer._data;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            data[i] = bytes[i + 2] / 255f;     // R (from Bgra byte[2])
            data[i + 1] = bytes[i + 1] / 255f; // G (from Bgra byte[1])
            data[i + 2] = bytes[i] / 255f;     // B (from Bgra byte[0])
            data[i + 3] = bytes[i + 3] / 255f; // A (from Bgra byte[3])
        }

        return buffer;
    }

    /// <summary>
    /// Applies a per-pixel function that maps (R,G,B,A) -> (R,G,B,A).
    /// </summary>
    public void Apply(Func<float, float, float, float, (float R, float G, float B, float A)> transform)
    {
        var pixelCount = _data.Length / 4;
        System.Threading.Tasks.Parallel.For(0, pixelCount, p =>
        {
            var i = p * 4;
            var (r, g, b, a) = transform(_data[i], _data[i + 1], _data[i + 2], _data[i + 3]);
            _data[i] = r;
            _data[i + 1] = g;
            _data[i + 2] = b;
            _data[i + 3] = a;
        });
    }

    /// <summary>
    /// Linearly interpolates between this buffer and another, per pixel.
    /// </summary>
    public static PixelBuffer Lerp(PixelBuffer a, PixelBuffer b, float t)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException("Buffer dimensions must match.");

        var result = PixelBufferPool.Borrow(a.Width, a.Height);
        var ad = a._data;
        var bd = b._data;
        var rd = result._data;
        var invT = 1f - t;

        System.Threading.Tasks.Parallel.For(0, ad.Length, i =>
        {
            rd[i] = ad[i] * invT + bd[i] * t;
        });

        return result;
    }

    /// <summary>
    /// Linearly interpolates per pixel using a mask buffer (R channel as weight).
    /// </summary>
    public static PixelBuffer Lerp(PixelBuffer a, PixelBuffer b, PixelBuffer mask)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(mask);
        if (a.Width != b.Width || a.Height != b.Height || a.Width != mask.Width || a.Height != mask.Height)
            throw new ArgumentException("Buffer dimensions must match.");

        var result = PixelBufferPool.Borrow(a.Width, a.Height);
        var ad = a._data;
        var bd = b._data;
        var md = mask._data;
        var rd = result._data;

        var width = a.Width;
        var height = a.Height;
        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var pi = (y * width + x) * 4;
                var t = Math.Clamp(md[pi], 0f, 1f); // use R channel of mask
                var invT = 1f - t;
                rd[pi] = ad[pi] * invT + bd[pi] * t;
                rd[pi + 1] = ad[pi + 1] * invT + bd[pi + 1] * t;
                rd[pi + 2] = ad[pi + 2] * invT + bd[pi + 2] * t;
                rd[pi + 3] = ad[pi + 3] * invT + bd[pi + 3] * t;
            }
        });

        return result;
    }

    /// <summary>
    /// Legacy method for backward compatibility with Roslyn-compiled script nodes.
    /// Calls ForEachPixel(action, parallel: true).
    /// </summary>
    [Obsolete("Use ForEachPixel(action, parallel: true) instead.")]
    public void ParallelForEachPixel(Action<int, int> action) => ForEachPixel(action, parallel: true);
}
