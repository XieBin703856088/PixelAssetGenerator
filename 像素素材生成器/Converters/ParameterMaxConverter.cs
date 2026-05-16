using System;
using System.Globalization;
using System.Windows.Data;

namespace PixelAssetGenerator
{
    // Converter that returns an appropriate maximum value for a given parameter name and tile type.
    // Usage: bind Slider.Maximum to SelectedNode.TileType with ConverterParameter set to the property name.
    public sealed class ParameterMaxConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value is expected to be TileType (or null), parameter is the property name string
            var propName = parameter as string ?? string.Empty;

            // Default max
            double max = 1.0;

            // Some parameters need larger ranges. Map by property name.
            switch (propName)
            {
                case "Scale":
                case "MicroScale":
                case "MacroScale":
                    max = 10.0;
                    break;
                case "FlowerSize":
                    max = 10.0;
                    break;
                case "AccentSize":
                case "NineSliceEdgeSize":
                    max = 2.0;
                    break;
                case "WaterWaveScale":
                    max = 2.0;
                    break;
                default:
                    max = 1.0;
                    break;
            }

            return max;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
