using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Handles all node graph evaluation: building GraphNodeInstance lists from
/// NodeViewModel collections, invoking the topological-sort evaluator,
/// and producing PixelBuffer / BitmapSource / Brush results for preview and export.
/// </summary>
public sealed class GraphEvaluationService
{
    private readonly NodeGraphEvaluator _graphEvaluator = new();

    /// <summary>
    /// Fired when a graph evaluation error occurs (e.g. cycle detected).
    /// The UI subscribes to show status bar messages or alerts.
    /// </summary>
    public event Action<string>? EvaluationError;

    public GraphEvaluationService()
    {
        _graphEvaluator.OnEvaluationError = msg => EvaluationError?.Invoke(msg);
    }

    /// <summary>
    /// Delegate for converting a tile-type node to a PixelBuffer so it can be used
    /// as an upstream source in the graph pipeline. Called when a node has TileType
    /// set instead of a graph node type name.
    /// </summary>
    public Func<NodeViewModel, int, PixelBuffer?>? TileNodeRenderer { get; set; }

    /// <summary>
    /// Checks if a node is a new graph-based node (from Core.GraphNodeRegistry).
    /// </summary>
    public static bool IsGraphNode(NodeViewModel node)
    {
        return node.TileType == null && GraphNodeRegistry.Create(node.RegistryKey) != null;
    }

    /// <summary>
    /// Evaluates the graph pipeline for the given nodes and returns a BitmapSource.
    /// </summary>
    public BitmapSource? EvaluateGraphPipeline(
        List<NodeViewModel> nodes,
        List<NodeConnectionViewModel> connections,
        int size,
        NodeViewModel? targetNode = null,
        NodeViewModel? selectedNode = null,
        CancellationToken ct = default,
        float? animationTimeOverride = null)
    {
        var (instances, instanceMap) = BuildInstances(nodes, connections, size);
        if (instances.Count == 0) return null;

        var graphConnections = BuildGraphConnections(connections, instanceMap);

        var seed = targetNode?.TileProperties?.Seed
            ?? targetNode?.Parameters.FirstOrDefault(p => p.Name == "seed")?.IntValue
            ?? selectedNode?.TileProperties?.Seed
            ?? selectedNode?.Parameters.FirstOrDefault(p => p.Name == "seed")?.IntValue
            ?? 42;

        var semanticOverrides = CollectSemanticOverrides(nodes);
        var context = new PixelGraphContext
        {
            TileSize = size,
            Seed = seed,
            SemanticOverrides = semanticOverrides,
            AnimationTime = animationTimeOverride
        };

        int? targetId = null;
        if (targetNode != null && instanceMap.ContainsKey(targetNode.Id))
            targetId = targetNode.Id;
        else if (selectedNode != null && instanceMap.ContainsKey(selectedNode.Id))
            targetId = selectedNode.Id;

        var result = _graphEvaluator.Evaluate(instances, graphConnections, context, targetId, ct);
        return result?.ToBitmapSource();
    }

    /// <summary>
    /// Evaluates all nodes in the graph and returns per-node PixelBuffer dictionary.
    /// </summary>
    public Dictionary<int, PixelBuffer>? EvaluateAllGraphNodeBuffers(
        List<NodeViewModel> nodesSnapshot,
        List<NodeConnectionViewModel> connectionsSnapshot,
        int previewSize,
        CancellationToken ct = default)
    {
        var (instances, instanceMap) = BuildInstances(nodesSnapshot, connectionsSnapshot, previewSize);
        if (instances.Count == 0) return null;

        var graphConnections = BuildGraphConnections(connectionsSnapshot, instanceMap);

        var semanticOverrides = CollectSemanticOverrides(nodesSnapshot);
        var context = new PixelGraphContext { TileSize = previewSize, Seed = 42, SemanticOverrides = semanticOverrides };

        return _graphEvaluator.EvaluateAll(instances, graphConnections, context, ct);
    }

    /// <summary>
    /// Generates a preview brush for a single graph-based node by evaluating it in isolation.
    /// </summary>
    public Brush? GenerateGraphNodePreviewBrush(NodeViewModel node, int previewSize)
    {
        var graphNode = GraphNodeRegistry.Create(node.RegistryKey);
        if (graphNode is null) return null;

        var instance = new GraphNodeInstance(node.Id, graphNode);
        CopyParameters(node, instance);

        var context = new PixelGraphContext
        {
            TileSize = previewSize,
            Seed = node.Parameters.FirstOrDefault(p => p.Name == "seed")?.IntValue ?? 42
        };

        var buffer = graphNode.Process(new PixelBuffer?[Math.Max(1, graphNode.InputPorts.Count)], instance.ParameterValues, context);
        var bmp = buffer.ToBitmapSource();
        return CreatePreviewBrushFromBitmap(bmp, previewSize);
    }

    /// <summary>
    /// Creates a simple ImageBrush from a bitmap for thumbnails.
    /// </summary>
    public static Brush CreatePreviewBrushFromBitmap(BitmapSource bmp, int logicalSize)
    {
        var imgBrush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(imgBrush, BitmapScalingMode.NearestNeighbor);
        imgBrush.Freeze();
        return imgBrush;
    }

    /// <summary>
    /// Generates a fast noise-based preview brush for compute nodes.
    /// </summary>
    public Brush GenerateComputeNodePreviewBrush(NodeViewModel node)
    {
        const int size = 32;

        var seedParam = node.Parameters.FirstOrDefault(p => p.Name == "seed");
        var scaleParam = node.Parameters.FirstOrDefault(p => p.Name == "scale");
        var octavesParam = node.Parameters.FirstOrDefault(p => p.Name == "octaves");
        var persistenceParam = node.Parameters.FirstOrDefault(p => p.Name == "persistence");
        var lacunarityParam = node.Parameters.FirstOrDefault(p => p.Name == "lacunarity");
        var invert = node.Parameters.FirstOrDefault(p => p.Name == "invert")?.BoolValue ?? false;

        if (seedParam != null && scaleParam != null && octavesParam != null)
        {
            var seed = (seedParam.Kind == NodeParameterKind.Integer || seedParam.Kind == NodeParameterKind.Seed) ? seedParam.IntValue : (int)seedParam.NumberValue;
            var scale = scaleParam.Kind == NodeParameterKind.Integer ? scaleParam.IntValue : scaleParam.NumberValue;
            var octaves = octavesParam.Kind == NodeParameterKind.Integer ? octavesParam.IntValue : (int)octavesParam.NumberValue;
            var persistence = persistenceParam != null ? (persistenceParam.Kind == NodeParameterKind.Integer ? persistenceParam.IntValue : persistenceParam.NumberValue) : 0.5;
            var lacunarity = lacunarityParam != null ? (lacunarityParam.Kind == NodeParameterKind.Integer ? lacunarityParam.IntValue : lacunarityParam.NumberValue) : 2.0;

            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = x / (float)size * (float)scale;
                    var ny = y / (float)size * (float)scale;
                    var v = GraphNodeBase.TileableFractalNoise(nx, ny, size, octaves, (float)persistence, (float)lacunarity, seed);
                    v = Math.Clamp(v, 0f, 1f);
                    if (invert) v = 1f - v;

                    var r = (byte)Math.Clamp(60 + v * (200 - 60), 0, 255);
                    var g = (byte)Math.Clamp(70 + v * (220 - 70), 0, 255);
                    var b = (byte)Math.Clamp(90 + v * (240 - 90), 0, 255);
                    var a = (byte)255;
                    var index = (y * size + x) * 4;
                    pixels[index] = b;
                    pixels[index + 1] = g;
                    pixels[index + 2] = r;
                    pixels[index + 3] = a;
                }
            }

            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }

        var fallbackGroup = new DrawingGroup();
        fallbackGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(28, 34, 44)), null,
            new RectangleGeometry(new Rect(0, 0, size, size))));
        fallbackGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(72, 88, 128)), null,
            new EllipseGeometry(new Point(11, 11), 5, 5)));
        fallbackGroup.Freeze();
        var fallbackBrush = new DrawingBrush(fallbackGroup) { Stretch = Stretch.UniformToFill };
        fallbackBrush.Freeze();
        return fallbackBrush;
    }

    /// <summary>
    /// Builds a GraphNodeInstance list from NodeViewModels, resolving tile nodes
    /// via the TileNodeRenderer delegate.
    /// </summary>
    private (List<GraphNodeInstance> Instances, Dictionary<int, GraphNodeInstance> Map) BuildInstances(
        List<NodeViewModel> nodes,
        List<NodeConnectionViewModel> connections,
        int size)
    {
        var instanceMap = new Dictionary<int, GraphNodeInstance>();
        var instances = new List<GraphNodeInstance>();

        foreach (var node in nodes)
        {
            IGraphNode? graphNode = GraphNodeRegistry.Create(node.RegistryKey);
            if (graphNode is null && node.TileType != null && TileNodeRenderer != null)
            {
                var tileBuffer = TileNodeRenderer(node, size);
                if (tileBuffer != null)
                    graphNode = new PixelBufferSourceNode(tileBuffer);
            }
            if (graphNode is null)
                continue;

            var instance = new GraphNodeInstance(node.Id, graphNode);
            CopyParameters(node, instance);
            instanceMap[node.Id] = instance;
            instances.Add(instance);
        }

        return (instances, instanceMap);
    }

    private static List<GraphConnection> BuildGraphConnections(
        List<NodeConnectionViewModel> connections,
        Dictionary<int, GraphNodeInstance> instanceMap)
    {
        var graphConnections = new List<GraphConnection>();
        foreach (var conn in connections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
        {
            if (instanceMap.ContainsKey(conn.StartNode!.Id) && instanceMap.ContainsKey(conn.EndNode!.Id))
            {
                graphConnections.Add(new GraphConnection(
                    conn.StartNode!.Id, conn.StartPortIndex,
                    conn.EndNode!.Id, conn.EndPortIndex));
            }
        }
        return graphConnections;
    }

    private static void CopyParameters(NodeViewModel node, GraphNodeInstance instance)
    {
        foreach (var param in node.Parameters)
        {
            var value = param.Kind switch
            {
                NodeParameterKind.Seed => (object)param.IntValue,
                NodeParameterKind.Integer => (object)param.IntValue,
                NodeParameterKind.Boolean => param.BoolValue,
                NodeParameterKind.Choice => param.SelectedChoice ?? string.Empty,
                NodeParameterKind.PointList => (object)(IReadOnlyList<Point>)param.PointListValue.ToArray(),
                NodeParameterKind.Color => (object)param.ColorValue,
                NodeParameterKind.Text => param.TextValue ?? string.Empty,
                _ => (object)param.NumberValue
            };
            instance.ParameterValues[param.Name] = value;
        }
    }

    /// <summary>
    /// Collects semantic parameter overrides from SemanticControlNode instances in the graph.
    /// Only includes overrides when the override toggle is enabled.
    /// </summary>
    private static IReadOnlyDictionary<string, float>? CollectSemanticOverrides(List<NodeViewModel> nodes)
    {
        Dictionary<string, float>? overrides = null;

        foreach (var node in nodes)
        {
            if (node.Title != "SemanticControl")
                continue;

            var enabled = node.Parameters.FirstOrDefault(p => p.Name == "enableOverride")?.BoolValue ?? false;
            if (!enabled)
                continue;

            overrides ??= new Dictionary<string, float>();

            foreach (var param in node.Parameters)
            {
                if (param.Name == "enableOverride")
                    continue;

                // Number parameters are in 0-1 range, use directly
                if (param.Kind == NodeParameterKind.Number)
                {
                    overrides[param.Name] = (float)param.NumberValue;
                }
            }
        }

        return overrides;
    }

    /// <summary>
    /// Adapts a pre-rendered PixelBuffer as a zero-input graph node so that tile nodes
    /// can participate in the graph evaluation pipeline.
    /// </summary>
    private sealed class PixelBufferSourceNode : IGraphNode
    {
        private readonly PixelBuffer _buffer;
        private static readonly IReadOnlyList<GraphNodePort> _outputPorts = new[] { new GraphNodePort("Bitmap", GraphPortType.Image) };

        public PixelBufferSourceNode(PixelBuffer buffer) => _buffer = buffer;
        public string TypeName => "__TileSource__";
        public string Category => "Source";
        public IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
        public IReadOnlyList<GraphNodePort> OutputPorts => _outputPorts;
        public IReadOnlyList<NodeParameterDefinition> Parameters => Array.Empty<NodeParameterDefinition>();
        public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context) => _buffer;
    }
}
