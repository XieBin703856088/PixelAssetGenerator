using System;
using System.Windows;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    // A lightweight element that renders an infinite grid based on world->screen transform
    public class InfiniteGrid : FrameworkElement
    {
        public InfiniteGrid()
        {
            // improve crispness by snapping to device pixels and disabling anti-aliasing for edges
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }
        // Note: the grid axes follow the world origin mapping (OffsetX/OffsetY) so the
        // control doesn't keep a separate one-time screen anchor. The host should set
        // OffsetX/OffsetY to center the content if a centered initial view is desired.
        // Scale (world -> screen)
        public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
            nameof(Scale), typeof(double), typeof(InfiniteGrid), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Scale
        {
            get => (double)GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }

        public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register(
            nameof(OffsetX), typeof(double), typeof(InfiniteGrid), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double OffsetX
        {
            get => (double)GetValue(OffsetXProperty);
            set => SetValue(OffsetXProperty, value);
        }

        public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
            nameof(OffsetY), typeof(double), typeof(InfiniteGrid), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double OffsetY
        {
            get => (double)GetValue(OffsetYProperty);
            set => SetValue(OffsetYProperty, value);
        }

        public static readonly DependencyProperty MajorSpacingProperty = DependencyProperty.Register(
            nameof(MajorSpacing), typeof(double), typeof(InfiniteGrid), new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double MajorSpacing
        {
            get => (double)GetValue(MajorSpacingProperty);
            set => SetValue(MajorSpacingProperty, value);
        }

        public static readonly DependencyProperty MinorSpacingProperty = DependencyProperty.Register(
            nameof(MinorSpacing), typeof(double), typeof(InfiniteGrid), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double MinorSpacing
        {
            get => (double)GetValue(MinorSpacingProperty);
            set => SetValue(MinorSpacingProperty, value);
        }

        public static readonly DependencyProperty MajorBrushProperty = DependencyProperty.Register(
            nameof(MajorBrush), typeof(Brush), typeof(InfiniteGrid), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? MajorBrush
        {
            get => (Brush?)GetValue(MajorBrushProperty);
            set => SetValue(MajorBrushProperty, value);
        }

        public static readonly DependencyProperty MinorBrushProperty = DependencyProperty.Register(
            nameof(MinorBrush), typeof(Brush), typeof(InfiniteGrid), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? MinorBrush
        {
            get => (Brush?)GetValue(MinorBrushProperty);
            set => SetValue(MinorBrushProperty, value);
        }

        // Whether to draw bold center axes (vertical/horizontal). Default false to avoid prominent axes.
        public static readonly DependencyProperty ShowAxesProperty = DependencyProperty.Register(
            nameof(ShowAxes), typeof(bool), typeof(InfiniteGrid), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowAxes
        {
            get => (bool)GetValue(ShowAxesProperty);
            set => SetValue(ShowAxesProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = (float)ActualWidth;
            var height = (float)ActualHeight;
            if (width <= 0 || height <= 0) return;

            var scale = Math.Max(0.0001, Scale);

            // Calculate visible world bounds (screen -> world)
            // screenX = worldX * scale + offsetX => worldX = (screenX - offsetX) / scale
            double leftWorld = (0 - OffsetX) / scale;
            double topWorld = (0 - OffsetY) / scale;
            double rightWorld = (width - OffsetX) / scale;
            double bottomWorld = (height - OffsetY) / scale;

            // Adaptive spacing in world units to keep screen spacing reasonable
            double minor = MinorSpacing;
            double major = MajorSpacing;

            // Ensure minor < major
            if (minor <= 0) minor = 16;
            if (major <= 0) major = minor * 4;
            if (minor >= major) minor = Math.Max(1, major / 4.0);

            // Compute screen spacing for minor; if too small, increase world spacing
            // Use a slightly larger threshold so we reduce subdivisions earlier when zoomed out
            double screenMinor = minor * scale;
            const double minScreenSpacing = 10.0;
            while (screenMinor < minScreenSpacing)
            {
                minor *= 2;
                major *= 2;
                screenMinor = minor * scale;
                if (minor > 10000) break;
            }

            // Ensure major is a sufficiently larger step to reduce visual clutter (fewer subdivisions)
            major = Math.Max(major, minor * 8.0);

            // pens - use constant screen thickness (independent of world scale) so lines stay legible
            // use thinner base thickness so lines are less prominent by default
            double baseMinor = 0.6;
            double baseMajor = 1.0;
            double minorThickness = Math.Clamp(baseMinor, 0.5, 6.0);
            double majorThickness = Math.Clamp(baseMajor, 0.7, 8.0);
            // resolve to solid brushes for consistent 1-px fills ¡ª use lighter default opacities
            SolidColorBrush minorBrushSolid = ResolveSolidBrush(MinorBrush, 0.08);
            SolidColorBrush majorBrushSolid = ResolveSolidBrush(MajorBrush, 0.14);

            // Pixel-align lines using device DPI and pen thickness to keep them crisp under zoom
            var dpi = VisualTreeHelper.GetDpi(this);
            double ds = dpi.DpiScaleX; // assume uniform scale
            int minorDeviceThickness = Math.Max(1, (int)Math.Round(minorThickness * ds));
            int majorDeviceThickness = Math.Max(1, (int)Math.Round(majorThickness * ds));
            double minorOffset = (minorDeviceThickness % 2 == 1) ? (0.5 / ds) : 0.0;
            double majorOffset = (majorDeviceThickness % 2 == 1) ? (0.5 / ds) : 0.0;

            // draw as filled rectangles (device-aligned) to avoid subpixel stroke thinning
            double minorDeviceDU = minorDeviceThickness / ds; // device-independent units for thickness
            double majorDeviceDU = majorDeviceThickness / ds;

            // Axis reference behavior:
            // - On first render we want the major axes to appear centered in the control
            //   (initialization requirement). After that, the axes follow the world origin
            //   mapping (OffsetX/OffsetY) so they move with panning.
            // Always map the world origin (0,0) using the provided Offset/Scale so the
            // grid stays consistent with the canvas transform. If a centered initial
            // view is required, the host (MainWindow) should initialize OffsetX/OffsetY
            // appropriately when the layout is available.
            double axisScreenX = OffsetX;
            double axisScreenY = OffsetY;

            double screenMinorStep = minor * scale;
            double screenMajorStep = major * scale;

            // Safety: if step is extremely small, fall back to world-aligned drawing
            if (double.IsNaN(screenMinorStep) || screenMinorStep <= 0.1 || double.IsNaN(screenMajorStep) || screenMajorStep <= 0.1)
            {
                // Fallback to previous world-aligned drawing to avoid runaway loops
                var startIX = (int)Math.Floor(leftWorld / minor) - 1;
                var endIX = (int)Math.Ceiling(rightWorld / minor) + 1;
                for (int ix = startIX; ix <= endIX; ix++)
                {
                    double worldX = ix * minor;
                    double screenX = worldX * scale + OffsetX;
                    var xDevAligned = Math.Round(screenX * ds) / ds + minorOffset;
                    var rectX = xDevAligned - minorDeviceDU / 2.0;
                    dc.DrawRectangle(minorBrushSolid, null, new Rect(rectX, 0, minorDeviceDU, height));
                }

                var startJX = (int)Math.Floor(leftWorld / major) - 1;
                var endJX = (int)Math.Ceiling(rightWorld / major) + 1;
                for (int jx = startJX; jx <= endJX; jx++)
                {
                    double worldX = jx * major;
                    double screenX = worldX * scale + OffsetX;
                    var xDevAligned = Math.Round(screenX * ds) / ds + majorOffset;
                    var rectX = xDevAligned - majorDeviceDU / 2.0;
                    dc.DrawRectangle(majorBrushSolid, null, new Rect(rectX, 0, majorDeviceDU, height));
                }

                var startIY = (int)Math.Floor(topWorld / minor) - 1;
                var endIY = (int)Math.Ceiling(bottomWorld / minor) + 1;
                for (int iy = startIY; iy <= endIY; iy++)
                {
                    double worldY = iy * minor;
                    double screenY = worldY * scale + OffsetY;
                    var yDevAligned = Math.Round(screenY * ds) / ds + minorOffset;
                    var rectY = yDevAligned - minorDeviceDU / 2.0;
                    dc.DrawRectangle(minorBrushSolid, null, new Rect(0, rectY, width, minorDeviceDU));
                }

                var startJY = (int)Math.Floor(topWorld / major) - 1;
                var endJY = (int)Math.Ceiling(bottomWorld / major) + 1;
                for (int jy = startJY; jy <= endJY; jy++)
                {
                    double worldY = jy * major;
                    double screenY = worldY * scale + OffsetY;
                    var yDevAligned = Math.Round(screenY * ds) / ds + majorOffset;
                    var rectY = yDevAligned - majorDeviceDU / 2.0;
                    dc.DrawRectangle(majorBrushSolid, null, new Rect(0, rectY, width, majorDeviceDU));
                }
            }
            else
            {
                // Draw minor lines centered on control
                int leftCount = (int)Math.Ceiling(axisScreenX / screenMinorStep) + 2;
                int rightCount = (int)Math.Ceiling((width - axisScreenX) / screenMinorStep) + 2;
                for (int i = -leftCount; i <= rightCount; i++)
                {
                    double screenX = axisScreenX + i * screenMinorStep;
                    if (screenX < -screenMinorStep || screenX > width + screenMinorStep) continue;
                    var xDevAligned = Math.Round(screenX * ds) / ds + minorOffset;
                    var rectX = xDevAligned - minorDeviceDU / 2.0;
                    dc.DrawRectangle(minorBrushSolid, null, new Rect(rectX, 0, minorDeviceDU, height));
                }

                // Draw major lines centered on control
                leftCount = (int)Math.Ceiling(axisScreenX / screenMajorStep) + 2;
                rightCount = (int)Math.Ceiling((width - axisScreenX) / screenMajorStep) + 2;
                for (int j = -leftCount; j <= rightCount; j++)
                {
                    double screenX = axisScreenX + j * screenMajorStep;
                    if (screenX < -screenMajorStep || screenX > width + screenMajorStep) continue;
                    var xDevAligned = Math.Round(screenX * ds) / ds + majorOffset;
                    var rectX = xDevAligned - majorDeviceDU / 2.0;
                    dc.DrawRectangle(majorBrushSolid, null, new Rect(rectX, 0, majorDeviceDU, height));
                }

                // horizontal minor
                int topCount = (int)Math.Ceiling(axisScreenY / screenMinorStep) + 2;
                int bottomCount = (int)Math.Ceiling((height - axisScreenY) / screenMinorStep) + 2;
                for (int i = -topCount; i <= bottomCount; i++)
                {
                    double screenY = axisScreenY + i * screenMinorStep;
                    if (screenY < -screenMinorStep || screenY > height + screenMinorStep) continue;
                    var yDevAligned = Math.Round(screenY * ds) / ds + minorOffset;
                    var rectY = yDevAligned - minorDeviceDU / 2.0;
                    dc.DrawRectangle(minorBrushSolid, null, new Rect(0, rectY, width, minorDeviceDU));
                }

                // horizontal major
                topCount = (int)Math.Ceiling(axisScreenY / screenMajorStep) + 2;
                bottomCount = (int)Math.Ceiling((height - axisScreenY) / screenMajorStep) + 2;
                for (int j = -topCount; j <= bottomCount; j++)
                {
                    double screenY = axisScreenY + j * screenMajorStep;
                    if (screenY < -screenMajorStep || screenY > height + screenMajorStep) continue;
                    var yDevAligned = Math.Round(screenY * ds) / ds + majorOffset;
                    var rectY = yDevAligned - majorDeviceDU / 2.0;
                    dc.DrawRectangle(majorBrushSolid, null, new Rect(0, rectY, width, majorDeviceDU));
                }

                // Draw bold axes exactly at center (only if enabled)
                if (ShowAxes)
                {
                    try
                    {
                        int axisDeviceThickness = Math.Max(1, (int)Math.Round((majorDeviceThickness * 1.8) * ds));
                        double axisDeviceDU = axisDeviceThickness / ds;
                        double axisOffset = (axisDeviceThickness % 2 == 1) ? (0.5 / ds) : 0.0;
                        var axisBrush = ResolveSolidBrush(MajorBrush, 0.32);

                        var axisXDevAligned = Math.Round(axisScreenX * ds) / ds + axisOffset;
                        var axisRectX = axisXDevAligned - axisDeviceDU / 2.0;
                        dc.DrawRectangle(axisBrush, null, new Rect(axisRectX, 0, axisDeviceDU, height));
                        var axisYDevAligned = Math.Round(axisScreenY * ds) / ds + axisOffset;
                        var axisRectY = axisYDevAligned - axisDeviceDU / 2.0;
                        dc.DrawRectangle(axisBrush, null, new Rect(0, axisRectY, width, axisDeviceDU));
                    }
                    catch
                    {
                        // ignore axis drawing failures
                    }
                }
            }
        }

        private static Pen CreatePen(Brush? brush, double thickness, double defaultOpacity)
        {
            Brush use = brush ?? new SolidColorBrush(Color.FromArgb((byte)(defaultOpacity * 255), 255, 255, 255));
            var pen = new Pen(use, thickness);
            pen.Freeze();
            return pen;
        }

        private static SolidColorBrush ResolveSolidBrush(Brush? brush, double defaultOpacity)
        {
            if (brush is SolidColorBrush sb)
            {
                // ensure frozen and return
                if (!sb.IsFrozen)
                {
                    try { sb.Freeze(); } catch { }
                }
                return sb;
            }

            // fallback color white with provided opacity
            var color = Color.FromArgb((byte)(defaultOpacity * 255), 255, 255, 255);
            var fallback = new SolidColorBrush(color);
            fallback.Freeze();
            return fallback;
        }
    }
}
