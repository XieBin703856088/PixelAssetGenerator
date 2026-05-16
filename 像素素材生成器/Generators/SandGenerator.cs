using System;
using System.Windows.Media;

namespace PixelAssetGenerator.Generators
{
    public class SandGenerator : BaseTileGenerator
    {
        private readonly record struct SandStyle(
            Color Deep,
            Color Shadow,
            Color Mid,
            Color Light,
            Color Highlight,
            Color WarmTint,
            Color CoolTint,
            Color Pebble,
            Color PebbleHighlight);

        private static readonly SandStyle Style = new(
            Deep: Color.FromRgb(170, 122, 70),
            Shadow: Color.FromRgb(198, 152, 86),
            Mid: Color.FromRgb(222, 184, 112),
            Light: Color.FromRgb(238, 206, 136),
            Highlight: Color.FromRgb(252, 232, 170),
            WarmTint: Color.FromRgb(232, 180, 104),
            CoolTint: Color.FromRgb(204, 184, 136),
            Pebble: Color.FromRgb(156, 118, 84),
            PebbleHighlight: Color.FromRgb(222, 196, 148));

        protected override TileLayerSettings GetDetailSettings(TileLayerSettings settings)
        {
            return settings with
            {
                DetailDensity = settings.DetailDensity * 0.1f,
                MacroStrength = settings.MacroStrength * 0.12f,
                MicroStrength = settings.MicroStrength * 0.08f,
                AccentDensity = 0f
            };
        }

        protected override Color GeneratePixel(int x, int y, int size, TileLayerSettings settings)
        {
            var seed = settings.Seed;
            var duneScale = Math.Clamp(settings.SandDuneScale, 0f, 1f);
            var duneSharpness = Math.Clamp(settings.SandDuneSharpness, 0f, 1f);
            var rippleStrength = Math.Clamp(settings.SandRippleStrength, 0f, 1f);
            var rippleScale = Math.Clamp(settings.SandRippleScale, 0f, 1f);
            var variation = Math.Clamp(settings.ColorVariation, 0f, 1f);
            var rippleDirection = Math.Clamp(settings.SandRippleDirection, 0f, 1f);
            var phaseX = x / (float)size * MathF.Tau;
            var phaseY = y / (float)size * MathF.Tau;

            var dune = TileableModuleNoise(GetPerlin(0.82f + duneScale * 1.2f, 2, 0.56f, 2f, seed), x, y, size);
            var contour = TileableModuleNoise(GetPerlin(1.45f + duneScale * 1.95f, 2, 0.54f, 2f, seed + 17), x, y, size);
            var cellSize = Math.Max(4, size / 6);
            var patchPeriod = Math.Max(1, size / cellSize);
            var patch = TileableValueNoise((float)x / cellSize, (float)y / cellSize, patchPeriod, seed + 43);
            var directionalRipple = TileableDirectionalBands(
                phaseX,
                phaseY,
                rippleDirection,
                3 + (int)MathF.Round(rippleScale * 6f),
                (contour - 0.5f) * (1.1f + rippleStrength * 1.5f));
            var crossRipple = TileableDirectionalBands(
                phaseX,
                phaseY,
                rippleDirection + 0.25f,
                1 + (int)MathF.Round(duneScale * 2f),
                patch * MathF.Tau);
            var checker = ((x + y) & 1) == 0 ? -0.011f : 0.011f;

            var tone = dune * 0.5f + contour * 0.22f + patch * 0.28f;
            tone = Lerp(tone, patch, 0.18f + duneSharpness * 0.22f);
            tone += (directionalRipple - 0.5f) * (0.12f + rippleStrength * 0.16f);
            tone += (crossRipple - 0.5f) * 0.05f;
            tone = Math.Clamp(tone + checker * (0.36f + rippleStrength * 0.58f), 0f, 1f);

            var color = SelectBaseColor(tone);
            color = ApplyColorVariation(color, patch, contour, variation);
            color = ApplyRippleRelief(color, directionalRipple, rippleStrength);
            color = ApplyPebbles(color, x, y, size, settings);
            return color;
        }

        private static Color SelectBaseColor(float tone)
        {
            if (tone < 0.18f)
            {
                return Lerp(Style.Deep, Style.Shadow, SmoothStep(0.02f, 0.18f, tone));
            }

            if (tone < 0.45f)
            {
                return Lerp(Style.Shadow, Style.Mid, SmoothStep(0.18f, 0.45f, tone));
            }

            if (tone < 0.75f)
            {
                return Lerp(Style.Mid, Style.Light, SmoothStep(0.45f, 0.75f, tone));
            }

            return Lerp(Style.Light, Style.Highlight, SmoothStep(0.75f, 0.95f, tone));
        }

        private static Color ApplyColorVariation(Color color, float patch, float contour, float variation)
        {
            if (variation <= 0.01f)
            {
                return color;
            }

            var warmMask = SmoothStep(0.56f, 0.92f, patch) * variation;
            var coolMask = SmoothStep(0.58f, 0.9f, 1f - contour) * variation * 0.65f;
            color = Lerp(color, Style.WarmTint, warmMask * 0.18f);
            color = Lerp(color, Style.CoolTint, coolMask * 0.12f);
            return color;
        }

        private static Color ApplyRippleRelief(Color color, float ripple, float rippleStrength)
        {
            if (rippleStrength <= 0.01f)
            {
                return color;
            }

            var ridge = SmoothStep(0.76f - rippleStrength * 0.08f, 0.98f, ripple);
            var trough = SmoothStep(0.02f, 0.2f + rippleStrength * 0.1f, 1f - ripple);
            color = Lerp(color, Style.Highlight, ridge * (0.1f + rippleStrength * 0.12f));
            color = Lerp(color, Style.Deep, trough * (0.08f + rippleStrength * 0.1f));
            return color;
        }

        private static Color ApplyPebbles(Color color, int x, int y, int size, TileLayerSettings settings)
        {
            var density = Math.Clamp(settings.SandPebbleDensity, 0f, 1f);
            if (density <= 0.01f)
            {
                return color;
            }

            var pebbleSize = Math.Clamp(settings.SandPebbleSize, 0f, 1f);
            var cellSize = Math.Max(6, (int)MathF.Round(size / (2.8f + density * 3.2f)));
            var period = Math.Max(1, size / cellSize);
            var cellX = x / cellSize;
            var cellY = y / cellSize;
            var localX = x % cellSize;
            var localY = y % cellSize;
            var wrappedX = Mod(cellX, period);
            var wrappedY = Mod(cellY, period);

            var pebbleRoll = HashToUnit(wrappedX, wrappedY, settings.Seed + 137);
            var threshold = 0.94f - density * 0.22f;
            if (pebbleRoll < threshold)
            {
                return color;
            }

            var centerX = Math.Min(cellSize - 2, 1 + (int)(HashToUnit(wrappedX, wrappedY, settings.Seed + 149) * Math.Max(1, cellSize - 2)));
            var centerY = Math.Min(cellSize - 2, 1 + (int)(HashToUnit(wrappedX, wrappedY, settings.Seed + 151) * Math.Max(1, cellSize - 2)));
            var radius = pebbleSize >= 0.7f ? 2 : pebbleSize >= 0.28f ? 1 : 0;
            var dx = Math.Abs(localX - centerX);
            var dy = Math.Abs(localY - centerY);
            var distance = dx + dy;

            if (distance == 0)
            {
                return Style.Pebble;
            }

            if (radius > 0 && distance <= radius)
            {
                return dx <= dy ? Style.PebbleHighlight : Style.Pebble;
            }

            return color;
        }
    }
}
