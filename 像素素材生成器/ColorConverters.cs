using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color c)
            {
                return new SolidColorBrush(c);
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush b)
            {
                return b.Color;
            }
            return Colors.Transparent;
        }
    }

    public class ColorToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color c)
            {
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#000000";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                try
                {
                    if (s.StartsWith("#")) s = s.Substring(1);
                    if (s.Length == 6)
                    {
                        byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                        byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                        byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                        return Color.FromRgb(r, g, b);
                    }
                }
                catch { }
            }
            return Colors.Transparent;
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
