using System;
using System.Windows.Media;

namespace PixelAssetGenerator.Generators
{
    public class WaterGenerator : BaseTileGenerator
    {
        private readonly record struct WaterStyle(
            Color Abyss,
            Color Deep,
            Color Mid,
            Color Shallow,
            Color Crest,
            Color Sparkle,
            Color Foam);

        private static readonly WaterStyle Style = new(
            Abyss: Color.FromRgb(18, 44, 118),
            Deep: Color.FromRgb(28, 72, 152),
            Mid: Color.FromRgb(50, 112, 188),
            Shallow: Color.FromRgb(84, 156, 214),
            Crest: Color.FromRgb(132, 196, 236),
            Sparkle: Color.FromRgb(204, 236, 255),
            Foam: Color.FromRgb(238, 248, 255));

        protected override TileLayerSettings GetDetailSettings(TileLayerSettings settings)
        {
            return settings with
            {
                DetailDensity = settings.DetailDensity * 0.08f,
                MacroStrength = settings.MacroStrength * 0.1f,
                MicroStrength = settings.MicroStrength * 0.06f,
                AccentDensity = 0f
            };
        }

        protected override Color GeneratePixel(int x, int y, int size, TileLayerSettings settings)
        {
            var seed = settings.Seed;
            var waveScale = Math.Clamp(settings.WaterWaveScale, 0f, 1f);
            var depthVariation = Math.Clamp(settings.WaterDepthVariation, 0f, 1f);
            var choppiness = Math.Clamp(settings.WaterWaveChoppiness, 0f, 1f);
            var foamDensity = Math.Clamp(settings.WaterFoamDensity, 0f, 1f);
            var currentStrength = Math.Clamp(settings.WaterCurrentStrength, 0f, 1f);
            var currentDirection = Math.Clamp(settings.WaterCurrentDirection, 0f, 1f);
            var phaseX = x / (float)size * MathF.Tau;
            var phaseY = y / (float)size * MathF.Tau;

            var basin = TileableModuleNoise(GetPerlin(0.95f + depthVariation * 0.85f, 2, 0.58f, 2f, seed), x, y, size);
            var pocket = TileableModuleNoise(GetPerlin(1.7f + depthVariation * 1.25f, 2, 0.54f, 2f, seed + 17), x, y, size);
            var currentField = TileableModuleNoise(GetPerlin(2.15f + currentStrength * 2.2f, 2, 0.52f, 2f, seed + 41), x, y, size);
            var crestField = TileableModuleNoise(GetPerlin(3.1f + waveScale * 2.6f, 2, 0.56f, 2f, seed + 67), x, y, size);

            var directionalBands = TileableDirectionalBands(
                phaseX,
                phaseY,
                currentDirection,
                3 + (int)MathF.Round(waveScale * 5f),
                (currentField - 0.5f) * (0.9f + currentStrength * 1.8f));
            var crossBands = TileableDirectionalBands(
                phaseX,
                phaseY,
                currentDirection + 0.25f,
                1 + (int)MathF.Round(waveScale * 2f),
                crestField * MathF.Tau);
            var checker = ((x + y) & 1) == 0 ? -0.012f : 0.012f;

            var depth = basin * 0.56f + pocket * 0.24f + currentField * 0.2f;
            depth = Lerp(depth, pocket, depthVariation * 0.26f);

            var waveLift = SmoothStep(0.34f - choppiness * 0.08f, 0.8f, directionalBands);
            var crestMask = SmoothStep(0.7f - choppiness * 0.12f, 0.98f, directionalBands) * (0.58f + crossBands * 0.42f);
            var tone = depth * 0.76f + waveLift * (0.15f + choppiness * 0.13f) + crossBands * 0.05f;
            tone = Math.Clamp(tone + checker * (0.45f + choppiness * 0.65f), 0f, 1f);

            var color = SelectBaseColor(tone);
            color = Lerp(color, Style.Crest, crestMask * (0.2f + waveScale * 0.14f));
            color = ApplySparkles(color, x, y, size, settings, crestMask, foamDensity);
            color = ApplyFoam(color, x, y, size, settings, crestMask);
            return color;
        }

        private static Color SelectBaseColor(float tone)
        {
            if (tone < 0.18f)
            {
                return Lerp(Style.Abyss, Style.Deep, SmoothStep(0.02f, 0.18f, tone));
            }

            if (tone < 0.42f)
            {
                return Lerp(Style.Deep, Style.Mid, SmoothStep(0.18f, 0.42f, tone));
            }

            if (tone < 0.72f)
            {
                return Lerp(Style.Mid, Style.Shallow, SmoothStep(0.42f, 0.72f, tone));
            }

            return Lerp(Style.Shallow, Style.Crest, SmoothStep(0.72f, 0.96f, tone));
        }

        private static Color ApplySparkles(Color color, int x, int y, int size, TileLayerSettings settings, float crestMask, float foamDensity)
        {
            if (foamDensity <= 0.01f || crestMask <= 0.05f)
            {
                return color;
            }

            var foamSize = Math.Clamp(settings.WaterFoamSize, 0f, 1f);
            var cellSize = Math.Max(4, (int)MathF.Round(size / (3.5f + foamDensity * 3f)));
            var period = Math.Max(1, size / cellSize);
            var cellX = x / cellSize;
            var cellY = y / cellSize;
            var localX = x % cellSize;
            var localY = y % cellSize;
            var wrappedX = Mod(cellX, period);
            var wrappedY = Mod(cellY, period);

            var sparkleRoll = HashToUnit(wrappedX, wrappedY, settings.Seed + 101);
            var threshold = 0.9f - foamDensity * 0.26f - crestMask * 0.12f;
            if (sparkleRoll < threshold)
            {
                return color;
            }

            var centerX = Math.Min(cellSize - 2, 1 + (int)(HashToUnit(wrappedX, wrappedY, settings.Seed + 109) * Math.Max(1, cellSize - 2)));
            var centerY = Math.Min(cellSize - 2, 1 + (int)(HashToUnit(wrappedX, wrappedY, settings.Seed + 113) * Math.Max(1, cellSize - 2)));
            var halfWidth = foamSize >= 0.72f ? 2 : foamSize >= 0.3f ? 1 : 0;
            var dy = Math.Abs(localY - centerY);
            var dx = Math.Abs(localX - centerX);

            if (dy == 0 && dx <= halfWidth)
            {
                return Lerp(color, Style.Sparkle, 0.26f + crestMask * 0.34f);
            }

            if (dx == 0 && dy == 0)
            {
                return Lerp(color, Style.Foam, 0.38f + crestMask * 0.28f);
            }

            return color;
        }

        private static Color ApplyFoam(Color color, int x, int y, int size, TileLayerSettings settings, float crestMask)
        {
            var density = Math.Clamp(settings.WaterFoamDensity, 0f, 1f);
            if (density <= 0.01f || crestMask <= 0.08f)
            {
                return color;
            }

            var foamNoise = TileableModuleNoise(GetSimplex(9f + settings.WaterFoamSize * 10f, settings.Seed + 149), x, y, size);
            var threshold = 0.93f - density * 0.18f - crestMask * 0.1f;
            if (foamNoise <= threshold)
            {
                return color;
            }

            var blend = SmoothStep(threshold, 1f, foamNoise) * (0.2f + crestMask * 0.3f);
            return Lerp(color, Style.Foam, blend);
        }
    }
}
