using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator
{
    public partial class ColorPickerDialog : Window
    {
        // ── Layout constants (must match XAML) ───────────────────────────
        private const int CanvasSize = 220;
        private const int OuterRadius = 106;  // hue ring outer edge
        private const int InnerRadius = 78;   // hue ring inner edge
        private const int SvSize = 110;       // SV square side length
        private const int SvOffset = 55;      // SV square top-left from canvas origin

        // ── HSV state ────────────────────────────────────────────────────
        private double _hue;         // 0 – 360
        private double _saturation;  // 0 – 1
        private double _value;       // 0 – 1

        private bool _updating;
        private bool _isDraggingHue;
        private bool _isDraggingSv;

        /// <summary>The color confirmed by the user when clicking OK.</summary>
        public Color SelectedColor { get; private set; }

        /// <summary>Fired whenever the color changes during interaction, enabling live preview in the caller.</summary>
        public event Action<Color>? PreviewColorChanged;

        public ColorPickerDialog(Color initialColor)
        {
            InitializeComponent();
            SelectedColor = initialColor;
            (_hue, _saturation, _value) = RgbToHsv(initialColor);
            HueRingImage.Source = BuildHueRing(CanvasSize, OuterRadius, InnerRadius);
            RebuildSvSquare();
            UpdateIndicators();
            SyncHexAndPreview();
        }

        // ── Bitmap builders ──────────────────────────────────────────────

        // Builds the hue ring. Pixels outside [innerR, outerR] are transparent.
        // A 1-px soft fade is applied at both edges for anti-aliased appearance.
        private static WriteableBitmap BuildHueRing(int size, int outerR, int innerR)
        {
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4];
            double cx = size / 2.0;
            double cy = size / 2.0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double r = Math.Sqrt(dx * dx + dy * dy);

                    // Soft edge: 1-px fade at inner and outer boundaries
                    double outerFade = Math.Clamp(outerR + 0.5 - r, 0.0, 1.0);
                    double innerFade = Math.Clamp(r - innerR + 0.5, 0.0, 1.0);
                    double alpha = outerFade * innerFade;
                    if (alpha <= 0.0) continue;

                    double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                    if (angle < 0.0) angle += 360.0;

                    var c = HsvToRgb(angle, 1.0, 1.0);
                    int idx = (y * size + x) * 4;
                    pixels[idx]     = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = (byte)(alpha * 255);
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return wb;
        }

        // Builds the saturation–value square for the given hue.
        // X axis = saturation (0→1), Y axis = value (1→0, bright at top).
        private static WriteableBitmap BuildSvSquare(int size, double hue)
        {
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4];
            double invSize = 1.0 / (size - 1);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double s = x * invSize;
                    double v = 1.0 - y * invSize;
                    var c = HsvToRgb(hue, s, v);
                    int idx = (y * size + x) * 4;
                    pixels[idx]     = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = 255;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return wb;
        }

        private void RebuildSvSquare() => SvSquareImage.Source = BuildSvSquare(SvSize, _hue);

        // ── Indicator positioning ────────────────────────────────────────

        private void UpdateIndicators()
        {
            UpdateHueIndicator();
            UpdateSvIndicator();
        }

        private void UpdateHueIndicator()
        {
            double angle = _hue * (Math.PI / 180.0);
            double r = (OuterRadius + InnerRadius) / 2.0;
            double x = CanvasSize / 2.0 + r * Math.Cos(angle) - HueSelector.Width  / 2.0;
            double y = CanvasSize / 2.0 + r * Math.Sin(angle) - HueSelector.Height / 2.0;
            Canvas.SetLeft(HueSelector,      x);
            Canvas.SetTop(HueSelector,       y);
            Canvas.SetLeft(HueSelectorOuter, x);
            Canvas.SetTop(HueSelectorOuter,  y);
        }

        private void UpdateSvIndicator()
        {
            double x = SvOffset + _saturation * (SvSize - 1) - SvSelector.Width  / 2.0;
            double y = SvOffset + (1 - _value) * (SvSize - 1) - SvSelector.Height / 2.0;
            Canvas.SetLeft(SvSelector,      x);
            Canvas.SetTop(SvSelector,       y);
            Canvas.SetLeft(SvSelectorOuter, x);
            Canvas.SetTop(SvSelectorOuter,  y);
        }

        // ── Mouse interaction ────────────────────────────────────────────

        private void ColorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ColorCanvas);
            if (HitTestHueRing(pos))
            {
                _isDraggingHue = true;
                ApplyHueFromPoint(pos);
                ColorCanvas.CaptureMouse();
            }
            else if (HitTestSvSquare(pos))
            {
                _isDraggingSv = true;
                ApplySvFromPoint(pos);
                ColorCanvas.CaptureMouse();
            }
        }

        private void ColorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(ColorCanvas);
            if (_isDraggingHue)
                ApplyHueFromPoint(pos);
            else if (_isDraggingSv)
                ApplySvFromPoint(pos);
        }

        private void ColorCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingHue = false;
            _isDraggingSv = false;
            ColorCanvas.ReleaseMouseCapture();
        }

        // Hit-test with an 8-px tolerance band on each edge so the ring is easy to grab.
        private static bool HitTestHueRing(Point p)
        {
            double dx = p.X - CanvasSize / 2.0;
            double dy = p.Y - CanvasSize / 2.0;
            double r2 = dx * dx + dy * dy;
            double lo = InnerRadius - 8;
            double hi = OuterRadius + 8;
            return r2 >= lo * lo && r2 <= hi * hi;
        }

        private static bool HitTestSvSquare(Point p)
            => p.X >= SvOffset && p.X <= SvOffset + SvSize
            && p.Y >= SvOffset && p.Y <= SvOffset + SvSize;

        private void ApplyHueFromPoint(Point p)
        {
            double dx = p.X - CanvasSize / 2.0;
            double dy = p.Y - CanvasSize / 2.0;
            double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
            if (angle < 0.0) angle += 360.0;
            _hue = angle;
            RebuildSvSquare();
            UpdateIndicators();
            CommitColor();
        }

        private void ApplySvFromPoint(Point p)
        {
            _saturation = Math.Clamp((p.X - SvOffset) / (SvSize - 1), 0.0, 1.0);
            _value      = Math.Clamp(1.0 - (p.Y - SvOffset) / (SvSize - 1), 0.0, 1.0);
            UpdateSvIndicator();
            CommitColor();
        }

        // ── Color commit + hex sync ──────────────────────────────────────

        private void CommitColor()
        {
            SelectedColor = HsvToRgb(_hue, _saturation, _value);
            SyncHexAndPreview();
            PreviewColorChanged?.Invoke(SelectedColor);
        }

        private void SyncHexAndPreview()
        {
            if (_updating) return;
            _updating = true;
            var c = SelectedColor;
            HexTextBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            PreviewBorder.Background = new SolidColorBrush(c);
            _updating = false;
        }

        private void HexTextBox_LostFocus(object sender, RoutedEventArgs e) => TryApplyHex();

        private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Return)
                TryApplyHex();
        }

        private void TryApplyHex()
        {
            if (_updating) return;
            var text = HexTextBox.Text.TrimStart('#');
            if (text.Length == 6
                && byte.TryParse(text[0..2], NumberStyles.HexNumber, null, out var r)
                && byte.TryParse(text[2..4], NumberStyles.HexNumber, null, out var g)
                && byte.TryParse(text[4..6], NumberStyles.HexNumber, null, out var b))
            {
                var c = Color.FromRgb(r, g, b);
                (_hue, _saturation, _value) = RgbToHsv(c);
                SelectedColor = c;
                RebuildSvSquare();
                UpdateIndicators();
                _updating = true;
                PreviewBorder.Background = new SolidColorBrush(c);
                _updating = false;
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── HSV ↔ RGB conversions ────────────────────────────────────────

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            double c = v * s;
            double x = c * (1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0));
            double m = v - c;
            double r, g, b;
            if      (h < 60)  { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else              { r = c; g = 0; b = x; }
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255.0),
                (byte)Math.Round((g + m) * 255.0),
                (byte)Math.Round((b + m) * 255.0));
        }

        private static (double h, double s, double v) RgbToHsv(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double v = max;
            double s = max == 0.0 ? 0.0 : delta / max;
            double h = 0.0;
            if (delta > 0.0)
            {
                if      (max == r) h = 60.0 * (((g - b) / delta % 6.0 + 6.0) % 6.0);
                else if (max == g) h = 60.0 * ((b - r) / delta + 2.0);
                else               h = 60.0 * ((r - g) / delta + 4.0);
            }
            return (h, s, v);
        }
    }
}
