using System;
using HPPH;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services.AiImage;

internal static class HpphPixelBufferAdapter
{
    public static Image<ColorRGB> ToImage(PixelBuffer source, int width, int height, bool monochrome = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, y * source.Height / height);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, x * source.Width / width);
                var (r, g, b, a) = source.GetPixel(sourceX, sourceY);
                var offset = (y * width + x) * 3;
                if (monochrome)
                {
                    var value = Math.Clamp(0.2126f * r + 0.7152f * g + 0.0722f * b, 0f, 1f);
                    if (a < 0.999f) value *= Math.Clamp(a, 0f, 1f);
                    var channel = (byte)Math.Round(value * 255f);
                    bytes[offset] = channel;
                    bytes[offset + 1] = channel;
                    bytes[offset + 2] = channel;
                }
                else
                {
                    // Diffusion checkpoints are RGB-only. Composite transparent source pixels
                    // onto a neutral background so alpha does not turn into an accidental black halo.
                    var alpha = Math.Clamp(a, 0f, 1f);
                    const float matte = 0.5f;
                    bytes[offset] = ToByte(r * alpha + matte * (1f - alpha));
                    bytes[offset + 1] = ToByte(g * alpha + matte * (1f - alpha));
                    bytes[offset + 2] = ToByte(b * alpha + matte * (1f - alpha));
                }
            }
        }

        return Image<ColorRGB>.Create(bytes, width, height, width * 3);
    }

    public static PixelBuffer ToPixelBuffer(Image<ColorRGB> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bytes = new byte[image.SizeInBytes];
        image.CopyTo(bytes.AsSpan());
        var result = PixelBufferPool.Borrow(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 3;
                result.SetPixel(x, y,
                    bytes[offset] / 255f,
                    bytes[offset + 1] / 255f,
                    bytes[offset + 2] / 255f,
                    1f);
            }
        }
        return result;
    }

    private static byte ToByte(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
