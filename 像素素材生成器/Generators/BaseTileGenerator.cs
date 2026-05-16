using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using SharpNoise.Modules;

namespace PixelAssetGenerator.Generators
{
    public abstract class BaseTileGenerator : ITileGenerator
    {
        private static readonly ConcurrentDictionary<NoiseKey, Module> NoiseCache = new();

        public BitmapSource Generate(int size, TileLayerSettings settings)
        {
            var pixels = new byte[size * size * 4];
            var detailSettings = GetDetailSettings(settings);
            var edgeStrength = Math.Clamp(settings.EdgeStrength, 0f, 1f);

            Parallel.For(0, size, y =>
            {
                for (var x = 0; x < size; x++)
                {
                    var color = GeneratePixel(x, y, size, settings);
                    color = ApplyDetailNoise(color, x, y, size, detailSettings);

                    if (edgeStrength > 0f && IsEdgePixel(x, y, size))
                    {
                        var edgeColor = Lerp(color, Colors.Black, edgeStrength);
                        color = Color.FromArgb(color.A, edgeColor.R, edgeColor.G, edgeColor.B);
                    }



                    var index = (y * size + x) * 4;
                    pixels[index] = color.B;
                    pixels[index + 1] = color.G;
                    pixels[index + 2] = color.R;
                    pixels[index + 3] = color.A;
                }
            });

            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return bitmap;
        }

        protected abstract Color GeneratePixel(int x, int y, int size, TileLayerSettings settings);

        protected virtual TileLayerSettings GetDetailSettings(TileLayerSettings settings)
        {
            return settings;
        }

        protected static bool IsEdgePixel(int x, int y, int size)
        {
            return x == 0 || y == 0 || x == size - 1 || y == size - 1;
        }

        protected static float FractalNoise(float x, float y, int octaves, float persistence, float lacunarity, int seed)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var value = 0f;
            var max = 0f;

            for (var o = 0; o < octaves; o++)
            {
                value += ValueNoise(x * frequency, y * frequency, seed + o * 1013) * amplitude;
                max += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return max == 0f ? 0f : value / max;
        }

        protected static float TileableModuleNoise(Module module, float x, float y, int size, float scale = 1f)
        {
            var u = (x / size) * MathF.Tau;
            var v = (y / size) * MathF.Tau;
            const float radius = 2f;
            var cosV = MathF.Cos(v);
            var sinV = MathF.Sin(v);
            var cosU = MathF.Cos(u);
            var sinU = MathF.Sin(u);

            var nx = (radius + cosV) * cosU * scale;
            var ny = (radius + cosV) * sinU * scale;
            var nz = sinV * scale;

            var value = module.GetValue(nx, ny, nz);
            return (float)(value * 0.5 + 0.5);
        }

        protected static float TileableFractalNoise(float x, float y, int period, int octaves, float persistence, float lacunarity, int seed)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var value = 0f;
            var max = 0f;

            for (var o = 0; o < octaves; o++)
            {
                var octavePeriod = Math.Max(1, (int)MathF.Round(period * frequency));
                value += TileableValueNoise(x * frequency, y * frequency, octavePeriod, seed + o * 1013) * amplitude;
                max += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return max == 0f ? 0f : value / max;
        }

        protected static float ValueNoise(float x, float y, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var x1 = x0 + 1;
            var y1 = y0 + 1;

            var sx = SmoothStep(x - x0);
            var sy = SmoothStep(y - y0);

            var n00 = HashToUnit(x0, y0, seed);
            var n10 = HashToUnit(x1, y0, seed);
            var n01 = HashToUnit(x0, y1, seed);
            var n11 = HashToUnit(x1, y1, seed);

            var ix0 = Lerp(n00, n10, sx);
            var ix1 = Lerp(n01, n11, sx);
            return Lerp(ix0, ix1, sy);
        }

        protected static float TileableGradientNoise(float x, float y, int period, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var x1 = x0 + 1;
            var y1 = y0 + 1;

            var dx = x - x0;
            var dy = y - y0;
            var dx1 = dx - 1f;
            var dy1 = dy - 1f;

            var px0 = Mod(x0, period);
            var py0 = Mod(y0, period);
            var px1 = Mod(x1, period);
            var py1 = Mod(y1, period);

            var g00 = Gradient(px0, py0, seed);
            var g10 = Gradient(px1, py0, seed);
            var g01 = Gradient(px0, py1, seed);
            var g11 = Gradient(px1, py1, seed);

            var n00 = g00.x * dx + g00.y * dy;
            var n10 = g10.x * dx1 + g10.y * dy;
            var n01 = g01.x * dx + g01.y * dy1;
            var n11 = g11.x * dx1 + g11.y * dy1;

            var sx = SmoothStep(dx);
            var sy = SmoothStep(dy);

            var ix0 = Lerp(n00, n10, sx);
            var ix1 = Lerp(n01, n11, sx);
            var value = Lerp(ix0, ix1, sy);
            return value * 0.5f + 0.5f;
        }

        protected static void TileableVoronoi(float x, float y, int cellSize, int periodInCells, int seed,
            out float nearest, out float secondNearest, out int cellX, out int cellY)
        {
            var cellX0 = (int)MathF.Floor(x / cellSize);
            var cellY0 = (int)MathF.Floor(y / cellSize);
            var periodSize = Math.Max(1, periodInCells * cellSize);

            nearest = float.MaxValue;
            secondNearest = float.MaxValue;
            cellX = 0;
            cellY = 0;

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var cx = cellX0 + dx;
                    var cy = cellY0 + dy;
                    var wrappedX = Mod(cx, periodInCells);
                    var wrappedY = Mod(cy, periodInCells);

                    var jitterX = HashToUnit(wrappedX, wrappedY, seed) - 0.5f;
                    var jitterY = HashToUnit(wrappedX, wrappedY, seed + 19) - 0.5f;

                    var pointX = (wrappedX + 0.5f + jitterX) * cellSize;
                    var pointY = (wrappedY + 0.5f + jitterY) * cellSize;

                    var deltaX = pointX - x;
                    var deltaY = pointY - y;
                    deltaX -= MathF.Round(deltaX / periodSize) * periodSize;
                    deltaY -= MathF.Round(deltaY / periodSize) * periodSize;

                    var distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    if (distance < nearest)
                    {
                        secondNearest = nearest;
                        nearest = distance;
                        cellX = wrappedX;
                        cellY = wrappedY;
                    }
                    else if (distance < secondNearest)
                    {
                        secondNearest = distance;
                    }
                }
            }

            var normalizer = Math.Max(1f, cellSize);
            nearest = Math.Clamp(nearest / normalizer, 0f, 1f);
            secondNearest = Math.Clamp(secondNearest / normalizer, 0f, 1f);
        }

        // Float-based Voronoi that supports non-integer cell sizes and smoother transitions when changing tile resolution.
        // Returns normalized nearest distances in range [0,1] where distances are divided by max(1, cellSizeF).
        protected static void TileableVoronoiFloat(float x, float y, float cellSizeF, float periodInCellsF, int seed,
            out float nearest, out float secondNearest, out int cellX, out int cellY)
        {
            // Fallback to integer Voronoi if cellSizeF is very small
            if (cellSizeF <= 0.5f)
            {
                TileableVoronoi(x, y, 1, Math.Max(1, (int)MathF.Round(periodInCellsF)), seed, out nearest, out secondNearest, out cellX, out cellY);
                return;
            }

            nearest = float.MaxValue;
            secondNearest = float.MaxValue;
            cellX = 0;
            cellY = 0;

            // period in float and period size in pixels
            var periodSize = periodInCellsF * cellSizeF;

            // compute integer cell coords around the sample point
            var cellX0 = (int)MathF.Floor(x / cellSizeF);
            var cellY0 = (int)MathF.Floor(y / cellSizeF);

            // for hashing/wrapping purposes use an integer period count
            var periodCells = Math.Max(1, (int)MathF.Round(periodInCellsF));

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var cx = cellX0 + dx;
                    var cy = cellY0 + dy;

                    // wrapped indices for deterministic jitter (used by hash)
                    var wrappedIx = Mod(cx, periodCells);
                    var wrappedIy = Mod(cy, periodCells);

                    var jitterX = HashToUnit(wrappedIx, wrappedIy, seed) - 0.5f;
                    var jitterY = HashToUnit(wrappedIx, wrappedIy, seed + 19) - 0.5f;

                    // feature point in pixel space
                    var pointX = (cx + 0.5f + jitterX) * cellSizeF;
                    var pointY = (cy + 0.5f + jitterY) * cellSizeF;

                    // wrap positions to maintain tileability on a float period size
                    var dxp = pointX - x;
                    var dyp = pointY - y;
                    dxp -= MathF.Round(dxp / periodSize) * periodSize;
                    dyp -= MathF.Round(dyp / periodSize) * periodSize;

                    var distance = MathF.Sqrt(dxp * dxp + dyp * dyp);
                    if (distance < nearest)
                    {
                        secondNearest = nearest;
                        nearest = distance;
                        cellX = wrappedIx;
                        cellY = wrappedIy;
                    }
                    else if (distance < secondNearest)
                    {
                        secondNearest = distance;
                    }
                }
            }

            var normalizer = MathF.Max(1f, cellSizeF);
            nearest = Math.Clamp(nearest / normalizer, 0f, 1f);
            secondNearest = Math.Clamp(secondNearest / normalizer, 0f, 1f);
        }

        protected static float TileableValueNoise(float x, float y, int period, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var x1 = x0 + 1;
            var y1 = y0 + 1;

            var sx = SmoothStep(x - x0);
            var sy = SmoothStep(y - y0);

            var px0 = Mod(x0, period);
            var py0 = Mod(y0, period);
            var px1 = Mod(x1, period);
            var py1 = Mod(y1, period);

            var n00 = HashToUnit(px0, py0, seed);
            var n10 = HashToUnit(px1, py0, seed);
            var n01 = HashToUnit(px0, py1, seed);
            var n11 = HashToUnit(px1, py1, seed);

            var ix0 = Lerp(n00, n10, sx);
            var ix1 = Lerp(n01, n11, sx);
            return Lerp(ix0, ix1, sy);
        }

        protected static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        protected static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Math.Abs(edge1 - edge0) < 0.0001f)
            {
                return x < edge0 ? 0f : 1f;
            }

            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        protected static int Mod(int value, int modulus)
        {
            if (modulus <= 0)
            {
                return 0;
            }

            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        protected static byte ResolveMaskAlpha(TileLayerSettings settings, float primaryMask, float detailMask, float accentMask)
        {
            if (!settings.MaskEnabled)
            {
                return 255;
            }

            var mask = settings.MaskElement switch
            {
                MaskElement.Detail => detailMask,
                MaskElement.Accent => accentMask,
                _ => primaryMask
            };

            mask = Math.Clamp(mask, 0f, 1f);
            if (settings.MaskInvert)
            {
                mask = 1f - mask;
            }

            return (byte)MathF.Round(mask * 255f);
        }

        protected static Color ApplyErosion(TileLayerSettings settings, Color color, float mask, int x, int y, int size)
        {
            if (!settings.MaskEnabled)
            {
                return color;
            }

            var strength = Math.Clamp(settings.ErosionStrength, 0f, 1f);
            if (strength <= 0f)
            {
                return color;
            }

            var scale = MathF.Max(0.1f, settings.ErosionScale);
            var period = Math.Max(1, (int)MathF.Round(size * scale));
            var erosionNoise = TileableFractalNoise(x * scale, y * scale, period, 3, 0.5f, 2f, settings.Seed + 801);
            var threshold = 1f - strength;
            var erosionMask = Math.Clamp((erosionNoise - threshold) / MathF.Max(0.0001f, strength), 0f, 1f);
            var erodedMask = Math.Clamp(mask * (1f - erosionMask), 0f, 1f);
            var erodedColor = Lerp(Color.FromArgb(255, color.R, color.G, color.B), Colors.Black, erosionMask * 0.35f);

            return Color.FromArgb((byte)MathF.Round(erodedMask * 255f), erodedColor.R, erodedColor.G, erodedColor.B);
        }

        private static Color ApplyDetailNoise(Color color, int x, int y, int size, TileLayerSettings settings)
        {
            var detailDensity = Math.Clamp(settings.DetailDensity, 0f, 1f);
            var macroStrength = Math.Clamp(settings.MacroStrength, 0f, 1f);
            var microStrength = Math.Clamp(settings.MicroStrength, 0f, 1f);
            var accentDensity = Math.Clamp(settings.AccentDensity, 0f, 1f);
            var accentSize = Math.Clamp(settings.AccentSize, 0f, 1f);

            if (detailDensity <= 0f && macroStrength <= 0f && microStrength <= 0f && accentDensity <= 0f)
            {
                return color;
            }

            if (detailDensity > 0f && (macroStrength > 0f || microStrength > 0f))
            {
                var macro = 0.5f;
                if (macroStrength > 0f)
                {
                    var macroPerlin = GetPerlin(settings.MacroScale * 1.15f, 2, 0.6f, 2f, settings.Seed + 401);
                    macro = TileableModuleNoise(macroPerlin, x, y, size);
                }

                var micro = 0.5f;
                if (microStrength > 0f)
                {
                    var microSimplex = GetSimplex(settings.MicroScale * 1.35f, settings.Seed + 433);
                    micro = TileableModuleNoise(microSimplex, x, y, size);
                }

                var combined = ((macro - 0.5f) * (0.6f * macroStrength)) + ((micro - 0.5f) * (0.4f * microStrength));
                if (Math.Abs(combined) > 0.0001f)
                {
                    color = AdjustColor(color, combined * detailDensity * 0.35f);
                }
            }

            if (accentDensity > 0f)
            {
                var accentScale = MathF.Max(0.5f, settings.MicroScale) * (1.1f + accentSize * 1.4f);
                var accentSimplex = GetSimplex(accentScale, settings.Seed + 560);
                var accentNoise = TileableModuleNoise(accentSimplex, x, y, size);
                var threshold = 1f - accentDensity * (0.35f + accentSize * 0.45f);
                if (accentNoise > threshold)
                {
                    color = AdjustColor(color, 0.12f + accentSize * 0.18f);
                }
            }

            return color;
        }

        private static Color AdjustColor(Color color, float delta)
        {
            var shift = delta * 255f;
            var r = (byte)Math.Clamp(color.R + shift, 0f, 255f);
            var g = (byte)Math.Clamp(color.G + shift, 0f, 255f);
            var b = (byte)Math.Clamp(color.B + shift, 0f, 255f);
            return Color.FromArgb(color.A, r, g, b);
        }

        protected static Perlin GetPerlin(float frequency, int octaves, float persistence, float lacunarity, int seed)
        {
            var key = new NoiseKey(typeof(Perlin), seed, frequency, octaves, persistence, lacunarity);
            return (Perlin)NoiseCache.GetOrAdd(key, _ => new Perlin
            {
                Frequency = frequency,
                OctaveCount = octaves,
                Persistence = persistence,
                Lacunarity = lacunarity,
                Seed = seed
            });
        }

        protected static Simplex GetSimplex(float frequency, int seed)
        {
            var key = new NoiseKey(typeof(Simplex), seed, frequency, 1, 0f, 0f);
            return (Simplex)NoiseCache.GetOrAdd(key, _ => new Simplex
            {
                Frequency = frequency
            });
        }

        private readonly record struct NoiseKey(Type ModuleType, int Seed, float Frequency, int Octaves, float Persistence, float Lacunarity);

        private static (float x, float y) Gradient(int x, int y, int seed)
        {
            var angle = HashToUnit(x, y, seed) * MathF.Tau;
            return (MathF.Cos(angle), MathF.Sin(angle));
        }

        protected static float HashToUnit(int x, int y, int seed)
        {
            unchecked
            {
                var hash = x * 374761393 + y * 668265263 + seed * 982451653;
                hash = (hash ^ (hash >> 13)) * 1274126177;
                hash ^= hash >> 16;
                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        protected static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        protected static float TileableDirectionalBands(float phaseX, float phaseY, float direction, int harmonicCount, float phaseOffset = 0f)
        {
            var wrappedDirection = direction - MathF.Floor(direction);
            var clampedHarmonics = Math.Max(1, harmonicCount);
            var position = wrappedDirection * 4f;
            var sector = Math.Min(3, (int)MathF.Floor(position));
            var blend = SmoothStep(position - sector);

            var horizontal = 0.5f + 0.5f * MathF.Sin(phaseX * clampedHarmonics + phaseOffset);
            var diagonalDown = 0.5f + 0.5f * MathF.Sin((phaseX + phaseY) * clampedHarmonics + phaseOffset);
            var vertical = 0.5f + 0.5f * MathF.Sin(phaseY * clampedHarmonics + phaseOffset);
            var diagonalUp = 0.5f + 0.5f * MathF.Sin((phaseX - phaseY) * clampedHarmonics + phaseOffset);

            return sector switch
            {
                0 => Lerp(horizontal, diagonalDown, blend),
                1 => Lerp(diagonalDown, vertical, blend),
                2 => Lerp(vertical, diagonalUp, blend),
                _ => Lerp(diagonalUp, horizontal, blend)
            };
        }

        protected static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }
    }
}
