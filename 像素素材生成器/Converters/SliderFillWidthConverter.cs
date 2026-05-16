using System;
using System.Globalization;
using System.Windows.Data;

namespace PixelAssetGenerator.Converters
{
    /// <summary>
    /// Multi-value converter: computes fill width for Slider track = (Value / Max) * totalWidth.
    /// Bindings order: [0]=Value, [1]=Maximum, [2]=ActualWidth of track background.
    /// </summary>
    public class SliderFillWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return 0d;
            var value = System.Convert.ToDouble(values[0]);
            var max = System.Convert.ToDouble(values[1]);
            var totalWidth = System.Convert.ToDouble(values[2]);
            if (max <= 0 || totalWidth <= 0) return 0d;
            var ratio = Math.Clamp(value / max, 0, 1);
            return ratio * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
