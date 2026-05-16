using System;
using System.Windows.Media;
using PixelAssetGenerator;

namespace PixelAssetGenerator.Generators
{
    public class RoadGenerator : BaseTileGenerator
    {
        private readonly record struct RoadSample(float DistanceToCenter, float PathPosition, bool HasPathPosition);

        private static readonly Color AsphaltDark = Color.FromRgb(45, 45, 50);
        private static readonly Color AsphaltBase = Color.FromRgb(65, 65, 70);
        private static readonly Color AsphaltLight = Color.FromRgb(85, 85, 90);
        private static readonly Color AsphaltLine = Color.FromRgb(220, 200, 70);
        private static readonly Color AsphaltLineWhite = Color.FromRgb(210, 210, 215);
        private static readonly Color ShoulderDark = Color.FromRgb(105, 85, 55);
        private static readonly Color ShoulderLight = Color.FromRgb(140, 115, 80);

        protected override Color GeneratePixel(int x, int y, int size, TileLayerSettings settings)
        {
            var seed = settings.Seed;
            var layout = settings.RoadLayout;
            var roadWidth = Math.Clamp(settings.RoadWidth, 0.1f, 1f);

            var wx = Mod(x, size);
            var wy = Mod(y, size);
            float normX = wx / (float)size;
            float normY = wy / (float)size;

            float roadHalfWidth = 0.05f + roadWidth * 0.25f;
            float shoulderWidth = 0.02f + Math.Clamp(settings.RoadShoulderWidth, 0f, 1f) * 0.08f;

            int pxSize = Math.Max(1, size / 32);
            float pxX = MathF.Floor(wx / pxSize) * pxSize;
            float pxY = MathF.Floor(wy / pxSize) * pxSize;

            var roadSample = SampleRoad(normX, normY, layout, roadHalfWidth, settings.RoadCornerRoundness);
            float distToCenter = roadSample.DistanceToCenter;
            float whiteLineDistortion = GetRoadDistortion(pxX, pxY, size, seed + 50, settings.RoadEdgeRoughness);
            float centerLineDistortion = GetRoadDistortion(pxX, pxY, size, seed + 60, settings.RoadCenterLineRoughness);
            float shoulderDistortion = GetRoadDistortion(pxX, pxY, size, seed + 70, settings.RoadShoulderRoughness);
            float whiteLineDistance = distToCenter + whiteLineDistortion;
            float centerLineDistance = distToCenter + centerLineDistortion;
            float shoulderDistance = distToCenter + shoulderDistortion;

            if (distToCenter < roadHalfWidth)
            {
                if (whiteLineDistance > roadHalfWidth - 0.02f)
                    return AsphaltLineWhite;

                var gritNoise = TileableModuleNoise(GetPerlin(32f, 1, 1f, 1f, seed + 10), wx, wy, size);
                float gravelIntensity = settings.RoadGravelDensity * 0.8f;
                Color asphalt = AsphaltBase;

                if (gritNoise > 1f - gravelIntensity * 0.2f) asphalt = AsphaltLight;
                else if (gritNoise < gravelIntensity * 0.2f) asphalt = AsphaltDark;

                float rutNoise = GetRutNoise(distToCenter, roadHalfWidth, wx, wy, size, seed, settings.RoadRutDepth);

                if (rutNoise > 0f) asphalt = Lerp(asphalt, AsphaltDark, Math.Clamp(rutNoise, 0f, 0.85f));

                var centerLineWidth = Math.Max(0.003f, settings.RoadCenterLine * roadHalfWidth * 0.45f);
                if (settings.RoadCenterLine > 0f && roadSample.HasPathPosition)
                {
                    if (centerLineDistance < centerLineWidth)
                    {
                        float dashPhase = (roadSample.PathPosition * 8f) % 1f;
                        if (dashPhase < 0.5f) asphalt = AsphaltLine;
                    }
                }

                return asphalt;
            }

            if (shoulderDistance < roadHalfWidth + shoulderWidth)
            {
                var shoulderTextureNoise = TileableModuleNoise(GetSimplex(20f, seed + 20), wx, wy, size);
                Color sdColor = shoulderTextureNoise > 0.5f ? ShoulderLight : ShoulderDark;

                if (settings.RoadShoulderRoughness <= 0f)
                {
                    return sdColor;
                }

                float shoulderPos = (shoulderDistance - roadHalfWidth) / shoulderWidth;
                float shoulderEdgeNoise = TileableModuleNoise(GetSimplex(14f, seed + 21), wx, wy, size);
                if (shoulderPos > shoulderEdgeNoise * 0.8f + 0.2f) return Colors.Transparent;

                return sdColor;
            }

            return Colors.Transparent;
        }

        private static RoadSample SampleRoad(float normX, float normY, RoadLayout layout, float roadHalfWidth, float cornerRoundness)
        {
            if (TryTransformCornerToCanonical(layout, normX, normY, out var canonicalX, out var canonicalY))
            {
                return SampleCanonicalCorner(canonicalX, canonicalY, roadHalfWidth, cornerRoundness);
            }

            return layout switch
            {
                RoadLayout.StraightVertical => new RoadSample(Math.Abs(normX - 0.5f), normY, true),
                RoadLayout.StraightHorizontal => new RoadSample(Math.Abs(normY - 0.5f), normX, true),
                _ => new RoadSample(GetRoadDistance(normX, normY, layout, roadHalfWidth, cornerRoundness), 0f, false)
            };
        }

        private static float GetRutNoise(float distToCenter, float roadHalfWidth, float wx, float wy, int size, int seed, float rutDepth)
        {
            if (rutDepth <= 0f || roadHalfWidth <= 0f)
            {
                return 0f;
            }

            float normalizedDistance = distToCenter / roadHalfWidth;
            float rutBandDistance = MathF.Abs(normalizedDistance - 0.42f);
            if (rutBandDistance >= 0.14f)
            {
                return 0f;
            }

            float rutMask = (0.14f - rutBandDistance) / 0.14f;
            float rutVariation = 0.75f + TileableModuleNoise(GetPerlin(24f, 1, 1f, 1f, seed + 30), wx, wy, size) * 0.25f;
            return rutMask * rutVariation * rutDepth;
        }

        private static float GetRoadDistortion(float pxX, float pxY, int size, int seed, float roughness)
        {
            if (roughness <= 0f)
            {
                return 0f;
            }

            return (TileableModuleNoise(GetPerlin(8f, 1, 1f, 1f, seed), pxX, pxY, size) - 0.5f) * roughness * 0.06f;
        }

        private static bool TryTransformCornerToCanonical(RoadLayout layout, float normX, float normY, out float canonicalX, out float canonicalY)
        {
            switch (layout)
            {
                case RoadLayout.CornerNE:
                    canonicalX = normX;
                    canonicalY = normY;
                    return true;
                case RoadLayout.CornerSE:
                    canonicalX = normX;
                    canonicalY = 1f - normY;
                    return true;
                case RoadLayout.CornerSW:
                    canonicalX = 1f - normX;
                    canonicalY = 1f - normY;
                    return true;
                case RoadLayout.CornerNW:
                    canonicalX = 1f - normX;
                    canonicalY = normY;
                    return true;
                default:
                    canonicalX = 0f;
                    canonicalY = 0f;
                    return false;
            }
        }

        private static RoadSample SampleCanonicalCorner(float normX, float normY, float roadHalfWidth, float cornerRoundness)
        {
            float clampedRoundness = Math.Clamp(cornerRoundness, 0f, 1f);
            float curveRadius = Math.Max(0f, 0.5f - roadHalfWidth) * clampedRoundness;
            float verticalEnd = 0.5f - curveRadius;
            float horizontalStart = 0.5f + curveRadius;
            float arcLength = MathF.PI * 0.5f * curveRadius;
            float totalLength = Math.Max(0.0001f, verticalEnd + arcLength + (1f - horizontalStart));

            var best = new RoadSample(float.MaxValue, 0f, true);

            best = MinSample(best, SampleSegment(normX, normY, 0.5f, 0f, 0.5f, verticalEnd, 0f));
            best = MinSample(best, SampleSegment(normX, normY, horizontalStart, 0.5f, 1f, 0.5f, verticalEnd + arcLength));

            if (curveRadius > 0f)
            {
                float centerX = 0.5f + curveRadius;
                float centerY = 0.5f - curveRadius;
                float dx = normX - centerX;
                float dy = normY - centerY;
                float angle = MathF.Atan2(dy, dx);

                if (angle >= MathF.PI * 0.5f && angle <= MathF.PI)
                {
                    float radialDistance = MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - curveRadius);
                    float arcProgress = (MathF.PI - angle) / (MathF.PI * 0.5f);
                    best = MinSample(best, new RoadSample(radialDistance, verticalEnd + arcProgress * arcLength, true));
                }
            }

            return new RoadSample(best.DistanceToCenter, best.PathPosition / totalLength, best.HasPathPosition);
        }

        private static RoadSample SampleSegment(float px, float py, float ax, float ay, float bx, float by, float lengthOffset)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float lengthSquared = dx * dx + dy * dy;

            if (lengthSquared <= 0f)
            {
                float pointDistance = MathF.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
                return new RoadSample(pointDistance, lengthOffset, true);
            }

            float t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0f, 1f);
            float closestX = ax + dx * t;
            float closestY = ay + dy * t;
            float distance = MathF.Sqrt((px - closestX) * (px - closestX) + (py - closestY) * (py - closestY));
            float segmentLength = MathF.Sqrt(lengthSquared);
            return new RoadSample(distance, lengthOffset + segmentLength * t, true);
        }

        private static RoadSample MinSample(RoadSample current, RoadSample candidate)
        {
            return candidate.DistanceToCenter < current.DistanceToCenter ? candidate : current;
        }

        private static float GetRoadDistance(float normX, float normY, RoadLayout layout, float roadWidth, float cornerRoundness)
        {
            var center = 0.5f;
            var dist = float.MaxValue;

            GetConnections(layout, out var up, out var right, out var down, out var left);

            if (up && normY <= center)
                dist = Math.Min(dist, Math.Abs(normX - center));
            if (down && normY >= center)
                dist = Math.Min(dist, Math.Abs(normX - center));
            if (left && normX <= center)
                dist = Math.Min(dist, Math.Abs(normY - center));
            if (right && normX >= center)
                dist = Math.Min(dist, Math.Abs(normY - center));

            // Fill the center intersection area
            if (IsIntersection(layout))
            {
                var cx = Math.Abs(normX - center);
                var cy = Math.Abs(normY - center);
                if (cx < roadWidth && cy < roadWidth)
                {
                    // Rounded corners
                    if (cornerRoundness > 0f)
                    {
                        var maxR = roadWidth;
                        var rSquare = cx * cx + cy * cy;
                        var mix = Math.Max(cx, cy);
                        var rounded = MathF.Sqrt(rSquare);
                        dist = Math.Min(dist, Lerp(mix, rounded, cornerRoundness) * 0.5f);
                    }
                    else
                    {
                        dist = Math.Min(dist, Math.Max(cx, cy) * 0.5f);
                    }
                }
            }

            return dist == float.MaxValue ? 1f : dist;
        }

        private static void GetConnections(RoadLayout layout, out bool up, out bool right, out bool down, out bool left)
        {
            up = right = down = left = false;
            switch (layout)
            {
                case RoadLayout.StraightVertical: up = down = true; break;
                case RoadLayout.StraightHorizontal: left = right = true; break;
                case RoadLayout.CornerNE: up = right = true; break;
                case RoadLayout.CornerSE: right = down = true; break;
                case RoadLayout.CornerSW: down = left = true; break;
                case RoadLayout.CornerNW: left = up = true; break;
                case RoadLayout.TJunctionUp: left = right = down = true; break;
                case RoadLayout.TJunctionRight: up = down = left = true; break;
                case RoadLayout.TJunctionDown: left = right = up = true; break;
                case RoadLayout.TJunctionLeft: up = down = right = true; break;
                case RoadLayout.Cross: up = right = down = left = true; break;
            }
        }

        private static bool IsIntersection(RoadLayout layout)
        {
            return layout is not RoadLayout.StraightVertical and not RoadLayout.StraightHorizontal;
        }
    }
}
