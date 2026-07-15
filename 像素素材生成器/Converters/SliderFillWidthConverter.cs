using System;
using System.Globalization;
using System.Windows.Data;

namespace PixelAssetGenerator.Converters
{
    /// <summary>
    /// Multi-value converter: computes fill width for a Slider track while honoring
    /// non-zero minimum values.
    /// Bindings order: [0]=Value, [1]=Minimum, [2]=Maximum, [3]=track width.
    /// </summary>
    public class SliderFillWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4) return 0d;
            var value = System.Convert.ToDouble(values[0]);
            var min = System.Convert.ToDouble(values[1]);
            var max = System.Convert.ToDouble(values[2]);
            var totalWidth = System.Convert.ToDouble(values[3]);
            var range = max - min;
            if (range <= 0 || totalWidth <= 0) return 0d;
            var ratio = Math.Clamp((value - min) / range, 0, 1);
            return ratio * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
