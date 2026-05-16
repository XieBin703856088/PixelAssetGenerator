using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    public class NodeConnectionViewModel : INotifyPropertyChanged
    {
        private double _startX;
        private double _startY;
        private double _endX;
        private double _endY;
        private bool _isPreview;
        private bool _isSelected;

        public NodeViewModel? StartNode { get; set; }

        public NodeViewModel? EndNode { get; set; }

        public int StartPortIndex { get; set; }

        public int EndPortIndex { get; set; }

        public double StartX
        {
            get => _startX;
            set
            {
                if (SetField(ref _startX, value))
                    OnPropertyChanged(nameof(WireGeometry));
            }
        }

        public double StartY
        {
            get => _startY;
            set
            {
                if (SetField(ref _startY, value))
                    OnPropertyChanged(nameof(WireGeometry));
            }
        }

        public double EndX
        {
            get => _endX;
            set
            {
                if (SetField(ref _endX, value))
                    OnPropertyChanged(nameof(WireGeometry));
            }
        }

        public double EndY
        {
            get => _endY;
            set
            {
                if (SetField(ref _endY, value))
                    OnPropertyChanged(nameof(WireGeometry));
            }
        }

        public bool IsPreview
        {
            get => _isPreview;
            set => SetField(ref _isPreview, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        /// <summary>
        /// Cubic B��zier geometry for the connection wire.
        /// Control points extend horizontally from each port so the curve
        /// leaves/arrives tangent to the port direction.
        /// </summary>
        public Geometry WireGeometry
        {
            get
            {
                double dx = EndX - StartX;
                // Minimum tangent length so short connections still look smooth.
                double tangent = Math.Max(Math.Abs(dx) * 0.5, 60);

                var figure = new PathFigure
                {
                    StartPoint = new Point(StartX, StartY),
                    IsClosed = false
                };
                figure.Segments.Add(new BezierSegment(
                    new Point(StartX + tangent, StartY),   // cp1 �� extends right from output
                    new Point(EndX - tangent, EndY),       // cp2 �� arrives left at input
                    new Point(EndX, EndY),
                    isStroked: true));

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                geometry.Freeze();
                return geometry;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
