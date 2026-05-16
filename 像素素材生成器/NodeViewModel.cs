using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    public class NodeViewModel : INotifyPropertyChanged
    {
        private static int _nextId;
        private string _title;
        private string _typeName = "";
        private double _x;
        private double _y;
        private readonly int _id;
        private Brush _previewBrush;
        private NodeLibraryItemKind _kind;
        private TileType? _tileType;
        private TileProperties? _tileProperties;
        private bool _isSelected;
        private string _category = "";

        public NodeViewModel(string title, double x, double y, Brush? previewBrush = null)
        {
            _title = title;
            _x = x;
            _y = y;
            _id = System.Threading.Interlocked.Increment(ref _nextId);
            _previewBrush = previewBrush ?? Brushes.DimGray;
        }

        public int Id => _id;

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        /// <summary>Registry TypeName (e.g. "Blur") — language-independent, used for node lookup.</summary>
        public string TypeName
        {
            get => _typeName;
            set => SetField(ref _typeName, value);
        }

        /// <summary>The canonical key for <see cref="Core.GraphNodeRegistry"/> lookups. Prefers <see cref="TypeName"/> when set, falls back to <see cref="Title"/>.</summary>
        public string RegistryKey => string.IsNullOrEmpty(_typeName) ? _title : _typeName;

        public double X
        {
            get => _x;
            set
            {
                // reduce precision to 0.01 to avoid tiny floating jitter causing linked updates
                var rounded = Math.Round(value, 2);
                SetField(ref _x, rounded);
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                // reduce precision to 0.01 to avoid tiny floating jitter causing linked updates
                var rounded = Math.Round(value, 2);
                SetField(ref _y, rounded);
            }
        }

        public Brush PreviewBrush
        {
            get => _previewBrush;
            set => SetField(ref _previewBrush, value);
        }

        public NodeLibraryItemKind Kind
        {
            get => _kind;
            set => SetField(ref _kind, value);
        }

        public TileType? TileType
        {
            get => _tileType;
            set => SetField(ref _tileType, value);
        }

        // When this is set, the node represents or shares tile properties with a layer
        public TileProperties? TileProperties
        {
            get => _tileProperties;
            set
            {
                if (ReferenceEquals(_tileProperties, value)) return;
                if (_tileProperties != null)
                {
                    _tileProperties.PropertyChanged -= TileProperties_PropertyChanged;
                }

                _tileProperties = value;

                if (_tileProperties != null)
                {
                    _tileProperties.PropertyChanged += TileProperties_PropertyChanged;
                }

                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public string Category
        {
            get => _category;
            set => SetField(ref _category, value);
        }

        public ObservableCollection<NodePortViewModel> InputPorts { get; } = new();

        public ObservableCollection<NodePortViewModel> OutputPorts { get; } = new();

        public ObservableCollection<NodeParameterViewModel> Parameters { get; } = new();

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

        private void TileProperties_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // When shared tile properties change, notify so UI can refresh previews
            OnPropertyChanged(nameof(TileProperties));
        }

    }

    public class NodePortViewModel
    {
        public NodePortViewModel(string name)
        {
            Name = name;
            Type = PortValueType.Float;
            IsOutput = false;
            FillBrush = GetBrushForType(Type, IsOutput);
        }

        public NodePortViewModel(string name, PortValueType type, bool isOutput)
        {
            Name = name;
            Type = type;
            IsOutput = isOutput;
            FillBrush = GetBrushForType(Type, IsOutput);
        }

        public string Name { get; set; }

        public PortValueType Type { get; }

        public bool IsOutput { get; }

        public Brush FillBrush { get; }

        public string TypeName
        {
            get
            {
                return Type switch
                {
                    PortValueType.Float => "Float",
                    PortValueType.Integer => "Integer",
                    PortValueType.Boolean => "Boolean",
                    PortValueType.Tile => "Bitmap",
                    PortValueType.Mask => "Grayscale",
                    PortValueType.Color => "Color",
                    PortValueType.Any => "Any",
                    PortValueType.Particle => "Particle",
                    _ => "Value",
                };
            }
        }

        private static Brush GetBrushForType(PortValueType type, bool isOutput)
        {
            // map port types to distinct colors
            System.Windows.Media.Color c;
            switch (type)
            {
                case PortValueType.Integer:
                    c = System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00); // orange
                    break;
                case PortValueType.Boolean:
                    c = System.Windows.Media.Color.FromRgb(0x9B, 0x59, 0xB6); // purple
                    break;
                case PortValueType.Tile:
                    c = System.Windows.Media.Color.FromRgb(0x5C, 0xC8, 0xFF); // blue (tile)
                    break;
                case PortValueType.Image:
                    c = System.Windows.Media.Color.FromRgb(0x4E, 0xE8, 0xFF); // cyan-blue (image)
                    break;
                case PortValueType.Mask:
                    c = System.Windows.Media.Color.FromRgb(0xD0, 0xD8, 0xE0); // light gray (grayscale)
                    break;
                case PortValueType.Color:
                    c = System.Windows.Media.Color.FromRgb(0xE6, 0x6F, 0x22); // accent
                    break;
                case PortValueType.Any:
                {
                    var grad = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(0x4E, 0xE8, 0xFF),
                        System.Windows.Media.Color.FromRgb(0xD0, 0xD8, 0xE0),
                        90.0);
                    grad.Freeze();
                    return grad;
                }
                case PortValueType.Particle:
                    c = System.Windows.Media.Color.FromRgb(0xE8, 0x4C, 0x88); // pink/magenta for particle
                    break;
                default:
                    c = System.Windows.Media.Color.FromRgb(0x4C, 0xC9, 0x7A); // green for float/default
                    break;
            }

            var brush = new System.Windows.Media.SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}

public enum PortValueType
{
    Float,
    Integer,
    Boolean,
    Tile,
    Image,
    Mask,
    Color,
    Any,
    Particle
}
