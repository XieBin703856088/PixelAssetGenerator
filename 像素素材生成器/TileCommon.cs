namespace PixelAssetGenerator
{
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    public enum TileType
    {
        Grass,
        Stone,
        Water,
        Sand,
        Road
    }

    public enum LayerBlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay
    }

    public enum MaskElement
    {
        Primary,
        Detail,
        Accent
    }

    public enum RoadLayout
    {
        StraightVertical,
        StraightHorizontal,
        CornerNE,
        CornerSE,
        CornerSW,
        CornerNW,
        TJunctionUp,
        TJunctionRight,
        TJunctionDown,
        TJunctionLeft,
        Cross
    }

    public enum GrassPreset
    {
        GrassA,
        GrassB,
        ForestGrass
    }

    public enum GrassFlowerMode
    {
        Palette,
        Custom
    }

    public readonly record struct TileLayerSettings(
        float Scale,
        int Octaves,
        float Persistence,
        float Lacunarity,
        float DetailDensity,
        float EdgeStrength,
        float MacroScale,
        float MacroStrength,
        float MicroScale,
        float MicroStrength,
        float AccentDensity,
        float AccentSize,
        float ColorVariation,
        float GrassBladeDensity,
        float GrassBladeHeight,
        float GrassPatchiness,
        GrassPreset GrassPreset,
        GrassFlowerMode GrassFlowerMode,
        float FlowerDensity,
        float FlowerSize,
        Color FlowerColor,
        Color[] FlowerPalette,
        float[] FlowerWeights,
        byte[]? CustomFlowerPatternPixels,
        int CustomFlowerPatternWidth,
        int CustomFlowerPatternHeight,
        float StoneCrackDensity,
        float StoneMossDensity,
        float WaterWaveScale,
        float WaterWaveChoppiness,
        float WaterFoamDensity,
        float WaterFoamSize,
        float WaterDepthVariation,
        float SandDuneScale,
        float SandDuneSharpness,
        float SandRippleStrength,
        float SandPebbleDensity,
        float SandPebbleSize,
        float RoadWidth, // Added RoadWidth for road settings
        float RoadEdgeRoughness,
        float RoadRutDepth,
        float RoadGravelDensity,
        float RoadShoulderWidth,
        float RoadShoulderRoughness,
        RoadLayout RoadLayout,
        float RoadCornerRoundness,
        int StoneHorizontalTileCount,
        int StoneVerticalTileCount,
        float WaterCurrentDirection,
        float WaterCurrentStrength,
        float SandRippleDirection,
        float SandRippleScale,
        float RoadCenterLine,
        float RoadCenterLineRoughness,
        float ErosionStrength,
        float ErosionScale,
        bool NineSliceEnabled,
        float NineSliceEdgeSize,
        float NineSliceMaskSize,
        float NineSliceEdgeFeather,
        bool MaskEnabled,
        bool MaskInvert,
        MaskElement MaskElement,
        int Seed);

    public readonly record struct TileLayerDefinition(
        TileType Type,
        TileLayerSettings Settings,
        LayerBlendMode BlendMode,
        float Opacity);
}
