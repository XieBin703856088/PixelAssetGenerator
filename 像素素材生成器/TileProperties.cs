using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PixelAssetGenerator
{
    // Shared properties container used by both LayerViewModel and NodeViewModel
    public class TileProperties : INotifyPropertyChanged
    {
        private int _seed = 1200;
        private double _scale = 5.0;
        private int _octaves = 3;
        private double _persistence = 0.5;
        private double _lacunarity = 2.0;
        private double _detailDensity = 0.35;
        private double _edgeStrength = 0.0;
        private double _macroScale = 2.5;
        private double _macroStrength = 0.6;
        private double _microScale = 8.0;
        private double _microStrength = 0.5;
        private double _accentDensity = 0.0;
        private double _accentSize = 0.25;
        private double _colorVariation = 0.5;
        private double _grassBladeDensity = 0.6;
        private double _grassBladeHeight = 0.7;
        private double _grassPatchiness = 0.5;
        private GrassPreset _grassPreset = GrassPreset.GrassA;
        private GrassFlowerMode _grassFlowerMode = GrassFlowerMode.Palette;
        private double _flowerDensity = 0.35;
        private double _flowerSize = 1.0;
        private System.Windows.Media.Color _flowerColor = System.Windows.Media.Color.FromRgb(220, 220, 100);
        private double _stoneCrackDensity = 0.45;
        private double _stoneMossDensity = 0.35;
        private int _stoneHorizontalTileCount = 4;
        private int _stoneVerticalTileCount = 4;
        private double _waterWaveScale = 0.42;
        private double _waterWaveChoppiness = 0.34;
        private double _waterFoamDensity = 0.18;
        private double _waterFoamSize = 0.22;
        private double _waterDepthVariation = 0.46;
        private double _sandDuneScale = 0.52;
        private double _sandDuneSharpness = 0.48;
        private double _sandRippleStrength = 0.58;
        private double _sandPebbleDensity = 0.14;
        private double _sandPebbleSize = 0.24;
        private double _roadWidth = 0.5;
        private double _roadEdgeRoughness = 0.5;
        private double _roadRutDepth = 0.45;
        private double _roadGravelDensity = 0.4;
        private double _roadShoulderWidth = 0.5;
        private double _roadShoulderRoughness = 0.5;
        private RoadLayout _roadLayout = RoadLayout.StraightVertical;
        private double _roadCornerRoundness = 0.25;
        private double _waterCurrentDirection = 0.58;
        private double _waterCurrentStrength = 0.34;
        private double _sandRippleDirection = 0.62;
        private double _sandRippleScale = 0.38;
        private double _roadCenterLine = 0.2;
        private double _roadCenterLineRoughness = 0.25;
        private double _erosionStrength = 0.3;
        private double _erosionScale = 0.6;
        private double _opacity = 1.0;
        private LayerBlendMode _blendMode = LayerBlendMode.Normal;
        private bool _nineSliceEnabled;
        private double _nineSliceEdgeSize = 0.25;
        private double _nineSliceMaskSize = 0.9;
        private double _nineSliceEdgeFeather = 0.3;
        private bool _maskEnabled;
        private bool _maskInvert;
        private MaskElement _maskElement = MaskElement.Primary;

        public int Seed { get => _seed; set => SetField(ref _seed, value); }
        public double Scale { get => _scale; set => SetField(ref _scale, value); }
        public int Octaves { get => _octaves; set => SetField(ref _octaves, value); }
        public double Persistence { get => _persistence; set => SetField(ref _persistence, value); }
        public double Lacunarity { get => _lacunarity; set => SetField(ref _lacunarity, value); }
        public double DetailDensity { get => _detailDensity; set => SetField(ref _detailDensity, value); }
        public double EdgeStrength { get => _edgeStrength; set => SetField(ref _edgeStrength, value); }
        public double MacroScale { get => _macroScale; set => SetField(ref _macroScale, value); }
        public double MacroStrength { get => _macroStrength; set => SetField(ref _macroStrength, value); }
        public double MicroScale { get => _microScale; set => SetField(ref _microScale, value); }
        public double MicroStrength { get => _microStrength; set => SetField(ref _microStrength, value); }
        public double AccentDensity { get => _accentDensity; set => SetField(ref _accentDensity, value); }
        public double AccentSize { get => _accentSize; set => SetField(ref _accentSize, value); }
        public double ColorVariation { get => _colorVariation; set => SetField(ref _colorVariation, value); }
        public double GrassBladeDensity { get => _grassBladeDensity; set => SetField(ref _grassBladeDensity, value); }
        public double GrassBladeHeight { get => _grassBladeHeight; set => SetField(ref _grassBladeHeight, value); }
        public double GrassPatchiness { get => _grassPatchiness; set => SetField(ref _grassPatchiness, value); }
        public GrassPreset GrassPreset { get => _grassPreset; set => SetField(ref _grassPreset, value); }
        public GrassFlowerMode GrassFlowerMode { get => _grassFlowerMode; set => SetField(ref _grassFlowerMode, value); }
        public double FlowerDensity { get => _flowerDensity; set => SetField(ref _flowerDensity, value); }
        public double FlowerSize { get => _flowerSize; set => SetField(ref _flowerSize, value); }
        public System.Windows.Media.Color FlowerColor { get => _flowerColor; set => SetField(ref _flowerColor, value); }
        // New: support multiple flower colors with frequency weights
        public class FlowerColorEntry : INotifyPropertyChanged
        {
            private System.Windows.Media.Color _color;
            private double _weight;

            public FlowerColorEntry(System.Windows.Media.Color color, double weight = 1.0)
            {
                _color = color;
                _weight = weight;
            }

            public System.Windows.Media.Color Color { get => _color; set { if (_color != value) { _color = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color))); } } }
            public double Weight { get => _weight; set { if (_weight != value) { _weight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Weight))); } } }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private ObservableCollection<FlowerColorEntry> _flowerColors;
        public ObservableCollection<FlowerColorEntry> FlowerColors { get => _flowerColors; set => SetField(ref _flowerColors, value); }

        public TileProperties()
        {
            // initialize flower colors collection for backward compatibility
            _flowerColors = new ObservableCollection<FlowerColorEntry>() { new FlowerColorEntry(_flowerColor, 1.0) };
        }
        public double StoneCrackDensity { get => _stoneCrackDensity; set => SetField(ref _stoneCrackDensity, value); }
        public double StoneMossDensity { get => _stoneMossDensity; set => SetField(ref _stoneMossDensity, value); }
        public int StoneHorizontalTileCount { get => _stoneHorizontalTileCount; set => SetField(ref _stoneHorizontalTileCount, value); }
        public int StoneVerticalTileCount { get => _stoneVerticalTileCount; set => SetField(ref _stoneVerticalTileCount, value); }
        public double WaterWaveScale { get => _waterWaveScale; set => SetField(ref _waterWaveScale, value); }
        public double WaterWaveChoppiness { get => _waterWaveChoppiness; set => SetField(ref _waterWaveChoppiness, value); }
        public double WaterFoamDensity { get => _waterFoamDensity; set => SetField(ref _waterFoamDensity, value); }
        public double WaterFoamSize { get => _waterFoamSize; set => SetField(ref _waterFoamSize, value); }
        public double WaterDepthVariation { get => _waterDepthVariation; set => SetField(ref _waterDepthVariation, value); }
        public double SandDuneScale { get => _sandDuneScale; set => SetField(ref _sandDuneScale, value); }
        public double SandDuneSharpness { get => _sandDuneSharpness; set => SetField(ref _sandDuneSharpness, value); }
        public double SandRippleStrength { get => _sandRippleStrength; set => SetField(ref _sandRippleStrength, value); }
        public double SandPebbleDensity { get => _sandPebbleDensity; set => SetField(ref _sandPebbleDensity, value); }
        public double SandPebbleSize { get => _sandPebbleSize; set => SetField(ref _sandPebbleSize, value); }
        public double RoadWidth { get => _roadWidth; set => SetField(ref _roadWidth, value); }
        public double RoadEdgeRoughness { get => _roadEdgeRoughness; set => SetField(ref _roadEdgeRoughness, value); }
        public double RoadRutDepth { get => _roadRutDepth; set => SetField(ref _roadRutDepth, value); }
        public double RoadGravelDensity { get => _roadGravelDensity; set => SetField(ref _roadGravelDensity, value); }
        public double RoadShoulderWidth { get => _roadShoulderWidth; set => SetField(ref _roadShoulderWidth, value); }
        public double RoadShoulderRoughness { get => _roadShoulderRoughness; set => SetField(ref _roadShoulderRoughness, value); }
        public RoadLayout RoadLayout { get => _roadLayout; set => SetField(ref _roadLayout, value); }
        public double RoadCornerRoundness { get => _roadCornerRoundness; set => SetField(ref _roadCornerRoundness, value); }
        public double WaterCurrentDirection { get => _waterCurrentDirection; set => SetField(ref _waterCurrentDirection, value); }
        public double WaterCurrentStrength { get => _waterCurrentStrength; set => SetField(ref _waterCurrentStrength, value); }
        public double SandRippleDirection { get => _sandRippleDirection; set => SetField(ref _sandRippleDirection, value); }
        public double SandRippleScale { get => _sandRippleScale; set => SetField(ref _sandRippleScale, value); }
        public double RoadCenterLine { get => _roadCenterLine; set => SetField(ref _roadCenterLine, value); }
        public double RoadCenterLineRoughness { get => _roadCenterLineRoughness; set => SetField(ref _roadCenterLineRoughness, value); }
        public double ErosionStrength { get => _erosionStrength; set => SetField(ref _erosionStrength, value); }
        public double ErosionScale { get => _erosionScale; set => SetField(ref _erosionScale, value); }
        public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }
        public LayerBlendMode BlendMode { get => _blendMode; set => SetField(ref _blendMode, value); }
        public bool NineSliceEnabled { get => _nineSliceEnabled; set => SetField(ref _nineSliceEnabled, value); }
        public double NineSliceEdgeSize { get => _nineSliceEdgeSize; set => SetField(ref _nineSliceEdgeSize, value); }
        public double NineSliceMaskSize { get => _nineSliceMaskSize; set => SetField(ref _nineSliceMaskSize, value); }
        public double NineSliceEdgeFeather { get => _nineSliceEdgeFeather; set => SetField(ref _nineSliceEdgeFeather, value); }
        public bool MaskEnabled { get => _maskEnabled; set => SetField(ref _maskEnabled, value); }
        public bool MaskInvert { get => _maskInvert; set => SetField(ref _maskInvert, value); }
        public MaskElement MaskElement { get => _maskElement; set => SetField(ref _maskElement, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // If the field is a double, clamp its precision to 0.001 to avoid long floating representations in the UI
            if (typeof(T) == typeof(double))
            {
                var d = (double)(object)value!;
                d = Math.Round(d, 3);
                var rounded = (T)(object)d;
                if (EqualityComparer<T>.Default.Equals(field, rounded)) return false;
                field = rounded;
                OnPropertyChanged(propertyName);
                return true;
            }

            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void CopyFrom(TileProperties other)
        {
            if (other == null) return;
            Seed = other.Seed;
            Scale = other.Scale;
            Octaves = other.Octaves;
            Persistence = other.Persistence;
            Lacunarity = other.Lacunarity;
            DetailDensity = other.DetailDensity;
            EdgeStrength = other.EdgeStrength;
            MacroScale = other.MacroScale;
            MacroStrength = other.MacroStrength;
            MicroScale = other.MicroScale;
            MicroStrength = other.MicroStrength;
            AccentDensity = other.AccentDensity;
            AccentSize = other.AccentSize;
            ColorVariation = other.ColorVariation;
            GrassBladeDensity = other.GrassBladeDensity;
            GrassBladeHeight = other.GrassBladeHeight;
            GrassPatchiness = other.GrassPatchiness;
            GrassPreset = other.GrassPreset;
            GrassFlowerMode = other.GrassFlowerMode;
            FlowerDensity = other.FlowerDensity;
            FlowerSize = other.FlowerSize;
            FlowerColor = other.FlowerColor;
            // copy flower colors collection (preserve weights)
            if (other.FlowerColors != null)
            {
                FlowerColors = new ObservableCollection<FlowerColorEntry>();
                foreach (var e in other.FlowerColors)
                {
                    FlowerColors.Add(new FlowerColorEntry(e.Color, e.Weight));
                }
            }
            StoneCrackDensity = other.StoneCrackDensity;
            StoneMossDensity = other.StoneMossDensity;
            StoneHorizontalTileCount = other.StoneHorizontalTileCount;
            StoneVerticalTileCount = other.StoneVerticalTileCount;
            WaterWaveScale = other.WaterWaveScale;
            WaterWaveChoppiness = other.WaterWaveChoppiness;
            WaterFoamDensity = other.WaterFoamDensity;
            WaterFoamSize = other.WaterFoamSize;
            WaterDepthVariation = other.WaterDepthVariation;
            SandDuneScale = other.SandDuneScale;
            SandDuneSharpness = other.SandDuneSharpness;
            SandRippleStrength = other.SandRippleStrength;
            SandPebbleDensity = other.SandPebbleDensity;
            SandPebbleSize = other.SandPebbleSize;
            RoadWidth = other.RoadWidth;
            RoadEdgeRoughness = other.RoadEdgeRoughness;
            RoadRutDepth = other.RoadRutDepth;
            RoadGravelDensity = other.RoadGravelDensity;
            RoadShoulderWidth = other.RoadShoulderWidth;
            RoadShoulderRoughness = other.RoadShoulderRoughness;
            RoadLayout = other.RoadLayout;
            RoadCornerRoundness = other.RoadCornerRoundness;
            WaterCurrentDirection = other.WaterCurrentDirection;
            WaterCurrentStrength = other.WaterCurrentStrength;
            SandRippleDirection = other.SandRippleDirection;
            SandRippleScale = other.SandRippleScale;
            RoadCenterLine = other.RoadCenterLine;
            RoadCenterLineRoughness = other.RoadCenterLineRoughness;
            ErosionStrength = other.ErosionStrength;
            ErosionScale = other.ErosionScale;
            Opacity = other.Opacity;
            BlendMode = other.BlendMode;
            NineSliceEnabled = other.NineSliceEnabled;
            NineSliceEdgeSize = other.NineSliceEdgeSize;
            NineSliceMaskSize = other.NineSliceMaskSize;
            NineSliceEdgeFeather = other.NineSliceEdgeFeather;
            MaskEnabled = other.MaskEnabled;
            MaskInvert = other.MaskInvert;
            MaskElement = other.MaskElement;
        }

        public TileProperties Clone()
        {
            var t = new TileProperties();
            t.CopyFrom(this);
            return t;
        }
    }

}
