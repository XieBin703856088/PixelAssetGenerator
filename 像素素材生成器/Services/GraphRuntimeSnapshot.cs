using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Immutable runtime projection of the editable WPF graph. It is the only place that
/// translates view-model parameters, tile nodes and UI connections into core contracts.
/// </summary>
public sealed class GraphRuntimeSnapshot : IDisposable
{
    public List<GraphNodeInstance> Instances { get; }
    public Dictionary<int, GraphNodeInstance> InstanceMap { get; }
    public List<GraphConnection> Connections { get; }

    internal GraphRuntimeSnapshot(
        List<GraphNodeInstance> instances,
        Dictionary<int, GraphNodeInstance> instanceMap,
        List<GraphConnection> connections)
    {
        Instances = instances;
        InstanceMap = instanceMap;
        Connections = connections;
    }

    public void Dispose()
    {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var instance in Instances)
            if (instance.Node is IDisposable disposable && disposed.Add(instance.Node))
                disposable.Dispose();
    }
}

public static class GraphRuntimeSnapshotBuilder
{
    public static GraphRuntimeSnapshot Build(
        IReadOnlyList<NodeViewModel> nodes,
        IReadOnlyList<NodeConnectionViewModel> connections,
        int size,
        Func<NodeViewModel, int, PixelBuffer?>? tileNodeRenderer)
    {
        var instanceMap = new Dictionary<int, GraphNodeInstance>(nodes.Count);
        var instances = new List<GraphNodeInstance>(nodes.Count);

        foreach (var node in nodes)
        {
            IGraphNode? graphNode = GraphNodeRegistry.Create(node.RegistryKey);
            if (graphNode == null && node.TileType != null && tileNodeRenderer != null)
            {
                var tileBuffer = tileNodeRenderer(node, size);
                if (tileBuffer != null) graphNode = new TilePixelBufferNode(tileBuffer);
            }

            if (graphNode == null) continue;

            if (graphNode is INodeInstanceAware instanceAware)
                instanceAware.NodeInstanceId = node.Id;

            var instance = new GraphNodeInstance(node.Id, graphNode);
            CopyParameters(node, instance);
            instanceMap[node.Id] = instance;
            instances.Add(instance);
        }

        var runtimeConnections = connections
            .Where(connection => !connection.IsPreview
                && connection.StartNode != null && connection.EndNode != null
                && instanceMap.ContainsKey(connection.StartNode.Id)
                && instanceMap.ContainsKey(connection.EndNode.Id))
            .Select(connection => new GraphConnection(
                connection.StartNode!.Id,
                connection.StartPortIndex,
                connection.EndNode!.Id,
                connection.EndPortIndex))
            .ToList();

        return new GraphRuntimeSnapshot(instances, instanceMap, runtimeConnections);
    }

    public static IReadOnlyDictionary<string, float>? CollectSemanticOverrides(
        IReadOnlyList<NodeViewModel> nodes)
    {
        Dictionary<string, float>? overrides = null;
        foreach (var node in nodes)
        {
            if (!node.RegistryKey.Equals("SemanticControl", StringComparison.OrdinalIgnoreCase)) continue;
            if (node.Parameters.FirstOrDefault(parameter => parameter.Name == "enableOverride")?.BoolValue != true)
                continue;

            overrides ??= new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var parameter in node.Parameters)
                if (parameter.Kind == NodeParameterKind.Number && parameter.Name != "enableOverride")
                    overrides[parameter.Name] = (float)parameter.NumberValue;
        }
        return overrides;
    }

    public static void CopyParameters(NodeViewModel node, GraphNodeInstance instance)
    {
        foreach (var parameter in node.Parameters)
        {
            instance.ParameterValues[parameter.Name] = parameter.Kind switch
            {
                NodeParameterKind.Seed or NodeParameterKind.Integer => parameter.IntValue,
                NodeParameterKind.Boolean => parameter.BoolValue,
                NodeParameterKind.Choice => parameter.SelectedChoice ?? string.Empty,
                NodeParameterKind.PointList => (IReadOnlyList<Point>)parameter.PointListValue.ToArray(),
                NodeParameterKind.Color => parameter.ColorValue,
                NodeParameterKind.Text => parameter.TextValue ?? string.Empty,
                _ => parameter.NumberValue
            };
        }
    }

    private sealed class TilePixelBufferNode : IGraphNode, IMultiOutputNode, IDisposable
    {
        private readonly PixelBuffer _image;
        private readonly PixelBuffer _mask;
        private static readonly IReadOnlyList<GraphNodePort> Outputs =
        [
            new("Bitmap", GraphPortType.Image, "image"),
            new("Mask", GraphPortType.Mask, "mask")
        ];

        public TilePixelBufferNode(PixelBuffer image)
        {
            _image = image;
            _mask = PixelBuffer.CreateMaskView(image);
        }

        public string TypeName => "__TileSource__";
        public string Category => "Source";
        public IReadOnlyList<GraphNodePort> InputPorts => Array.Empty<GraphNodePort>();
        public IReadOnlyList<GraphNodePort> OutputPorts => Outputs;
        public IReadOnlyList<NodeParameterDefinition> Parameters => Array.Empty<NodeParameterDefinition>();
        public GraphNodeTraits Traits => GraphNodeTraits.None;

        public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
            PixelGraphContext context) => _image.Clone();

        public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters,
            PixelGraphContext context) => [_image.Clone(), _mask.Clone()];

        public void Dispose()
        {
            _image.Dispose();
            _mask.Dispose();
        }
    }
}
