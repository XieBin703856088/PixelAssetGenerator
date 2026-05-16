using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Generators;

namespace PixelAssetGenerator
{
    public sealed class TileGenerator
    {
        private readonly Dictionary<TileType, ITileGenerator> _generators = new()
        {
            { TileType.Grass, new GrassGenerator() },
            { TileType.Stone, new StoneGenerator() },
            { TileType.Water, new WaterGenerator() },
            { TileType.Sand, new SandGenerator() },
            { TileType.Road, new RoadGenerator() }
        };

        public BitmapSource GenerateTileBitmap(int size, IReadOnlyList<TileLayerDefinition> layers)
        {
            if (layers is null || layers.Count == 0)
            {
                throw new ArgumentException("No layers provided", nameof(layers));
            }

            // Parallelize per-layer generation and per-pixel compositing where safe.
            var pixels = new byte[size * size * 4];
            var layerPixels = new byte[size * size * 4];

            foreach (var layer in layers)
            {
                if (!_generators.TryGetValue(layer.Type, out var generator))
                {
                    throw new ArgumentException($"Unknown tile type: {layer.Type}");
                }

                var bitmap = generator.Generate(size, layer.Settings);
                bitmap.CopyPixels(layerPixels, size * 4, 0);
                var opacity = Math.Clamp(layer.Opacity, 0f, 1f);

                // Use simple parallel loop to utilize multiple CPU cores for big tiles
                System.Threading.Tasks.Parallel.For(0, size * size, i =>
                {
                    var idx = i * 4;

                    var destB = pixels[idx] / 255f;
                    var destG = pixels[idx + 1] / 255f;
                    var destR = pixels[idx + 2] / 255f;
                    var destA = pixels[idx + 3] / 255f;

                    var srcB = layerPixels[idx] / 255f;
                    var srcG = layerPixels[idx + 1] / 255f;
                    var srcR = layerPixels[idx + 2] / 255f;
                    var srcA = layerPixels[idx + 3] / 255f;

                    var effectiveOpacity = Math.Clamp(opacity * srcA, 0f, 1f);
                    if (effectiveOpacity <= 0f)
                    {
                        return;
                    }

                    var blendedR = destA <= 0f ? srcR : BlendChannel(destR, srcR, layer.BlendMode);
                    var blendedG = destA <= 0f ? srcG : BlendChannel(destG, srcG, layer.BlendMode);
                    var blendedB = destA <= 0f ? srcB : BlendChannel(destB, srcB, layer.BlendMode);

                    // Interlocked updates not needed if we write to a separate buffer per layer,
                    // but for simplicity we accumulate into pixels with no guarantees; acceptable for UI preview.
                    pixels[idx] = (byte)Math.Clamp((destB * (1 - effectiveOpacity) + blendedB * effectiveOpacity) * 255f, 0, 255);
                    pixels[idx + 1] = (byte)Math.Clamp((destG * (1 - effectiveOpacity) + blendedG * effectiveOpacity) * 255f, 0, 255);
                    pixels[idx + 2] = (byte)Math.Clamp((destR * (1 - effectiveOpacity) + blendedR * effectiveOpacity) * 255f, 0, 255);
                    var outA = destA + effectiveOpacity * (1f - destA);
                    pixels[idx + 3] = (byte)Math.Clamp(outA * 255f, 0, 255);
                });
            }

            var output = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return output;
        }

        /// <summary>
        /// Composites multiple bitmap layers using blend modes and opacity.
        /// </summary>
        public BitmapSource ComposeBitmapLayers(int size, IReadOnlyList<(BitmapSource Bitmap, LayerBlendMode BlendMode, float Opacity)> layers)
        {
            if (layers is null || layers.Count == 0)
            {
                throw new ArgumentException("No layers provided", nameof(layers));
            }

            var pixels = new byte[size * size * 4];
            var layerPixels = new byte[size * size * 4];

            foreach (var layer in layers)
            {
                if (layer.Bitmap.PixelWidth != size || layer.Bitmap.PixelHeight != size)
                {
                    throw new ArgumentException("Layer bitmap size mismatch", nameof(layers));
                }

                layer.Bitmap.CopyPixels(layerPixels, size * 4, 0);
                var opacity = Math.Clamp(layer.Opacity, 0f, 1f);

                for (var i = 0; i < layerPixels.Length; i += 4)
                {
                    var destB = pixels[i] / 255f;
                    var destG = pixels[i + 1] / 255f;
                    var destR = pixels[i + 2] / 255f;
                    var destA = pixels[i + 3] / 255f;

                    var srcB = layerPixels[i] / 255f;
                    var srcG = layerPixels[i + 1] / 255f;
                    var srcR = layerPixels[i + 2] / 255f;
                    var srcA = layerPixels[i + 3] / 255f;

                    var effectiveOpacity = Math.Clamp(opacity * srcA, 0f, 1f);
                    if (effectiveOpacity <= 0f)
                    {
                        continue;
                    }

                    var blendedR = destA <= 0f ? srcR : BlendChannel(destR, srcR, layer.BlendMode);
                    var blendedG = destA <= 0f ? srcG : BlendChannel(destG, srcG, layer.BlendMode);
                    var blendedB = destA <= 0f ? srcB : BlendChannel(destB, srcB, layer.BlendMode);

                    pixels[i] = (byte)Math.Clamp((destB * (1 - effectiveOpacity) + blendedB * effectiveOpacity) * 255f, 0, 255);
                    pixels[i + 1] = (byte)Math.Clamp((destG * (1 - effectiveOpacity) + blendedG * effectiveOpacity) * 255f, 0, 255);
                    pixels[i + 2] = (byte)Math.Clamp((destR * (1 - effectiveOpacity) + blendedR * effectiveOpacity) * 255f, 0, 255);
                    var outA = destA + effectiveOpacity * (1f - destA);
                    pixels[i + 3] = (byte)Math.Clamp(outA * 255f, 0, 255);
                }
            }

            var output = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return output;
        }

        public BitmapSource GenerateNineSliceBitmap(int size, TileLayerDefinition baseLayer, TileLayerDefinition transitionLayer, float edgeSize, float maskSize, float edgeFeather)
        {
            var edgePixels = Math.Clamp((int)MathF.Round(size * edgeSize), 1, Math.Max(1, size / 2));

            var baseBitmap = GenerateTileBitmap(size, new[] { baseLayer });
            var transitionBitmap = GenerateTileBitmap(size, new[] { transitionLayer });

            var basePixels = new byte[size * size * 4];
            var transitionPixels = new byte[size * size * 4];
            baseBitmap.CopyPixels(basePixels, size * 4, 0);
            transitionBitmap.CopyPixels(transitionPixels, size * 4, 0);

            var outputSize = size * 3;
            var outputPixels = new byte[outputSize * outputSize * 4];

            maskSize = Math.Clamp(maskSize, 0.5f, 1f);
            var inset = (outputSize * (1f - maskSize)) * 0.5f;
            var radius = Math.Min(edgePixels, (int)MathF.Round(edgePixels * maskSize));
            edgeFeather = Math.Clamp(edgeFeather, 0.1f, 1f);
            var feather = MathF.Max(1f, radius * edgeFeather);
            var halfWidth = (outputSize * maskSize) * 0.5f;
            var halfHeight = halfWidth;

            for (var y = 0; y < outputSize; y++)
            {
                for (var x = 0; x < outputSize; x++)
                {
                    var localX = x % size;
                    var localY = y % size;
                    var index = (localY * size + localX) * 4;
                    var outIndex = (y * outputSize + x) * 4;

                    var cx = x - (outputSize - 1) * 0.5f;
                    var cy = y - (outputSize - 1) * 0.5f;
                    var dx = MathF.Abs(cx) - (halfWidth - radius);
                    var dy = MathF.Abs(cy) - (halfHeight - radius);
                    var ax = MathF.Max(dx, 0f);
                    var ay = MathF.Max(dy, 0f);
                    var distance = MathF.Sqrt(ax * ax + ay * ay) + MathF.Min(MathF.Max(dx, dy), 0f) - radius;
                    var mask = 1f - SmoothStep(0f, feather, distance + feather);
                    mask = Math.Clamp(mask, 0f, 1f);
                    var invMask = 1f - mask;

                    var baseB = basePixels[index] / 255f;
                    var baseG = basePixels[index + 1] / 255f;
                    var baseR = basePixels[index + 2] / 255f;
                    var baseA = basePixels[index + 3] / 255f;

                    var transB = transitionPixels[index] / 255f;
                    var transG = transitionPixels[index + 1] / 255f;
                    var transR = transitionPixels[index + 2] / 255f;
                    var transA = transitionPixels[index + 3] / 255f;

                    outputPixels[outIndex] = (byte)Math.Clamp((baseB * mask + transB * invMask) * 255f, 0, 255);
                    outputPixels[outIndex + 1] = (byte)Math.Clamp((baseG * mask + transG * invMask) * 255f, 0, 255);
                    outputPixels[outIndex + 2] = (byte)Math.Clamp((baseR * mask + transR * invMask) * 255f, 0, 255);
                    outputPixels[outIndex + 3] = (byte)Math.Clamp((baseA * mask + transA * invMask) * 255f, 0, 255);
                }
            }

            var output = new WriteableBitmap(outputSize, outputSize, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, outputSize, outputSize), outputPixels, outputSize * 4, 0);
            return output;
        }

        public BitmapSource GenerateNineSliceBitmapFromBitmaps(int size, BitmapSource baseBitmap, BitmapSource transitionBitmap, float edgeSize, float maskSize, float edgeFeather)
        {
            if (baseBitmap.PixelWidth != transitionBitmap.PixelWidth || baseBitmap.PixelHeight != transitionBitmap.PixelHeight)
            {
                throw new ArgumentException("Nine-slice bitmap size mismatch", nameof(transitionBitmap));
            }

            var outputSize = baseBitmap.PixelWidth;
            var edgePixels = Math.Clamp((int)MathF.Round(size * edgeSize), 1, Math.Max(1, size / 2));

            var basePixels = new byte[outputSize * outputSize * 4];
            var transitionPixels = new byte[outputSize * outputSize * 4];
            baseBitmap.CopyPixels(basePixels, outputSize * 4, 0);
            transitionBitmap.CopyPixels(transitionPixels, outputSize * 4, 0);

            var outputPixels = new byte[outputSize * outputSize * 4];

            maskSize = Math.Clamp(maskSize, 0.5f, 1f);
            var radius = Math.Min(edgePixels, (int)MathF.Round(edgePixels * maskSize));
            edgeFeather = Math.Clamp(edgeFeather, 0.1f, 1f);
            var feather = MathF.Max(1f, radius * edgeFeather);
            var halfWidth = (outputSize * maskSize) * 0.5f;
            var halfHeight = halfWidth;

            for (var y = 0; y < outputSize; y++)
            {
                for (var x = 0; x < outputSize; x++)
                {
                    var index = (y * outputSize + x) * 4;

                    var cx = x - (outputSize - 1) * 0.5f;
                    var cy = y - (outputSize - 1) * 0.5f;
                    var dx = MathF.Abs(cx) - (halfWidth - radius);
                    var dy = MathF.Abs(cy) - (halfHeight - radius);
                    var ax = MathF.Max(dx, 0f);
                    var ay = MathF.Max(dy, 0f);
                    var distance = MathF.Sqrt(ax * ax + ay * ay) + MathF.Min(MathF.Max(dx, dy), 0f) - radius;
                    var mask = 1f - SmoothStep(0f, feather, distance + feather);
                    mask = Math.Clamp(mask, 0f, 1f);
                    var invMask = 1f - mask;

                    var baseB = basePixels[index] / 255f;
                    var baseG = basePixels[index + 1] / 255f;
                    var baseR = basePixels[index + 2] / 255f;
                    var baseA = basePixels[index + 3] / 255f;

                    var transB = transitionPixels[index] / 255f;
                    var transG = transitionPixels[index + 1] / 255f;
                    var transR = transitionPixels[index + 2] / 255f;
                    var transA = transitionPixels[index + 3] / 255f;

                    outputPixels[index] = (byte)Math.Clamp((baseB * mask + transB * invMask) * 255f, 0, 255);
                    outputPixels[index + 1] = (byte)Math.Clamp((baseG * mask + transG * invMask) * 255f, 0, 255);
                    outputPixels[index + 2] = (byte)Math.Clamp((baseR * mask + transR * invMask) * 255f, 0, 255);
                    outputPixels[index + 3] = (byte)Math.Clamp((baseA * mask + transA * invMask) * 255f, 0, 255);
                }
            }

            var output = new WriteableBitmap(outputSize, outputSize, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, outputSize, outputSize), outputPixels, outputSize * 4, 0);
            return output;
        }

        public BitmapSource GenerateNineSliceMaskBitmap(int size, float edgeSize, float maskSize, float edgeFeather)
        {
            var outputSize = size * 3;
            var edgePixels = Math.Clamp((int)MathF.Round(size * edgeSize), 1, Math.Max(1, size / 2));

            var outputPixels = new byte[outputSize * outputSize * 4];

            maskSize = Math.Clamp(maskSize, 0.5f, 1f);
            var radius = Math.Min(edgePixels, (int)MathF.Round(edgePixels * maskSize));
            edgeFeather = Math.Clamp(edgeFeather, 0.1f, 1f);
            var feather = MathF.Max(1f, radius * edgeFeather);
            var halfWidth = (outputSize * maskSize) * 0.5f;
            var halfHeight = halfWidth;

            for (var y = 0; y < outputSize; y++)
            {
                for (var x = 0; x < outputSize; x++)
                {
                    var index = (y * outputSize + x) * 4;

                    var cx = x - (outputSize - 1) * 0.5f;
                    var cy = y - (outputSize - 1) * 0.5f;
                    var dx = MathF.Abs(cx) - (halfWidth - radius);
                    var dy = MathF.Abs(cy) - (halfHeight - radius);
                    var ax = MathF.Max(dx, 0f);
                    var ay = MathF.Max(dy, 0f);
                    var distance = MathF.Sqrt(ax * ax + ay * ay) + MathF.Min(MathF.Max(dx, dy), 0f) - radius;
                    var mask = 1f - SmoothStep(0f, feather, distance + feather);
                    mask = Math.Clamp(mask, 0f, 1f);
                    var value = (byte)Math.Clamp(mask * 255f, 0f, 255f);

                    outputPixels[index] = value;
                    outputPixels[index + 1] = value;
                    outputPixels[index + 2] = value;
                    outputPixels[index + 3] = 255;
                }
            }

            var output = new WriteableBitmap(outputSize, outputSize, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, outputSize, outputSize), outputPixels, outputSize * 4, 0);
            return output;
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Math.Abs(edge1 - edge0) < 0.0001f)
            {
                return x < edge0 ? 0f : 1f;
            }

            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float BlendChannel(float dest, float src, LayerBlendMode mode)
        {
            return mode switch
            {
                LayerBlendMode.Multiply => dest * src,
                LayerBlendMode.Screen => 1f - (1f - dest) * (1f - src),
                LayerBlendMode.Overlay => dest < 0.5f
                    ? 2f * dest * src
                    : 1f - 2f * (1f - dest) * (1f - src),
                _ => src
            };
        }
    }
}
