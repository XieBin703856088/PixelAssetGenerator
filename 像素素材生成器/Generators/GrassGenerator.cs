using System;
using System.Windows.Media;

namespace PixelAssetGenerator.Generators
{
    public class GrassGenerator : BaseTileGenerator
    {
        private readonly record struct GrassStyle(
            Color Shadow,
            Color Dark,
            Color Mid,
            Color Light,
            Color Highlight,
            float BaseTone,
            Color CoolTint,
            Color WarmTint,
            Color LushTint,
            float MacroFrequency,
            float MeadowFrequency,
            float ColorRegionFrequency,
            float AccentRegionFrequency,
            float PatchBlend,
            float CheckerStrength,
            float HighlightMix,
            float ShadowMix,
            float FlowerMultiplier,
            float WarmStrength,
            float CoolStrength,
            float LushStrength);

        protected override TileLayerSettings GetDetailSettings(TileLayerSettings settings)
        {
            var microFactor = settings.GrassPreset == GrassPreset.ForestGrass ? 0.14f : 0.1f;
            return settings with
            {
                DetailDensity = settings.DetailDensity * 0.18f,
                MacroStrength = settings.MacroStrength * 0.22f,
                MicroStrength = settings.MicroStrength * microFactor,
                AccentDensity = 0f
            };
        }

        protected override Color GeneratePixel(int x, int y, int size, TileLayerSettings settings)
        {
            var seed = settings.Seed;
            var style = GetStyle(settings.GrassPreset);
            float variation = Math.Clamp(settings.ColorVariation, 0f, 1f);
            float patchiness = Math.Clamp(settings.GrassPatchiness, 0f, 1f);
            var variationBlend = 0.04f + variation * 0.96f;

            var macro = TileableModuleNoise(GetPerlin(style.MacroFrequency, 2, 0.55f, 2f, seed), x, y, size);
            var meadow = TileableModuleNoise(GetPerlin(style.MeadowFrequency, 1, 0.5f, 2f, seed + 17), x, y, size);
            var patchCellSize = Math.Max(2, size / 8);
            var patchPeriod = Math.Max(1, size / patchCellSize);
            var patch = TileableValueNoise((float)x / patchCellSize, (float)y / patchCellSize, patchPeriod, seed + 31);
            var broadColorRegion = TileableModuleNoise(GetPerlin(style.ColorRegionFrequency * 0.58f, 2, 0.56f, 1.9f, seed + 97), x, y, size);
            var mediumColorRegion = TileableModuleNoise(GetPerlin(style.ColorRegionFrequency, 2, 0.52f, 2f, seed + 109), x, y, size);
            var colorRegion = Lerp(broadColorRegion, mediumColorRegion, 0.3f + variation * 0.18f);
            var accentRegion = Lerp(
                colorRegion,
                TileableModuleNoise(GetPerlin(style.AccentRegionFrequency, 2, 0.58f, 2.2f, seed + 131), x, y, size),
                0.26f + variation * 0.16f);
            var checker = ((x + y) & 1) == 0 ? -style.CheckerStrength : style.CheckerStrength;

            var tone = macro * 0.5f + meadow * 0.22f + patch * (0.28f + variation * 0.08f);
            tone = Lerp(tone, patch, (style.PatchBlend + patchiness * 0.18f + variation * 0.05f) * variationBlend);

            if (settings.GrassPreset == GrassPreset.ForestGrass)
            {
                var canopy = TileableModuleNoise(GetPerlin(1.15f, 2, 0.55f, 2f, seed + 101), x, y, size);
                tone = Lerp(tone, canopy * 0.68f, 0.28f * variationBlend);
            }
            else if (settings.GrassPreset == GrassPreset.GrassB)
            {
                tone = Lerp(tone, patch, 0.08f * variationBlend);
            }

            tone = Lerp(style.BaseTone, tone, variationBlend);
            tone = Math.Clamp(tone + checker * (0.18f + variation * 0.46f), 0f, 1f);

            var color = SelectBaseColor(tone, style);
            color = ApplyColorVariation(color, tone, colorRegion, accentRegion, variation, style);
            color = ApplyBladeTufts(color, x, y, size, settings, style);
            color = ApplyFlowers(color, x, y, size, settings, style);

            return color;
        }

        private static GrassStyle GetStyle(GrassPreset preset)
        {
            return preset switch
            {
                GrassPreset.GrassB => new GrassStyle(
                    Shadow: Color.FromRgb(60, 102, 52),
                    Dark: Color.FromRgb(88, 132, 66),
                    Mid: Color.FromRgb(118, 158, 82),
                    Light: Color.FromRgb(154, 186, 106),
                    Highlight: Color.FromRgb(202, 220, 146),
                    BaseTone: 0.62f,
                    CoolTint: Color.FromRgb(82, 138, 96),
                    WarmTint: Color.FromRgb(172, 164, 92),
                    LushTint: Color.FromRgb(114, 176, 92),
                    MacroFrequency: 1.1f,
                    MeadowFrequency: 1.75f,
                    ColorRegionFrequency: 0.72f,
                    AccentRegionFrequency: 1.55f,
                    PatchBlend: 0.24f,
                    CheckerStrength: 0.012f,
                    HighlightMix: 0.44f,
                    ShadowMix: 0.18f,
                    FlowerMultiplier: 0.55f,
                    WarmStrength: 0.28f,
                    CoolStrength: 0.22f,
                    LushStrength: 0.24f),
                GrassPreset.ForestGrass => new GrassStyle(
                    Shadow: Color.FromRgb(30, 54, 36),
                    Dark: Color.FromRgb(42, 76, 46),
                    Mid: Color.FromRgb(58, 106, 62),
                    Light: Color.FromRgb(82, 138, 82),
                    Highlight: Color.FromRgb(118, 176, 108),
                    BaseTone: 0.56f,
                    CoolTint: Color.FromRgb(42, 88, 66),
                    WarmTint: Color.FromRgb(90, 124, 72),
                    LushTint: Color.FromRgb(64, 132, 74),
                    MacroFrequency: 1.35f,
                    MeadowFrequency: 2.45f,
                    ColorRegionFrequency: 0.8f,
                    AccentRegionFrequency: 1.9f,
                    PatchBlend: 0.28f,
                    CheckerStrength: 0.009f,
                    HighlightMix: 0.38f,
                    ShadowMix: 0.24f,
                    FlowerMultiplier: 0.15f,
                    WarmStrength: 0.16f,
                    CoolStrength: 0.24f,
                    LushStrength: 0.2f),
                _ => new GrassStyle(
                    Shadow: Color.FromRgb(40, 94, 52),
                    Dark: Color.FromRgb(64, 126, 66),
                    Mid: Color.FromRgb(92, 160, 82),
                    Light: Color.FromRgb(132, 194, 108),
                    Highlight: Color.FromRgb(188, 228, 144),
                    BaseTone: 0.68f,
                    CoolTint: Color.FromRgb(70, 142, 94),
                    WarmTint: Color.FromRgb(176, 170, 96),
                    LushTint: Color.FromRgb(96, 188, 88),
                    MacroFrequency: 1.05f,
                    MeadowFrequency: 1.95f,
                    ColorRegionFrequency: 0.68f,
                    AccentRegionFrequency: 1.48f,
                    PatchBlend: 0.2f,
                    CheckerStrength: 0.014f,
                    HighlightMix: 0.48f,
                    ShadowMix: 0.16f,
                    FlowerMultiplier: 0.85f,
                    WarmStrength: 0.26f,
                    CoolStrength: 0.18f,
                    LushStrength: 0.28f)
            };
        }

        private static Color SelectBaseColor(float tone, GrassStyle style)
        {
            if (tone < 0.24f)
            {
                return Lerp(style.Shadow, style.Dark, SmoothStep(0.02f, 0.24f, tone));
            }

            if (tone < 0.5f)
            {
                return Lerp(style.Dark, style.Mid, SmoothStep(0.24f, 0.5f, tone));
            }

            if (tone < 0.78f)
            {
                return Lerp(style.Mid, style.Light, SmoothStep(0.5f, 0.78f, tone));
            }

            if (tone < 0.92f)
            {
                return Lerp(style.Light, style.Highlight, SmoothStep(0.78f, 0.92f, tone));
            }

            return Lerp(style.Light, style.Highlight, SmoothStep(0.92f, 1f, tone));
        }

        private static Color ApplyColorVariation(Color color, float tone, float colorRegion, float accentRegion, float variation, GrassStyle style)
        {
            if (variation <= 0.01f)
            {
                return color;
            }

            var variationStrength = variation * variation;
            var warmBias = Math.Clamp((colorRegion - 0.5f) * 1.7f + (accentRegion - 0.5f) * 0.35f, -1f, 1f);
            var coolBias = Math.Clamp((0.5f - colorRegion) * 1.55f + (0.58f - accentRegion) * 0.22f, -1f, 1f);
            var lushBias = Math.Clamp((accentRegion - 0.5f) * 1.6f - warmBias * 0.22f, -1f, 1f);

            var warmMask = SmoothStep(0.1f, 0.78f, warmBias);
            var coolMask = SmoothStep(0.12f, 0.8f, coolBias);
            var lushMask = SmoothStep(0.08f, 0.74f, lushBias) * (1f - tone * 0.24f);

            var warmBlend = variationStrength * style.WarmStrength * warmMask * (0.55f + (1f - tone) * 0.45f);
            var coolBlend = variationStrength * style.CoolStrength * coolMask * (0.45f + tone * 0.35f);
            var lushBlend = variation * style.LushStrength * lushMask * (0.4f + tone * 0.35f);

            var colorized = Lerp(color, style.WarmTint, warmBlend);
            colorized = Lerp(colorized, style.CoolTint, coolBlend * (1f - warmBlend * 0.45f));
            colorized = Lerp(colorized, style.LushTint, lushBlend * (1f - warmBlend * 0.35f));

            var localContrast = (accentRegion - 0.5f) * variation * 0.04f;
            if (Math.Abs(localContrast) > 0.0001f)
            {
                colorized = localContrast > 0f
                    ? Lerp(colorized, style.Highlight, localContrast)
                    : Lerp(colorized, style.Shadow, -localContrast);
            }

            return colorized;
        }

        private static Color ApplyBladeTufts(Color color, int x, int y, int size, TileLayerSettings settings, GrassStyle style)
        {
            var density = Math.Clamp(settings.GrassBladeDensity, 0f, 1f);
            if (density <= 0f)
            {
                return color;
            }

            var patchiness = Math.Clamp(settings.GrassPatchiness, 0f, 1f);
            var cellSize = Math.Max(2, size / 8);
            var cellPeriod = Math.Max(1, size / cellSize);
            var cellX = x / cellSize;
            var cellY = y / cellSize;
            var localX = x % cellSize;
            var localY = y % cellSize;
            var wrappedX = Mod(cellX, cellPeriod);
            var wrappedY = Mod(cellY, cellPeriod);

            var tuftRoll = HashToUnit(wrappedX, wrappedY, settings.Seed + 43);
            var tuftThreshold = 0.34f + (1f - density) * 0.26f - patchiness * 0.14f;
            if (tuftRoll < tuftThreshold)
            {
                return color;
            }

            var mainColumn = Math.Min(cellSize - 1, (int)(HashToUnit(wrappedX, wrappedY, settings.Seed + 47) * cellSize));
            var secondaryColumn = Mod(mainColumn + (HashToUnit(wrappedX, wrappedY, settings.Seed + 53) > 0.5f ? 1 : -1), cellSize);
            var tipColumn = Mod(mainColumn + (HashToUnit(wrappedX, wrappedY, settings.Seed + 57) > 0.5f ? 1 : -1), cellSize);
            var heightBias = 0.35f + Math.Clamp(settings.GrassBladeHeight, 0f, 1f) * 0.45f + HashToUnit(wrappedX, wrappedY, settings.Seed + 59) * 0.1f;
            var bladeHeight = Math.Clamp(1 + (int)MathF.Round((cellSize - 1) * heightBias), 1, cellSize);
            var topY = cellSize - bladeHeight;

            if (localY < topY)
            {
                return color;
            }

            if (localY == topY && localX == tipColumn)
            {
                return Lerp(color, style.Highlight, style.HighlightMix + 0.08f);
            }

            if (localX == mainColumn)
            {
                var tipY = topY + Math.Max(1, bladeHeight / 3);
                return localY <= tipY
                    ? Lerp(color, style.Highlight, style.HighlightMix)
                    : Lerp(color, style.Light, 0.32f);
            }

            if (density > 0.55f && localX == secondaryColumn && localY > topY)
            {
                return Lerp(color, style.Dark, style.ShadowMix);
            }

            if (localY == cellSize - 1 && Math.Abs(localX - mainColumn) <= 1)
            {
                return Lerp(color, style.Shadow, style.ShadowMix * 0.72f);
            }

            return color;
        }

        private static Color ApplyFlowers(Color color, int x, int y, int size, TileLayerSettings settings, GrassStyle style)
        {
            var density = Math.Clamp(settings.FlowerDensity * style.FlowerMultiplier, 0f, 1f);
            if (density <= 0f)
            {
                return color;
            }

            var cellSize = Math.Max(8, size / 4);
            var cellPeriod = Math.Max(1, size / cellSize);
            var cellX = x / cellSize;
            var cellY = y / cellSize;

            var flowerSize = Math.Max(0f, settings.FlowerSize);
            int petalRadius;

            if (flowerSize < 0.1f)
            {
                petalRadius = 0;
            }
            else if (flowerSize < 1f)
            {
                var t = (flowerSize - 0.1f) / 0.9f;
                var defaultRadius = cellSize >= 12 ? 2 : 1;
                petalRadius = t >= 0.6f ? defaultRadius : (t >= 0.2f ? 1 : 0);
            }
            else
            {
                var scale = flowerSize;
                if (scale >= 8f)
                    petalRadius = 12;
                else if (scale >= 6f)
                    petalRadius = 10;
                else if (scale >= 5f)
                    petalRadius = 8;
                else if (scale >= 4f)
                    petalRadius = 6;
                else if (scale >= 3f)
                    petalRadius = 5;
                else if (scale >= 2f)
                    petalRadius = 4;
                else if (scale >= 1.5f)
                    petalRadius = 3;
                else
                    petalRadius = cellSize >= 12 ? 2 : 1;
            }

            if (petalRadius <= 0)
            {
                return color;
            }

            var brightnessBoost = flowerSize >= 1f
                ? Math.Min(flowerSize / 10f, 0.4f)
                : (flowerSize * 0.28f);

            var reach = (petalRadius + cellSize - 1) / cellSize;

            for (var ndy = -reach; ndy <= reach; ndy++)
            {
                for (var ndx = -reach; ndx <= reach; ndx++)
                {
                    var ncx = cellX + ndx;
                    var ncy = cellY + ndy;
                    var wrappedCX = Mod(ncx, cellPeriod);
                    var wrappedCY = Mod(ncy, cellPeriod);

                    var flowerRoll = HashToUnit(wrappedCX, wrappedCY, settings.Seed + 73);
                    if (flowerRoll < 1f - density * 0.9f)
                    {
                        continue;
                    }

                    var centerInCellX = 1 + Math.Min(cellSize - 2, (int)(HashToUnit(wrappedCX, wrappedCY, settings.Seed + 79) * Math.Max(1, cellSize - 2)));
                    var centerInCellY = 1 + Math.Min(cellSize - 2, (int)(HashToUnit(wrappedCX, wrappedCY, settings.Seed + 83) * Math.Max(1, cellSize - 2)));

                    var globalCenterX = ncx * cellSize + centerInCellX;
                    var globalCenterY = ncy * cellSize + centerInCellY;

                    var dx = Math.Abs(x - globalCenterX);
                    var dy = Math.Abs(y - globalCenterY);

                    if (settings.GrassFlowerMode == GrassFlowerMode.Custom)
                    {
                        var tintColor = (settings.FlowerPalette != null && settings.FlowerPalette.Length > 0)
                            ? PickFlowerColor(settings, wrappedCX, wrappedCY, settings.Seed)
                            : (Color?)null;
                        var customResult = ApplyCustomFlower(color, x, y, globalCenterX, globalCenterY, petalRadius, settings, tintColor);
                        if (customResult.R != color.R || customResult.G != color.G || customResult.B != color.B || customResult.A != color.A)
                        {
                            return customResult;
                        }
                        continue;
                    }

                    var manhattanDistance = dx + dy;

                    if (manhattanDistance > petalRadius + 1)
                    {
                        continue;
                    }

                    var flowerColor = PickFlowerColor(settings, wrappedCX, wrappedCY, settings.Seed);
                    var coreColor = Lerp(flowerColor, Colors.White, 0.18f + brightnessBoost);
                    if (dx == 0 && dy == 0)
                    {
                        return coreColor;
                    }

                    if (manhattanDistance <= petalRadius)
                    {
                        return Lerp(flowerColor, Colors.White, 0.08f + brightnessBoost * 0.5f);
                    }

                    if (petalRadius > 1 && dx <= 1 && dy <= 1)
                    {
                        return Lerp(flowerColor, Colors.White, 0.2f + brightnessBoost * 0.6f);
                    }
                }
            }

            return color;
        }

        private static Color ApplyCustomFlower(Color color, int localX, int localY, int centerX, int centerY, int petalRadius, TileLayerSettings settings, Color? tintColor)
        {
            var patternPixels = settings.CustomFlowerPatternPixels;
            if (patternPixels == null || settings.CustomFlowerPatternWidth <= 0 || settings.CustomFlowerPatternHeight <= 0)
            {
                return color;
            }

            var drawSize = Math.Max(1, petalRadius * 2 + 1);
            var left = centerX - (drawSize / 2);
            var top = centerY - (drawSize / 2);

            var dx = localX - left;
            var dy = localY - top;

            if (dx < 0 || dy < 0 || dx >= drawSize || dy >= drawSize)
            {
                return color;
            }

            var sampleX = Math.Clamp(dx * settings.CustomFlowerPatternWidth / drawSize, 0, settings.CustomFlowerPatternWidth - 1);
            var sampleY = Math.Clamp(dy * settings.CustomFlowerPatternHeight / drawSize, 0, settings.CustomFlowerPatternHeight - 1);
            var patternColor = ReadPatternColor(patternPixels, settings.CustomFlowerPatternWidth, sampleX, sampleY);
            if (patternColor.A == 0)
            {
                return color;
            }

            Color finalColor;
            if (tintColor.HasValue)
            {
                var brightness = (patternColor.R * 0.299f + patternColor.G * 0.587f + patternColor.B * 0.114f) / 255f;
                finalColor = Color.FromArgb(patternColor.A,
                    (byte)Math.Clamp(tintColor.Value.R * brightness, 0, 255),
                    (byte)Math.Clamp(tintColor.Value.G * brightness, 0, 255),
                    (byte)Math.Clamp(tintColor.Value.B * brightness, 0, 255));
            }
            else
            {
                finalColor = patternColor;
            }

            var alpha = finalColor.A / 255f;
            return Lerp(color, Color.FromArgb(255, finalColor.R, finalColor.G, finalColor.B), alpha);
        }

        private static Color ReadPatternColor(byte[] pixels, int width, int x, int y)
        {
            var index = ((y * width) + x) * 4;
            if (index < 0 || index + 3 >= pixels.Length)
            {
                return Colors.Transparent;
            }

            return Color.FromArgb(pixels[index + 3], pixels[index + 2], pixels[index + 1], pixels[index]);
        }

        private static Color PickFlowerColor(TileLayerSettings settings, int x, int y, int seed)
        {
            var palette = settings.FlowerPalette;
            var weights = settings.FlowerWeights;
            if (palette == null || weights == null || palette.Length == 0 || weights.Length != palette.Length)
                return settings.FlowerColor;

            var rnd = HashToUnit(x, y, seed + 999);
            var total = 0f;
            for (var i = 0; i < weights.Length; i++) total += weights[i];
            if (total <= 0f) return settings.FlowerColor;

            var target = rnd * total;
            var acc = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (target <= acc) return palette[i];
            }
            return palette[^1];
        }
    }
}
