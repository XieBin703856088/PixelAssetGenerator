using System;
using System.Globalization;
using System.Windows.Data;

namespace PixelAssetGenerator
{
    // Converter that returns the inverse of a double value (1.0 / value).
    // Used to bind a DrawingBrush transform so grid strokes remain thin when the preview is scaled.
    public class InverseScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && Math.Abs(d) > 1e-6)
            {
                return 1.0 / d;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not required for our usage
            return Binding.DoNothing;
        }
    }
}
