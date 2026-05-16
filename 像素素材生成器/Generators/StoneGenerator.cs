using System;
using System.Windows.Media;

namespace PixelAssetGenerator.Generators
{
    public class StoneGenerator : BaseTileGenerator
    {
        private static readonly Color Border = Color.FromRgb(52, 52, 56);
        private static readonly Color Shadow = Color.FromRgb(82, 82, 88);
        private static readonly Color Mid = Color.FromRgb(118, 118, 124);
        private static readonly Color Light = Color.FromRgb(148, 148, 154);
        private static readonly Color Highlight = Color.FromRgb(168, 168, 174);
        private static readonly Color MossDark = Color.FromRgb(54, 78, 48);
        private static readonly Color MossMid = Color.FromRgb(72, 104, 64);
        private static readonly Color MossLight = Color.FromRgb(96, 132, 80);

        protected override Color GeneratePixel(int x, int y, int size, TileLayerSettings settings)
        {
            var seed = settings.Seed;
            var horizontalTileCount = Math.Max(1, settings.StoneHorizontalTileCount);
            var verticalTileCount = Math.Max(1, settings.StoneVerticalTileCount);
            var crackDensity = Math.Clamp(settings.StoneCrackDensity, 0f, 1f);
            var mossDensity = Math.Clamp(settings.StoneMossDensity, 0f, 1f);

            var wrappedX = Mod(x, size);
            var wrappedY = Mod(y, size);

            var by = wrappedY * verticalTileCount / size;
            var rowStart = by * size / verticalTileCount;
            var rowEnd = (by + 1) * size / verticalTileCount;
            var blockHeight = Math.Max(1, rowEnd - rowStart);
            var ly = wrappedY - rowStart;

            var averageBlockWidth = Math.Max(1, size / horizontalTileCount);
            var rowOffset = (by % 2 == 1) ? Math.Max(1, averageBlockWidth / 2) : 0;
            var adjustedX = Mod(wrappedX + rowOffset, size);
            var bx = adjustedX * horizontalTileCount / size;
            var columnStart = bx * size / horizontalTileCount;
            var columnEnd = (bx + 1) * size / horizontalTileCount;
            var blockWidth = Math.Max(1, columnEnd - columnStart);
            var lx = adjustedX - columnStart;

            if (lx == 0 || ly == 0)
            {
                return Border;
            }

            var innerWidth = Math.Max(1, blockWidth - 1);
            var innerHeight = Math.Max(1, blockHeight - 1);
            var localX = Math.Clamp((lx - 0.5f) / innerWidth, 0f, 1f);
            var localY = Math.Clamp((ly - 0.5f) / innerHeight, 0f, 1f);
            var blockHash = HashToUnit(bx, by, seed + 10);

            var centerDx = localX - 0.5f;
            var centerDy = localY - 0.5f;
            var centerDistance = MathF.Sqrt(centerDx * centerDx + centerDy * centerDy);
            var centerLift = 1f - SmoothStep(0.12f, 0.72f, centerDistance);
            var lightBias = (1f - localX) * 0.38f + (1f - localY) * 0.62f;
            var surfaceNoise = TileableFractalNoise(
                wrappedX * (0.45f + blockHash * 0.2f),
                wrappedY * (0.45f + (1f - blockHash) * 0.2f),
                size,
                3,
                0.55f,
                2f,
                seed + 70 + bx * 17 + by * 31);

            var tone = 0.18f;
            tone += blockHash * 0.22f;
            tone += centerLift * 0.26f;
            tone += lightBias * 0.24f;
            tone += (surfaceNoise - 0.5f) * 0.24f;

            var color = ResolveStoneTone(tone);

            if (lx == 1 || ly == 1)
            {
                color = BlendToward(color, Light);
            }

            if (lx >= blockWidth - 1 || ly >= blockHeight - 1)
            {
                color = BlendToward(color, Shadow);
            }

            var fractureBand = crackDensity > 0f
                ? ComputeFractureMask(localX, localY, blockWidth, blockHeight, bx, by, seed, crackDensity, 2.1f)
                : 0f;
            var fractureCore = crackDensity > 0f
                ? ComputeFractureMask(localX, localY, blockWidth, blockHeight, bx, by, seed, crackDensity, 1f)
                : 0f;

            if (fractureBand > 0f)
            {
                color = LerpColor(color, Shadow, fractureBand * 0.45f);
            }

            if (fractureCore > 0f)
            {
                color = LerpColor(color, Border, fractureCore);
            }

            if (mossDensity > 0f)
            {
                var edgeDistance = MathF.Min(MathF.Min(localX, 1f - localX), MathF.Min(localY, 1f - localY));
                var edgePocket = 1f - SmoothStep(0.08f, 0.3f, edgeDistance);
                var shadowPocket = SmoothStep(0.48f, 0.92f, localY * 0.7f + localX * 0.3f);
                var moistureNoise = TileableFractalNoise(wrappedX * 0.75f, wrappedY * 0.75f, size, 3, 0.58f, 2f, seed + 301);
                var patchNoise = TileableFractalNoise(wrappedX * 1.35f, wrappedY * 1.35f, size, 2, 0.5f, 2f, seed + 337);
                var mossNoise = moistureNoise * 0.68f + patchNoise * 0.32f;
                var mossAffinity = Math.Clamp(fractureBand * 0.8f + edgePocket * 0.7f + shadowPocket * 0.35f, 0f, 1f);
                var threshold = 0.9f - mossDensity * 0.34f - mossAffinity * 0.24f;

                if (mossAffinity > 0.16f && mossNoise > threshold)
                {
                    var mossTone = ResolveMossTone(mossNoise + fractureBand * 0.25f);
                    var blend = Math.Clamp(0.42f + mossAffinity * 0.35f, 0f, 0.88f);
                    color = LerpColor(color, mossTone, blend);
                }
            }

            return color;
        }

        private static float ComputeFractureMask(float x, float y, int blockWidth, int blockHeight, int blockX, int blockY, int seed, float density, float widthScale)
        {
            var crackPresence = 0.08f + density * 0.82f;
            if (HashToUnit(blockX, blockY, seed + 205) > crackPresence)
            {
                return 0f;
            }

            var aspect = blockWidth / (float)Math.Max(1, blockHeight);
            var routeSelector = HashToUnit(blockX, blockY, seed + 211);
            var startOffset = 0.18f + HashToUnit(blockX, blockY, seed + 223) * 0.2f;
            var endOffset = 0.62f + HashToUnit(blockX, blockY, seed + 227) * 0.2f;

            (float x, float y) start;
            (float x, float y) end;

            if (aspect > 1.25f)
            {
                start = routeSelector < 0.62f ? (0.22f + startOffset * 0.45f, 0f) : (0f, startOffset);
                end = routeSelector < 0.62f ? (0.54f + endOffset * 0.25f, 1f) : (1f, 0.58f + endOffset * 0.18f);
            }
            else if (aspect < 0.8f)
            {
                start = routeSelector < 0.62f ? (0f, 0.22f + startOffset * 0.45f) : (startOffset, 0f);
                end = routeSelector < 0.62f ? (1f, 0.54f + endOffset * 0.25f) : (0.58f + endOffset * 0.18f, 1f);
            }
            else if (routeSelector < 0.28f)
            {
                start = (0f, startOffset);
                end = (1f, 0.58f + endOffset * 0.18f);
            }
            else if (routeSelector < 0.56f)
            {
                start = (0.22f + startOffset * 0.45f, 0f);
                end = (0.54f + endOffset * 0.25f, 1f);
            }
            else if (routeSelector < 0.78f)
            {
                start = (0f, 0.62f + startOffset * 0.18f);
                end = (1f, 0.18f + endOffset * 0.2f);
            }
            else
            {
                start = (0.62f + startOffset * 0.18f, 0f);
                end = (0.18f + endOffset * 0.2f, 1f);
            }

            var directionX = end.x - start.x;
            var directionY = end.y - start.y;
            var directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
            if (directionLength < 0.0001f)
            {
                return 0f;
            }

            directionX /= directionLength;
            directionY /= directionLength;
            var normalX = -directionY;
            var normalY = directionX;

            var bendStrength = 0.08f + density * 0.12f;
            var bendOneT = 0.24f + HashToUnit(blockX, blockY, seed + 229) * 0.18f;
            var bendTwoT = 0.6f + HashToUnit(blockX, blockY, seed + 233) * 0.16f;
            var bendOneOffset = (HashToUnit(blockX, blockY, seed + 239) - 0.5f) * bendStrength;
            var bendTwoOffset = (HashToUnit(blockX, blockY, seed + 241) - 0.5f) * bendStrength * 1.2f;
            if (MathF.Abs(bendOneOffset - bendTwoOffset) < 0.03f)
            {
                bendTwoOffset = -bendTwoOffset * 0.85f;
            }

            var bendOne = OffsetPoint(LerpPoint(start, end, bendOneT), normalX, normalY, bendOneOffset);
            var bendTwo = OffsetPoint(LerpPoint(start, end, bendTwoT), normalX, normalY, bendTwoOffset);
            var width = (0.018f + density * 0.022f) * widthScale;

            var primary = ComputePolylineMask(x, y, width, start, bendOne, bendTwo, end);

            var branchChance = density * 0.52f;
            if (HashToUnit(blockX, blockY, seed + 257) > branchChance)
            {
                return primary;
            }

            var branchStart = HashToUnit(blockX, blockY, seed + 263) > 0.5f ? bendOne : bendTwo;
            var branchForward = 0.04f + density * 0.08f + HashToUnit(blockX, blockY, seed + 269) * 0.04f;
            var branchSide = (0.08f + density * 0.12f) * (HashToUnit(blockX, blockY, seed + 271) > 0.5f ? 1f : -1f);
            var branchEnd = (
                Math.Clamp(branchStart.x + directionX * branchForward + normalX * branchSide, 0.04f, 0.96f),
                Math.Clamp(branchStart.y + directionY * branchForward + normalY * branchSide, 0.04f, 0.96f));
            var branch = ComputeSegmentMask(x, y, branchStart, branchEnd, width * 0.82f);

            return Math.Max(primary, branch * 0.9f);
        }

        private static float ComputePolylineMask(
            float x,
            float y,
            float width,
            (float x, float y) start,
            (float x, float y) bendOne,
            (float x, float y) bendTwo,
            (float x, float y) end)
        {
            var segmentOne = ComputeSegmentMask(x, y, start, bendOne, width);
            var segmentTwo = ComputeSegmentMask(x, y, bendOne, bendTwo, width * 0.92f);
            var segmentThree = ComputeSegmentMask(x, y, bendTwo, end, width * 0.86f);
            return Math.Max(segmentOne, Math.Max(segmentTwo, segmentThree));
        }

        private static float ComputeSegmentMask(float x, float y, (float x, float y) start, (float x, float y) end, float width)
        {
            var distance = DistanceToSegment(x, y, start, end);
            return 1f - SmoothStep(width, width + 0.02f, distance);
        }

        private static (float x, float y) LerpPoint((float x, float y) start, (float x, float y) end, float t)
        {
            return (Lerp(start.x, end.x, t), Lerp(start.y, end.y, t));
        }

        private static (float x, float y) OffsetPoint((float x, float y) point, float normalX, float normalY, float distance)
        {
            return (
                Math.Clamp(point.x + normalX * distance, 0.04f, 0.96f),
                Math.Clamp(point.y + normalY * distance, 0.04f, 0.96f));
        }

        private static float DistanceToSegment(float px, float py, (float x, float y) start, (float x, float y) end)
        {
            var segmentX = end.x - start.x;
            var segmentY = end.y - start.y;
            var segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;
            if (segmentLengthSquared < 0.000001f)
            {
                var dx = px - start.x;
                var dy = py - start.y;
                return MathF.Sqrt(dx * dx + dy * dy);
            }

            var t = ((px - start.x) * segmentX + (py - start.y) * segmentY) / segmentLengthSquared;
            t = Math.Clamp(t, 0f, 1f);
            var nearestX = start.x + segmentX * t;
            var nearestY = start.y + segmentY * t;
            var distanceX = px - nearestX;
            var distanceY = py - nearestY;
            return MathF.Sqrt(distanceX * distanceX + distanceY * distanceY);
        }

        private static Color ResolveStoneTone(float tone)
        {
            return tone switch
            {
                > 0.82f => Highlight,
                > 0.63f => Light,
                > 0.36f => Mid,
                _ => Shadow
            };
        }

        private static Color ResolveMossTone(float tone)
        {
            return tone switch
            {
                > 0.78f => MossLight,
                > 0.52f => MossMid,
                _ => MossDark
            };
        }

        private static Color BlendToward(Color from, Color toward)
        {
            return Color.FromRgb(
                (byte)((from.R + toward.R) / 2),
                (byte)((from.G + toward.G) / 2),
                (byte)((from.B + toward.B) / 2));
        }

        private static Color LerpColor(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromRgb(
                (byte)MathF.Round(Lerp(from.R, to.R, amount)),
                (byte)MathF.Round(Lerp(from.G, to.G, amount)),
                (byte)MathF.Round(Lerp(from.B, to.B, amount)));
        }
    }
}
