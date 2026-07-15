using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using PixelAssetGenerator.Core;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Stateless controller for node graph operations: selection, CRUD, copy/paste,
/// connection management, port configuration, and parameter initialization.
/// All state (selected connection, clipboard, active connection) is owned by the
/// caller and passed explicitly.
/// </summary>
public sealed class NodeGraphController
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly ObservableCollection<NodeConnectionViewModel> _connections;
    private const string GrassCustomFlowerPortName = "customFlower";

    // ─── Callbacks ───────────────────────────────────────────────────

    public Action<bool>? RequestPreviewRefresh { get; set; }
    public Action? UpdateNodeCanvasExtent { get; set; }
    public Action? RefreshConnectionsView { get; set; }
    public Action? InvalidateConnectionLayer { get; set; }
    public Action? MarkStatsActive { get; set; }
    public Action<string>? SetStatusText { get; set; }
    public Action<NodeViewModel?>? UpdateConnectionPositions { get; set; }
    public Func<Point, double, Point>? GetNodeCanvasPosition { get; set; }
    public Action<NodeParameterViewModel>? OnParameterCreated { get; set; }
    public Action? RecordUndoSnapshot { get; set; }

    public NodeGraphController(ObservableCollection<NodeViewModel> nodes, ObservableCollection<NodeConnectionViewModel> connections)
    {
        _nodes = nodes;
        _connections = connections;
    }

    // ─── Selection ───────────────────────────────────────────────────

    public void SelectAllNodes()
    {
        foreach (var node in _nodes)
            node.IsSelected = true;
        RequestPreviewRefresh?.Invoke(false);
    }

    public bool ClearNodeSelection()
    {
        var changed = false;
        foreach (var node in _nodes)
        {
            if (node.IsSelected)
            {
                node.IsSelected = false;
                changed = true;
            }
        }
        if (changed)
            RequestPreviewRefresh?.Invoke(false);
        return changed;
    }

    public static void ClearConnectionSelection(ref NodeConnectionViewModel? selectedConnection)
    {
        if (selectedConnection != null)
        {
            selectedConnection.IsSelected = false;
            selectedConnection = null;
        }
    }

    public void ClearSelectionExcept(NodeViewModel keep)
    {
        var changed = false;
        foreach (var node in _nodes)
        {
            if (!ReferenceEquals(node, keep) && node.IsSelected)
            {
                node.IsSelected = false;
                changed = true;
            }
        }
        if (changed)
            RequestPreviewRefresh?.Invoke(false);
    }

    public List<NodeViewModel> GetSelectedNodes(NodeViewModel? selectedNode = null)
    {
        var list = _nodes.Where(n => n.IsSelected).ToList();
        if (selectedNode != null && !list.Contains(selectedNode))
            list.Add(selectedNode);
        return list;
    }

    // ─── Node CRUD ───────────────────────────────────────────────────

    /// <summary>Returns the list of removed nodes (for use by caller to update state).</summary>
    public List<NodeViewModel> DeleteSelectedNodes(NodeViewModel? selectedNode = null)
    {
        var selected = GetSelectedNodes(selectedNode);
        if (selected.Count == 0)
        {
            if (selectedNode != null)
                return DeleteNodes(new[] { selectedNode });
            return selected;
        }
        return DeleteNodes(selected);
    }

    /// <summary>Deletes the given nodes and their connections. Returns the removed list.</summary>
    public List<NodeViewModel> DeleteNodes(IEnumerable<NodeViewModel> nodesToRemove)
    {
        RecordUndoSnapshot?.Invoke();
        var set = new HashSet<NodeViewModel>(nodesToRemove);

        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            var c = _connections[i];
            if ((c.StartNode != null && set.Contains(c.StartNode)) || (c.EndNode != null && set.Contains(c.EndNode)))
                _connections.RemoveAt(i);
        }

        var removed = new List<NodeViewModel>();
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            if (set.Contains(n))
            {
                _nodes.RemoveAt(i);
                removed.Add(n);
            }
        }

        UpdateNodeCanvasExtent?.Invoke();
        RequestPreviewRefresh?.Invoke(false);
        return removed;
    }

    /// <summary>Copies selected nodes to a new ProjectData and returns it (the clipboard).</summary>
    public ProjectFileService.ProjectData? CopySelectedNodes(NodeViewModel? selectedNode = null, int tileSize = 32)
    {
        var selected = GetSelectedNodes(selectedNode);
        if (selected.Count == 0)
            return null;

        var data = new ProjectFileService.ProjectData { TileSize = tileSize };

        foreach (var node in selected)
        {
            var nd = new ProjectFileService.NodeData
            {
                Title = node.Title,
                TypeName = node.TypeName,
                X = node.X,
                Y = node.Y,
                Kind = node.Kind,
                TileType = node.TileType,
                Properties = node.TileProperties?.Clone() ?? new TileProperties()
            };

            if (node.Kind != NodeLibraryItemKind.Tile)
            {
                foreach (var p in node.Parameters)
                {
                    nd.Parameters.Add(new ProjectFileService.NodeParameterData
                    {
                        Name = p.Name, Kind = p.Kind,
                        NumberValue = p.NumberValue, IntValue = p.IntValue,
                        BoolValue = p.BoolValue, SelectedChoice = p.SelectedChoice,
                        TextValue = p.TextValue,
                        ColorArgb = PackColor(p.ColorValue),
                        PointListData = new List<Point>(p.PointListValue)
                    });
                }
            }

            data.Nodes.Add(nd);
        }

        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            var c = _connections[i];
            if (c.IsPreview || c.StartNode == null || c.EndNode == null) continue;
            var sIndex = selected.IndexOf(c.StartNode);
            var eIndex = selected.IndexOf(c.EndNode);
            if (sIndex >= 0 && eIndex >= 0)
                data.Connections.Add(new ProjectFileService.ConnectionData { StartNodeIndex = sIndex, StartPortIndex = c.StartPortIndex, EndNodeIndex = eIndex, EndPortIndex = c.EndPortIndex });
        }

        SetStatusText?.Invoke("Nodes copied");
        return data;
    }

    /// <summary>Pastes nodes from clipboard data and returns the created node list.</summary>
    public List<NodeViewModel> PasteClipboardAtMouse(
        ProjectFileService.ProjectData data,
        double nodeCanvasScale,
        Func<Point> getMousePosition,
        Func<double> getHostWidth,
        Func<double> getHostHeight)
    {
        var result = new List<NodeViewModel>();
        if (data == null || data.Nodes.Count == 0) return result;

        RecordUndoSnapshot?.Invoke();

        Point viewport;
        try { viewport = getMousePosition(); } catch { viewport = new Point(0, 0); }

        try
        {
            var w = getHostWidth();
            var h = getHostHeight();
            if (w > 0 && h > 0 && (viewport.X < 0 || viewport.X > w || viewport.Y < 0 || viewport.Y > h))
                viewport = new Point(w / 2.0, h / 2.0);
        }
        catch { }

        var contentPos = GetNodeCanvasPosition != null
            ? GetNodeCanvasPosition(viewport, nodeCanvasScale)
            : viewport;

        var minX = data.Nodes.Min(n => n.X);
        var minY = data.Nodes.Min(n => n.Y);
        var offsetX = contentPos.X - minX;
        var offsetY = contentPos.Y - minY;

        var created = new List<NodeViewModel>();

        foreach (var nd in data.Nodes)
        {
            var node = new NodeViewModel(nd.Title, nd.X + offsetX, nd.Y + offsetY, null)
            {
                Kind = nd.Kind, TileType = nd.TileType,
                TypeName = string.IsNullOrEmpty(nd.TypeName) ? nd.Title : nd.TypeName
            };

            if (nd.Properties != null)
                node.TileProperties = nd.Properties.Clone();

            node.Parameters.Clear();
            if (node.Kind != NodeLibraryItemKind.Tile)
            {
                var definitions = GraphNodeRegistry.Create(node.RegistryKey)?.Parameters;
                foreach (var pd in nd.Parameters)
                {
                    var definition = definitions?.FirstOrDefault(item =>
                        string.Equals(item.Name, pd.Name, StringComparison.Ordinal));
                    var param = definition?.CreateViewModel()
                        ?? new NodeParameterViewModel(pd.Name, pd.Kind, 0, 1, 0.01, new List<string>());
                    param.NumberValue = pd.NumberValue;
                    param.IntValue = pd.IntValue;
                    param.BoolValue = pd.BoolValue;
                    param.SelectedChoice = pd.SelectedChoice;
                    param.TextValue = pd.TextValue;
                    if (pd.ColorArgb is uint argb)
                    {
                        param.ColorValue = System.Windows.Media.Color.FromArgb(
                            (byte)(argb >> 24), (byte)(argb >> 16),
                            (byte)(argb >> 8), (byte)argb);
                    }
                    if (pd.PointListData.Count > 0)
                        param.PointListValue = new ObservableCollection<Point>(pd.PointListData);
                    node.Parameters.Add(param);
                    OnParameterCreated?.Invoke(param);
                }
            }

            if (node.TileType != null)
            {
                ConfigureTileNodePorts(node);
            }
            else
            {
                var proto = GraphNodeRegistry.Create(node.RegistryKey);
                if (proto != null)
                {
                    foreach (var port in proto.InputPorts)
                        node.InputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), false, port.StableKey));
                    foreach (var port in proto.OutputPorts)
                        node.OutputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), true, port.StableKey));
                }
            }

            _nodes.Add(node);
            created.Add(node);
        }

        foreach (var cd in data.Connections)
        {
            if (cd.StartNodeIndex < 0 || cd.StartNodeIndex >= created.Count) continue;
            if (cd.EndNodeIndex < 0 || cd.EndNodeIndex >= created.Count) continue;
            _connections.Add(new NodeConnectionViewModel
            {
                StartNode = created[cd.StartNodeIndex], StartPortIndex = cd.StartPortIndex,
                EndNode = created[cd.EndNodeIndex], EndPortIndex = cd.EndPortIndex,
                IsPreview = false
            });
        }

        ClearNodeSelection();
        foreach (var n in created) n.IsSelected = true;

        UpdateNodeCanvasExtent?.Invoke();
        RefreshConnectionsView?.Invoke();
        UpdateConnectionPositions?.Invoke(null);
        RequestPreviewRefresh?.Invoke(false);
        SetStatusText?.Invoke("Nodes pasted");
        return created;
    }

    /// <summary>Duplicates a node and returns the copy.</summary>
    public NodeViewModel DuplicateNode(NodeViewModel source)
    {
        var copy = new NodeViewModel(source.Title + " copy", source.X + 16, source.Y + 16, null)
        {
            Kind = source.Kind, TileType = source.TileType,
            TypeName = source.TypeName
        };

        copy.TileProperties = source.TileProperties?.Clone();

        foreach (var p in source.InputPorts)
            copy.InputPorts.Add(new NodePortViewModel(p.Name, p.Type, p.IsOutput, p.Key));
        foreach (var p in source.OutputPorts)
            copy.OutputPorts.Add(new NodePortViewModel(p.Name, p.Type, p.IsOutput, p.Key));

        foreach (var p in source.Parameters)
        {
            var newParam = new NodeParameterViewModel(
                p.Name, p.DisplayName, p.Kind, p.Min, p.Max, p.Step,
                p.Choices.ToList(), p.DisplayChoices.ToList())
            {
                DefaultNumberValue = p.DefaultNumberValue,
                DefaultIntValue = p.DefaultIntValue,
                DefaultBoolValue = p.DefaultBoolValue,
                DefaultChoiceValue = p.DefaultChoiceValue,
                DefaultColorValue = p.DefaultColorValue,
                DefaultTextValue = p.DefaultTextValue
            };
            // Preserve display choices for Choice parameters so they retain the current locale
            if (p.Kind == NodeParameterKind.Choice && p.DisplayChoices.Count > 0)
            {
                newParam.DisplayChoices.Clear();
                foreach (var dc in p.DisplayChoices)
                    newParam.DisplayChoices.Add(dc);
                newParam.RebuildChoiceMappings();
            }
            switch (p.Kind)
            {
                case NodeParameterKind.Integer:
                case NodeParameterKind.Seed:    newParam.IntValue = p.IntValue; break;
                case NodeParameterKind.Boolean: newParam.BoolValue = p.BoolValue; break;
                case NodeParameterKind.Choice:  newParam.SelectedChoice = p.SelectedChoice; break;
                case NodeParameterKind.Color:   newParam.ColorValue = p.ColorValue; break;
                case NodeParameterKind.Text:    newParam.TextValue = p.TextValue; break;
                case NodeParameterKind.PointList:
                    newParam.PointListValue = new ObservableCollection<Point>(p.PointListValue); break;
                default: newParam.NumberValue = p.NumberValue; break;
            }
            copy.Parameters.Add(newParam);
            OnParameterCreated?.Invoke(newParam);
        }

        if (copy.TileType != null)
            ConfigureTileNodePorts(copy);

        _nodes.Add(copy);
        UpdateNodeCanvasExtent?.Invoke();
        RequestPreviewRefresh?.Invoke(false);
        return copy;
    }

    // ─── Template data ──────────────────────────────────────────────

    public ProjectFileService.ProjectData BuildTemplateData(List<NodeViewModel> selectedNodes, int tileSize)
    {
        var data = new ProjectFileService.ProjectData { TileSize = tileSize };
        var nodeMap = new Dictionary<NodeViewModel, int>();

        for (var i = 0; i < selectedNodes.Count; i++)
        {
            nodeMap[selectedNodes[i]] = i;
            var node = selectedNodes[i];
            var nd = new ProjectFileService.NodeData
            {
                Title = node.Title, TypeName = node.TypeName, X = node.X, Y = node.Y,
                Kind = node.Kind, TileType = node.TileType,
                Properties = node.TileProperties?.Clone() ?? new TileProperties()
            };

            if (node.Kind != NodeLibraryItemKind.Tile)
            {
                foreach (var p in node.Parameters)
                    nd.Parameters.Add(new ProjectFileService.NodeParameterData
                    {
                        Name = p.Name, Kind = p.Kind,
                        NumberValue = p.NumberValue, IntValue = p.IntValue,
                        BoolValue = p.BoolValue, SelectedChoice = p.SelectedChoice,
                        TextValue = p.TextValue,
                        ColorArgb = PackColor(p.ColorValue),
                        PointListData = new List<Point>(p.PointListValue)
                    });
            }
            data.Nodes.Add(nd);
        }

        foreach (var conn in _connections.Where(c => !c.IsPreview && c.StartNode != null && c.EndNode != null))
        {
            if (nodeMap.TryGetValue(conn.StartNode!, out var si) && nodeMap.TryGetValue(conn.EndNode!, out var ei))
                data.Connections.Add(new ProjectFileService.ConnectionData { StartNodeIndex = si, StartPortIndex = conn.StartPortIndex, EndNodeIndex = ei, EndPortIndex = conn.EndPortIndex });
        }

        return data;
    }

    private static uint PackColor(System.Windows.Media.Color color)
        => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    // ─── Connection management ───────────────────────────────────────

    public bool CanSnapConnectionTarget(NodeViewModel startNode, int startPortIndex, NodeViewModel endNode, int endPortIndex)
    {
        if (endPortIndex < 0 || endPortIndex >= endNode.InputPorts.Count)
            return false;

        var inputPort = endNode.InputPorts[endPortIndex];

        if (startPortIndex >= 0 && startPortIndex < startNode.OutputPorts.Count)
        {
            var outputPort = startNode.OutputPorts[startPortIndex];
            if (!ArePortTypesCompatible(outputPort.Type, inputPort.Type))
                return false;
        }

        if (endNode.TileType == TileType.Grass && string.Equals(inputPort.Name, GrassCustomFlowerPortName, StringComparison.Ordinal))
            return startNode.TileType == null;

        return true;
    }

    public bool CanAcceptConnection(NodeViewModel startNode, int startPortIndex, NodeViewModel endNode, int endPortIndex, out string? rejectionMessage)
    {
        rejectionMessage = null;
        if (ReferenceEquals(startNode, endNode))
        {
            rejectionMessage = "A node cannot connect to itself.";
            return false;
        }
        if (startPortIndex < 0 || startPortIndex >= startNode.OutputPorts.Count)
        {
            rejectionMessage = "Invalid source output port";
            return false;
        }
        if (endPortIndex < 0 || endPortIndex >= endNode.InputPorts.Count)
        {
            rejectionMessage = "Invalid target input port";
            return false;
        }

        var inputPort = endNode.InputPorts[endPortIndex];

        if (startPortIndex >= 0 && startPortIndex < startNode.OutputPorts.Count)
        {
            var outputPort = startNode.OutputPorts[startPortIndex];
            if (!ArePortTypesCompatible(outputPort.Type, inputPort.Type))
            {
                rejectionMessage = inputPort.Type == PortValueType.Mask
                    ? "Mask ports can only connect to mask outputs" : "Image ports can only connect to image outputs";
                return false;
            }
        }

        var graphNode = GraphNodeRegistry.Create(endNode.RegistryKey);
        if (graphNode is IExclusiveInputNode)
        {
            var occupied = _connections.Any(c =>
                !c.IsPreview && ReferenceEquals(c.EndNode, endNode) && c.EndPortIndex != endPortIndex);
            if (occupied)
            {
                rejectionMessage = "This node can only connect to one input port at a time.";
                return false;
            }
        }

        var existingEdges = _connections
            .Where(connection => !connection.IsPreview
                && connection.StartNode != null && connection.EndNode != null
                && !(ReferenceEquals(connection.EndNode, endNode) && connection.EndPortIndex == endPortIndex))
            .Select(connection => new GraphConnection(
                connection.StartNode!.Id, connection.StartPortIndex,
                connection.EndNode!.Id, connection.EndPortIndex));
        var candidate = new GraphConnection(startNode.Id, startPortIndex, endNode.Id, endPortIndex);
        if (GraphValidator.WouldCreateCycle(_nodes.Select(node => node.Id), existingEdges, candidate))
        {
            rejectionMessage = "This connection would create a circular dependency.";
            return false;
        }

        if (endNode.TileType != TileType.Grass || !string.Equals(inputPort.Name, GrassCustomFlowerPortName, StringComparison.Ordinal))
            return true;

        if (startNode.TileType != null)
        {
            rejectionMessage = "Custom flower pattern cannot connect to optimized tile nodes. Connect a normal grayscale image node instead.";
            return false;
        }

        return true;
    }

    /// <summary>Returns the removed existing connections (for exclusive-input replacement).</summary>
    public static List<NodeConnectionViewModel> RemoveConflictingConnections(
        ObservableCollection<NodeConnectionViewModel> connections,
        NodeViewModel endNode, int endPortIndex)
    {
        var removed = connections.Where(c => !c.IsPreview && c.EndNode == endNode && c.EndPortIndex == endPortIndex).ToList();
        foreach (var c in removed)
            connections.Remove(c);
        return removed;
    }

    public void TrimInvalidConnectionsForNode(NodeViewModel node)
    {
        for (var i = _connections.Count - 1; i >= 0; i--)
        {
            var connection = _connections[i];
            if (ReferenceEquals(connection.StartNode, node) && connection.StartPortIndex >= node.OutputPorts.Count)
            {
                _connections.RemoveAt(i);
                continue;
            }
            if (ReferenceEquals(connection.EndNode, node) && connection.EndPortIndex >= node.InputPorts.Count)
                _connections.RemoveAt(i);
        }
    }

    // ─── Port configuration ─────────────────────────────────────────

    public void ConfigureTileNodePorts(NodeViewModel node)
    {
        if (node.TileType == null) return;

        var desiredInputs = new List<NodePortViewModel>();
        if (node.TileType == TileType.Grass && node.TileProperties?.GrassFlowerMode == GrassFlowerMode.Custom)
            desiredInputs.Add(new NodePortViewModel(GrassCustomFlowerPortName, PortValueType.Image, false));

        var desiredOutputs = new List<NodePortViewModel>
        {
            new NodePortViewModel("Bitmap", PortValueType.Tile, true)
        };

        if (PortsMatch(node.InputPorts, desiredInputs) && PortsMatch(node.OutputPorts, desiredOutputs))
            return;

        node.InputPorts.Clear();
        foreach (var port in desiredInputs)
            node.InputPorts.Add(port);

        node.OutputPorts.Clear();
        foreach (var port in desiredOutputs)
            node.OutputPorts.Add(port);

        TrimInvalidConnectionsForNode(node);
        RefreshConnectionsView?.Invoke();
        UpdateConnectionPositions?.Invoke(node);
    }

    public static bool PortsMatch(IReadOnlyList<NodePortViewModel> existing, IReadOnlyList<NodePortViewModel> desired)
    {
        if (existing.Count != desired.Count) return false;
        for (var i = 0; i < existing.Count; i++)
        {
            if (!string.Equals(existing[i].Name, desired[i].Name, StringComparison.Ordinal) ||
                !string.Equals(existing[i].Key, desired[i].Key, StringComparison.Ordinal) ||
                existing[i].Type != desired[i].Type || existing[i].IsOutput != desired[i].IsOutput)
                return false;
        }
        return true;
    }

    // ─── Port helpers ────────────────────────────────────────────────

    public static int GetInputPortIndex(NodeViewModel node, string portName)
    {
        for (var i = 0; i < node.InputPorts.Count; i++)
        {
            if (string.Equals(node.InputPorts[i].Name, portName, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    public static Point GetPortPosition(NodeViewModel node, bool isOutput, int index)
    {
        const double portStartY = 51d, portRowHeight = 16d, nodeWidth = 180d, portSize = 14d;
        var selOff = node.IsSelected ? 1 : 0;
        return new Point(
            isOutput
                ? node.X + nodeWidth + 13 - portSize * 0.5 - selOff
                : node.X - 13 + portSize * 0.5 + selOff,
            node.Y + portStartY + index * portRowHeight);
    }

    public static PortValueType MapGraphPortType(GraphPortType graphPortType) => graphPortType switch
    {
        GraphPortType.Image => PortValueType.Image,
        GraphPortType.Mask  => PortValueType.Mask,
        GraphPortType.Float => PortValueType.Float,
        GraphPortType.Color => PortValueType.Color,
        GraphPortType.Any   => PortValueType.Any,
        GraphPortType.Particle => PortValueType.Particle,
        _                   => PortValueType.Float
    };

    public static bool ArePortTypesCompatible(PortValueType outputType, PortValueType inputType)
    {
        return GraphValidator.AreCompatible(MapPortValueType(outputType), MapPortValueType(inputType));
        /*if (inputType == PortValueType.Any || outputType == inputType) return true;
        // Image/Tile can connect to Mask — mask blending reads the R channel
        if (outputType is PortValueType.Image or PortValueType.Tile)
            return inputType is PortValueType.Image or PortValueType.Tile or PortValueType.Mask;
        return false;*/
    }

    public static GraphPortType MapPortValueType(PortValueType portType) => portType switch
    {
        PortValueType.Image or PortValueType.Tile => GraphPortType.Image,
        PortValueType.Mask => GraphPortType.Mask,
        PortValueType.Color => GraphPortType.Color,
        PortValueType.Any => GraphPortType.Any,
        PortValueType.Particle => GraphPortType.Particle,
        PortValueType.Float or PortValueType.Integer or PortValueType.Boolean => GraphPortType.Float,
        _ => GraphPortType.Any
    };

    public static bool IsGraphPortTypeCompatible(PortValueType outputType, GraphPortType inputType)
    {
        if (inputType == GraphPortType.Any) return true;
        var outputIsImage = outputType is PortValueType.Image or PortValueType.Tile;
        var inputIsImage = inputType is GraphPortType.Image;
        if (outputIsImage && inputIsImage) return true;
        if (outputIsImage && inputType == GraphPortType.Mask) return true;
        return outputType switch
        {
            PortValueType.Mask => inputType == GraphPortType.Mask,
            PortValueType.Float or PortValueType.Integer or PortValueType.Boolean => inputType == GraphPortType.Float,
            PortValueType.Color => inputType == GraphPortType.Color,
            PortValueType.Any => true,
            PortValueType.Tile => inputType == GraphPortType.Image,
            _ => false
        };
    }

    // ─── Parameter management ────────────────────────────────────────

    public void ApplyParameterDefinitions(NodeViewModel node, IEnumerable<NodeParameterDefinition> definitions)
    {
        node.Parameters.Clear();
        foreach (var definition in definitions)
            node.Parameters.Add(CreateParameterViewModel(definition));
    }

    public static NodeParameterViewModel CreateParameterViewModel(NodeParameterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var parameter = new NodeParameterViewModel(definition.Name, definition.DisplayName, definition.Kind, definition.Min, definition.Max, definition.Step, definition.Choices, definition.DisplayChoices)
        {
            DefaultNumberValue = definition.DefaultNumber,
            DefaultIntValue = definition.DefaultInt,
            DefaultBoolValue = definition.DefaultBool,
            DefaultChoiceValue = definition.DefaultChoice ?? definition.Choices.FirstOrDefault(),
            DefaultColorValue = definition.DefaultColor,
            DefaultTextValue = definition.DefaultText
        };
        switch (definition.Kind)
        {
            case NodeParameterKind.Seed:
            case NodeParameterKind.Integer: parameter.IntValue = definition.DefaultInt; break;
            case NodeParameterKind.Boolean: parameter.BoolValue = definition.DefaultBool; break;
            case NodeParameterKind.Choice:  parameter.SelectedChoice = parameter.DefaultChoiceValue; break;
            case NodeParameterKind.Color:   parameter.ColorValue = definition.DefaultColor; break;
            case NodeParameterKind.PointList: break;
            case NodeParameterKind.Text:    parameter.TextValue = definition.DefaultText; break;
            default: parameter.NumberValue = definition.DefaultNumber; break;
        }
        return parameter;
    }

    public IReadOnlyList<NodeParameterDefinition> GetParameterDefinitions(
        string nodeTitle, NodeLibraryItemKind kind,
        Func<string, NodeLibraryItemKind, NodeLibraryItem?>? findLibraryItem = null,
        Func<string, NodeLibraryItemKind, IReadOnlyList<NodeParameterDefinition>>? fallback = null)
    {
        if (findLibraryItem != null)
        {
            var item = findLibraryItem(nodeTitle, kind);
            if (item != null) return item.Parameters;
        }
        if (fallback != null) return fallback(nodeTitle, kind);
        return kind switch
        {
            NodeLibraryItemKind.Tile => Array.Empty<NodeParameterDefinition>(),
            NodeLibraryItemKind.Compute => NodeLibraryService.CreateComputeParameters(),
            NodeLibraryItemKind.Composite => NodeLibraryService.CreateCompositeParameters(),
            _ => Array.Empty<NodeParameterDefinition>()
        };
    }

    public void EnsureNodeParametersInitialized(NodeViewModel node,
        Func<string, NodeLibraryItemKind, NodeLibraryItem?>? findLibraryItem = null)
    {
        if (node.Parameters.Count > 0 &&
            !node.Parameters.All(p =>
                (p.Kind == NodeParameterKind.Number || p.Kind == NodeParameterKind.Integer || p.Kind == NodeParameterKind.Seed) &&
                (p.NumberValue == 0 && p.IntValue == 0)))
            return;

        ApplyParameterDefinitions(node, GetParameterDefinitions(node.Title, node.Kind, findLibraryItem));
        RequestPreviewRefresh?.Invoke(false);
    }

    public static void ApplyNodeParameters(TileProperties props, NodeViewModel node)
    {
        if (node.TileProperties != null)
            props.CopyFrom(node.TileProperties);
    }
}
