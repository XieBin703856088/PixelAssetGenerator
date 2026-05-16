using System;
using System.Globalization;
using System.Windows.Data;

namespace PixelAssetGenerator
{
    /// <summary>
    /// Converts NodeThumbnailSize (a double, e.g. 64) to the StackPanel width
    /// for the tile layout. The width accommodates the label text below the thumbnail.
    /// </summary>
    public class ThumbnailSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d > 0)
                return d + 20;
            return 84.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
