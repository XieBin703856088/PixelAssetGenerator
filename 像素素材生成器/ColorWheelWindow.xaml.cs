using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator
{
    public partial class ColorWheelWindow : Window
    {
        // 色环几何常量
        private const int WheelSize = 280;
        private const double Cx = WheelSize / 2.0;
        private const double Cy = WheelSize / 2.0;
        private const double OuterR = 136.0;
        private const double InnerR = 108.0;
        private const double MidR = (OuterR + InnerR) / 2.0;

        // SV 方块：内切于色相环内圈，留出少许间隙
        private static readonly double SqSide = Math.Floor(InnerR * Math.Sqrt(2) - 8);
        private static readonly double SqHalf = SqSide / 2.0;
        private static readonly double SqLeft = Cx - SqHalf;
        private static readonly double SqTop = Cy - SqHalf;

        private double _h, _s, _v;
        private Color _initialColor;
        private bool _loaded;
        private bool _suppressTextUpdates;

        private WriteableBitmap _ringBmp = null!;
        private WriteableBitmap _svBmp = null!;

        private enum DragMode { None, Ring, Square }
        private DragMode _drag = DragMode.None;

        public Color SelectedColor { get; private set; } = Colors.White;
        // Raised whenever the user changes the color interactively (live preview)
        public event Action<System.Windows.Media.Color>? ColorChanged;

        public ColorWheelWindow(Color initial)
        {
            InitializeComponent();
            _initialColor = initial;
            SelectedColor = initial;
            var (h, s, v) = RgbToHsv(initial.R, initial.G, initial.B);
            _h = h; _s = s; _v = v;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 色相环（静态，只渲染一次）
            _ringBmp = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
            RenderHueRing();
            HueRingImage.Source = _ringBmp;

            // SV 方块
            int sq = (int)SqSide;
            _svBmp = new WriteableBitmap(sq, sq, 96, 96, PixelFormats.Bgra32, null);
            RenderSVSquare();
            SVSquareImage.Source = _svBmp;
            SVSquareImage.Width = sq;
            SVSquareImage.Height = sq;
            Canvas.SetLeft(SVSquareImage, SqLeft);
            Canvas.SetTop(SVSquareImage, SqTop);

            // SV 边框定位
            Canvas.SetLeft(SVSquareBorder, SqLeft - 0.5);
            Canvas.SetTop(SVSquareBorder, SqTop - 0.5);
            SVSquareBorder.Width = sq + 1;
            SVSquareBorder.Height = sq + 1;

            // 原色预览
            OldColorRect.Fill = new SolidColorBrush(_initialColor);

            _loaded = true;
            UpdateAll();
        }

        #region 渲染

        private void RenderHueRing()
        {
            int w = _ringBmp.PixelWidth;
            int h = _ringBmp.PixelHeight;
            byte[] pixels = new byte[w * h * 4];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = x - Cx;
                    double dy = y - Cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist >= InnerR - 1 && dist <= OuterR + 1)
                    {
                        // 屏幕角度 → 色相（PS 惯例：顶部 = 0°红色，顺时针递增）
                        double screenAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                        if (screenAngle < 0) screenAngle += 360;
                        double hue = (screenAngle + 90) % 360;

                        var (r, g, b) = HsvToRgb(hue, 1, 1);
                        int idx = (y * w + x) * 4;
                        pixels[idx + 0] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;

                        // 边缘抗锯齿
                        double alpha = 1.0;
                        if (dist < InnerR)
                            alpha = dist - (InnerR - 1);
                        else if (dist > OuterR)
                            alpha = (OuterR + 1) - dist;
                        pixels[idx + 3] = (byte)(255 * Math.Clamp(alpha, 0, 1));
                    }
                }
            }

            _ringBmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        }

        private void RenderSVSquare()
        {
            int size = _svBmp.PixelWidth;
            byte[] pixels = new byte[size * size * 4];

            for (int y = 0; y < size; y++)
            {
                double v = 1.0 - (double)y / (size - 1);
                for (int x = 0; x < size; x++)
                {
                    double s = (double)x / (size - 1);
                    var (r, g, b) = HsvToRgb(_h, s, v);
                    int idx = (y * size + x) * 4;
                    pixels[idx + 0] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
                }
            }

            _svBmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        }

        #endregion

        #region 鼠标交互

        private void WheelCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(WheelCanvas);
            double dx = pos.X - Cx;
            double dy = pos.Y - Cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist >= InnerR - 4 && dist <= OuterR + 4)
            {
                _drag = DragMode.Ring;
                UpdateHueFromPoint(pos);
                WheelCanvas.CaptureMouse();
                e.Handled = true;
            }
            else if (pos.X >= SqLeft && pos.X <= SqLeft + SqSide &&
                     pos.Y >= SqTop && pos.Y <= SqTop + SqSide)
            {
                _drag = DragMode.Square;
                UpdateSVFromPoint(pos);
                WheelCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void WheelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag == DragMode.None) return;
            Point pos = e.GetPosition(WheelCanvas);

            if (_drag == DragMode.Ring)
                UpdateHueFromPoint(pos);
            else if (_drag == DragMode.Square)
                UpdateSVFromPoint(pos);
        }

        private void WheelCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag != DragMode.None)
            {
                _drag = DragMode.None;
                WheelCanvas.ReleaseMouseCapture();
            }
        }

        private void UpdateHueFromPoint(Point pt)
        {
            double dx = pt.X - Cx;
            double dy = pt.Y - Cy;
            double screenAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (screenAngle < 0) screenAngle += 360;
            _h = (screenAngle + 90) % 360;
            RenderSVSquare();
            UpdateAll();
        }

        private void UpdateSVFromPoint(Point pt)
        {
            _s = Math.Clamp((pt.X - SqLeft) / SqSide, 0, 1);
            _v = Math.Clamp(1 - (pt.Y - SqTop) / SqSide, 0, 1);
            UpdateAll();
        }

        #endregion

        #region 更新

        private void UpdateAll()
        {
            if (!_loaded) return;

            var (r, g, b) = HsvToRgb(_h, _s, _v);
            SelectedColor = Color.FromRgb(r, g, b);

            // Notify listeners for live-preview scenarios
            try { ColorChanged?.Invoke(SelectedColor); } catch { }

            // 新色预览
            NewColorRect.Fill = new SolidColorBrush(SelectedColor);

            // 色相环标记：定位到环中线对应色相角度
            double hueRad = (_h - 90) * Math.PI / 180.0;
            double mw = RingMarker.Width;
            double mh = RingMarker.Height;
            Canvas.SetLeft(RingMarker, Cx + MidR * Math.Cos(hueRad) - mw / 2);
            Canvas.SetTop(RingMarker, Cy + MidR * Math.Sin(hueRad) - mh / 2);
            var hueRgb = HsvToRgb(_h, 1, 1);
            RingMarker.Fill = new SolidColorBrush(Color.FromRgb(hueRgb.r, hueRgb.g, hueRgb.b));

            // SV 标记：定位到方块中对应 S/V 坐标
            double sw = SVMarker.Width;
            double sh = SVMarker.Height;
            Canvas.SetLeft(SVMarker, SqLeft + _s * SqSide - sw / 2);
            Canvas.SetTop(SVMarker, SqTop + (1 - _v) * SqSide - sh / 2);

            // 文本字段
            if (!_suppressTextUpdates)
            {
                _suppressTextUpdates = true;
                HexTextBox.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
                RTextBox.Text = SelectedColor.R.ToString();
                GTextBox.Text = SelectedColor.G.ToString();
                BTextBox.Text = SelectedColor.B.ToString();
                HTextBox.Text = ((int)Math.Round(_h)).ToString();
                STextBox.Text = ((int)Math.Round(_s * 100)).ToString();
                VTextBox.Text = ((int)Math.Round(_v * 100)).ToString();
                _suppressTextUpdates = false;
            }
        }

        #endregion

        #region 文本输入

        private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHex();
        }

        private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHex();
        }

        private void ApplyHex()
        {
            if (_suppressTextUpdates) return;
            try
            {
                string hex = HexTextBox.Text.Trim().TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    var (h, s, v) = RgbToHsv(r, g, b);
                    _h = h; _s = s; _v = v;
                    RenderSVSquare();
                    UpdateAll();
                }
            }
            catch { }
        }

        private void RGBTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyRGB();
        }

        private void RGBTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyRGB();
        }

        private void ApplyRGB()
        {
            if (_suppressTextUpdates) return;
            if (byte.TryParse(RTextBox.Text, out byte r) &&
                byte.TryParse(GTextBox.Text, out byte g) &&
                byte.TryParse(BTextBox.Text, out byte b))
            {
                var (h, s, v) = RgbToHsv(r, g, b);
                _h = h; _s = s; _v = v;
                RenderSVSquare();
                UpdateAll();
            }
        }

        private void HSVTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHSV();
        }

        private void HSVTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHSV();
        }

        private void ApplyHSV()
        {
            if (_suppressTextUpdates) return;
            if (double.TryParse(HTextBox.Text, out double h) &&
                double.TryParse(STextBox.Text, out double s) &&
                double.TryParse(VTextBox.Text, out double v))
            {
                _h = Math.Clamp(h, 0, 360);
                _s = Math.Clamp(s / 100.0, 0, 1);
                _v = Math.Clamp(v / 100.0, 0, 1);
                RenderSVSquare();
                UpdateAll();
            }
        }

        #endregion

        #region 对话框按钮

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        #region HSV ↔ RGB

        private static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;
            double h = 0;
            if (delta > 0)
            {
                if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
                else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
                else h = 60 * (((rd - gd) / delta) + 4);
            }
            if (h < 0) h += 360;
            double s = max == 0 ? 0 : delta / max;
            double v = max;
            return (h, s, v);
        }

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            var c = v * s;
            var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            var m = v - c;
            double rd = 0, gd = 0, bd = 0;
            if (h < 60) { rd = c; gd = x; bd = 0; }
            else if (h < 120) { rd = x; gd = c; bd = 0; }
            else if (h < 180) { rd = 0; gd = c; bd = x; }
            else if (h < 240) { rd = 0; gd = x; bd = c; }
            else if (h < 300) { rd = x; gd = 0; bd = c; }
            else { rd = c; gd = 0; bd = x; }
            byte r = (byte)Math.Round((rd + m) * 255);
            byte g = (byte)Math.Round((gd + m) * 255);
            byte b = (byte)Math.Round((bd + m) * 255);
            return (r, g, b);
        }

        #endregion
    }
}
