using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PixelAssetGenerator
{
    public partial class SplashWindow : Window
    {
        private readonly DispatcherTimer _animTimer;
        private double _progress;
        private double _scanY = 0;
        private bool _scanDown = true;
        private int _pixelStep;
        private double _bgOffsetX = 0;
        private double _bgOffsetY = 0;

        public SplashWindow()
        {
            InitializeComponent();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(30) // ~33fps
            };
            _animTimer.Tick += AnimTimer_Tick;
        }

        public void ReportProgress(double value, string status, string detail = "")
        {
            _progress = Math.Clamp(value, 0.0, 1.0);
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = status;
                DetailTextBlock.Text = detail;
                PercentTextBlock.Text = $"{(int)(_progress * 100)}%";

                var parentWidth = ProgressFillBorder.Parent is FrameworkElement fe ? fe.ActualWidth : 380;
                var targetWidth = parentWidth * _progress;
                _pixelStep = (int)(targetWidth / 4) * 4;
                ProgressFillBorder.Width = _pixelStep;
            }, DispatcherPriority.Normal);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Logo entrance: bounce scale
            var bounce = new DoubleAnimation
            {
                From = 0.6,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(600),
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }
            };
            LogoCanvas.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new ScaleTransform(0.6, 0.6, 32, 32),
                    new TranslateTransform(0, 0)
                }
            };
            var scaleTransform = (ScaleTransform)((TransformGroup)LogoCanvas.RenderTransform).Children[0];
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);

            // Logo opacity fade-in
            LogoCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));

            // Title fade-in
            TitleBlock.Opacity = 0;
            TitleBlock.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            // Scan-line pulsing glow
            ScanLine.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 0.5, TimeSpan.FromMilliseconds(800))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });

            _animTimer.Start();
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            // ── Scan line ──
            if (_scanDown)
            {
                _scanY += 1.5;
                if (_scanY >= 56) _scanDown = false;
            }
            else
            {
                _scanY -= 1.5;
                if (_scanY <= 4) _scanDown = true;
            }
            Canvas.SetTop(ScanLine, _scanY);

            // ── Background grid scroll ──
            _bgOffsetX -= 0.15;
            _bgOffsetY -= 0.10;
            BgPatternHost.RenderTransform = new TranslateTransform(_bgOffsetX, _bgOffsetY);

            // ── Background dots scroll (opposite direction for parallax) ──
            BgDotHost.RenderTransform = new TranslateTransform(_bgOffsetX * 0.5, _bgOffsetY * 0.3);

            // ── Progress bar pixel grid overlay ──
            if (_pixelStep > 0)
            {
                var dv = new DrawingGroup();
                var dc = dv.Open();
                for (int x = 0; x < _pixelStep; x += 8)
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 11, 14, 20)), null,
                        new Rect(x, 0, 4, 14));
                }
                dc.Close();
                dv.Freeze();
                PixelGridOverlay.Fill = new DrawingBrush(dv)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    TileMode = TileMode.None
                };
            }
        }
    }
}
