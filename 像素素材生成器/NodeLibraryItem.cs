using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelAssetGenerator
{
    public enum NodeLibraryItemKind
    {
        Tile,
        Compute,
        Composite
    }

    /// <summary>AI-facing metadata for a node type — describes what it does, when to use it, and what feeds into it.</summary>
    public sealed class NodeAiMetadata
    {
        /// <summary>Capability tags for AI classification (e.g. "generate-noise", "color-adjust", "pattern").</summary>
        public List<string>? Capabilities { get; init; }
        /// <summary>Natural language trigger keywords (CN + EN) to help AI match user intent to this node.</summary>
        public string? Triggers { get; init; }
        /// <summary>Typical predecessor node type names for AI graph building hints.</summary>
        public List<string>? SuggestedInputs { get; init; }
        /// <summary>One-line usage example, e.g. "Feed a Noise node into Displace for organic distortion".</summary>
        public string? ExampleUsage { get; init; }
    }

    public sealed class NodeLibraryItem
    {
        public NodeLibraryItem(string name,
            string category,
            NodeLibraryItemKind kind,
            Brush previewBrush,
            TileType? tileType,
            IReadOnlyList<string> inputPorts,
            IReadOnlyList<string> outputPorts,
            IReadOnlyList<NodeParameterDefinition>? parameters = null,
            string subcategory = "",
            string typeName = "",
            string? categoryKey = null,
            int thumbnailWidth = 64,
            int thumbnailHeight = 0,
            string? description = null,
            NodeAiMetadata? aiMetadata = null)
        {
            Name = name;
            Category = category;
            CategoryKey = categoryKey ?? "";
            Kind = kind;
            PreviewBrush = previewBrush;
            TileType = tileType;
            InputPorts = inputPorts;
            OutputPorts = outputPorts;
            Parameters = parameters ?? Array.Empty<NodeParameterDefinition>();
            Subcategory = subcategory;
            TypeName = string.IsNullOrEmpty(typeName) ? name : typeName;
            _thumbnailWidth = thumbnailWidth;
            _thumbnailHeight = thumbnailHeight > 0 ? thumbnailHeight : thumbnailWidth;
            Description = description ?? "";
            AiMetadata = aiMetadata;
        }

        public string Name { get; }

        /// <summary>Language-independent registry key (e.g. "Blur"). Equals Name for tile nodes.</summary>
        public string TypeName { get; }

        /// <summary>Localized category display name for UI.</summary>
        public string Category { get; }

        /// <summary>Canonical category key (directory name) for filtering.</summary>
        public string CategoryKey { get; }

        public NodeLibraryItemKind Kind { get; }

        public Brush PreviewBrush { get; }

        /// <summary>One-line description of what this node does (for AI context).</summary>
        public string Description { get; }

        /// <summary>AI-facing metadata for semantic matching and graph building hints.</summary>
        public NodeAiMetadata? AiMetadata { get; }

        // Small rasterized thumbnail derived from the brush for display in the node library.
        // Generated on demand on the UI thread to avoid cross-thread access errors.
        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get
            {
                if (_previewImage == null && PreviewBrush != null)
                {
                    try
                    {
                        _previewImage = CreatePreviewImage(PreviewBrush);
                    }
                    catch
                    {
                        _previewImage = null;
                    }
                }
                return _previewImage;
            }
        }

        public TileType? TileType { get; }

        public IReadOnlyList<string> InputPorts { get; }

        public IReadOnlyList<string> OutputPorts { get; }

        public IReadOnlyList<NodeParameterDefinition> Parameters { get; }

        public string Subcategory { get; }

        /// <summary>Width used when rasterizing this item's preview thumbnail.</summary>
        public int ThumbnailWidth => _thumbnailWidth;
        /// <summary>Height used when rasterizing this item's preview thumbnail.</summary>
        public int ThumbnailHeight => _thumbnailHeight;

        private readonly int _thumbnailWidth;
        private readonly int _thumbnailHeight;

        public static Brush CreateTiledPreviewBrush(Color baseColor, Color accentColor)
        {
            var tile = new DrawingGroup();
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(baseColor), null, new RectangleGeometry(new System.Windows.Rect(0, 0, 16, 16))));
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(accentColor), null, new RectangleGeometry(new System.Windows.Rect(0, 0, 6, 6))));
            tile.Children.Add(new GeometryDrawing(new SolidColorBrush(accentColor), null, new RectangleGeometry(new System.Windows.Rect(10, 10, 6, 6))));
            tile.Freeze();

            var brush = new DrawingBrush(tile)
            {
                TileMode = TileMode.Tile,
                Viewport = new System.Windows.Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        private ImageSource? CreatePreviewImage(Brush? brush)
        {
            try
            {
                if (brush == null) return null;

                var w = _thumbnailWidth;
                var h = _thumbnailHeight;

                // Clone brush so we can adjust properties (e.g. ImageBrush.Stretch) without
                // mutating the original brush instance used elsewhere.
                var clone = brush.CloneCurrentValue();
                if (clone is ImageBrush ib)
                {
                    ib.Stretch = Stretch.UniformToFill;
                }
                clone.Freeze();

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(clone, null, new Rect(0, 0, w, h));
                }

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch
            {
                return null;
            }
        }
    }
}
