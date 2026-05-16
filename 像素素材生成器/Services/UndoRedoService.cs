using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Snapshot of a single node's full state for undo/redo.
/// </summary>
public sealed record NodeSnapshot(
    int Id,
    string Title,
    double X,
    double Y,
    NodeLibraryItemKind Kind,
    string Category,
    bool IsSelected,
    TileType? TileType,
    TileProperties? TileProperties,
    IReadOnlyList<ParameterSnapshot> Parameters,
    IReadOnlyList<PortSnapshot> InputPorts,
    IReadOnlyList<PortSnapshot> OutputPorts);

/// <summary>
/// Snapshot of a single parameter.
/// </summary>
public sealed record ParameterSnapshot(
    string Name,
    NodeParameterKind Kind,
    double NumberValue,
    int IntValue,
    bool BoolValue,
    string? SelectedChoice,
    IReadOnlyList<Point> PointListValue,
    System.Windows.Media.Color ColorValue);

/// <summary>
/// Snapshot of a single port.
/// </summary>
public sealed record PortSnapshot(string Name, PortValueType Type, bool IsOutput);

/// <summary>
/// Snapshot of a single connection.
/// </summary>
public sealed record ConnectionSnapshot(
    int StartNodeId,
    int StartPortIndex,
    int EndNodeId,
    int EndPortIndex);

/// <summary>
/// Complete graph state snapshot for undo/redo operations.
/// </summary>
public sealed record GraphSnapshot(
    IReadOnlyList<NodeSnapshot> Nodes,
    IReadOnlyList<ConnectionSnapshot> Connections);

/// <summary>
/// Undo/redo service for the node graph.
/// Uses snapshot-based approach: captures full graph state before mutations.
/// </summary>
public sealed class UndoRedoService
{
    private readonly LinkedList<GraphSnapshot> _undoStack = new();
    private readonly LinkedList<GraphSnapshot> _redoStack = new();
    private const int MaxUndoLevels = 60;

    /// <summary>
    /// Fired when the undo/redo state changes (for UI update of button enabled states).
    /// </summary>
    public event Action? StateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Captures the current graph state as a snapshot and pushes it onto the undo stack.
    /// Call this BEFORE making mutations.
    /// </summary>
    public void RecordSnapshot(
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections)
    {
        var snapshot = CreateSnapshot(nodes, connections);
        _undoStack.AddLast(snapshot);
        _redoStack.Clear();

        if (_undoStack.Count > MaxUndoLevels)
            _undoStack.RemoveFirst();

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Undo: restores the most recent snapshot and pushes the current state onto the redo stack.
    /// Returns the snapshot to restore, or null if nothing to undo.
    /// </summary>
    public GraphSnapshot? Undo(
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections)
    {
        if (_undoStack.Count == 0) return null;

        // Save current state onto redo stack
        var currentSnapshot = CreateSnapshot(nodes, connections);
        _redoStack.AddLast(currentSnapshot);

        // Pop and restore the undo snapshot
        var snapshot = _undoStack.Last!.Value;
        _undoStack.RemoveLast();

        StateChanged?.Invoke();
        return snapshot;
    }

    /// <summary>
    /// Redo: restores the most recent redo snapshot and pushes current state onto undo stack.
    /// Returns the snapshot to restore, or null if nothing to redo.
    /// </summary>
    public GraphSnapshot? Redo(
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections)
    {
        if (_redoStack.Count == 0) return null;

        // Save current state onto undo stack
        var currentSnapshot = CreateSnapshot(nodes, connections);
        _undoStack.AddLast(currentSnapshot);

        // Pop and restore the redo snapshot
        var snapshot = _redoStack.Last!.Value;
        _redoStack.RemoveLast();

        StateChanged?.Invoke();
        return snapshot;
    }

    /// <summary>
    /// Clears both undo and redo stacks (e.g., when loading a new project).
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Applies a snapshot to the given collections, replacing their contents.
    /// </summary>
    public static void ApplySnapshot(
        GraphSnapshot snapshot,
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections)
    {
        // Build node ID map: old IDs in snapshot → new NodeViewModel instances
        nodes.Clear();
        connections.Clear();

        var idMap = new Dictionary<int, NodeViewModel>();

        foreach (var ns in snapshot.Nodes)
        {
            var previewBrush = System.Windows.Media.Brushes.DimGray; // will be refreshed by preview system
            var node = new NodeViewModel(ns.Title, ns.X, ns.Y, previewBrush)
            {
                Kind = ns.Kind,
                Category = ns.Category,
                TileType = ns.TileType,
                TileProperties = ns.TileProperties?.Clone()
            };

            foreach (var ps in ns.Parameters)
            {
                var pvm = new NodeParameterViewModel(ps.Name, ps.Kind, 0, 1, 0.01, new List<string>())
                {
                    NumberValue = ps.NumberValue,
                    IntValue = ps.IntValue,
                    BoolValue = ps.BoolValue,
                    SelectedChoice = ps.SelectedChoice,
                    ColorValue = ps.ColorValue
                };
                pvm.PointListValue.Clear();
                foreach (var pt in ps.PointListValue)
                    pvm.PointListValue.Add(pt);
                node.Parameters.Add(pvm);
            }

            foreach (var ps in ns.InputPorts)
                node.InputPorts.Add(new NodePortViewModel(ps.Name, ps.Type, ps.IsOutput));
            foreach (var ps in ns.OutputPorts)
                node.OutputPorts.Add(new NodePortViewModel(ps.Name, ps.Type, ps.IsOutput));

            nodes.Add(node);
            idMap[ns.Id] = node;
        }

        foreach (var cs in snapshot.Connections)
        {
            if (idMap.TryGetValue(cs.StartNodeId, out var startNode) &&
                idMap.TryGetValue(cs.EndNodeId, out var endNode))
            {
                connections.Add(new NodeConnectionViewModel
                {
                    StartNode = startNode,
                    StartPortIndex = cs.StartPortIndex,
                    EndNode = endNode,
                    EndPortIndex = cs.EndPortIndex
                });
            }
        }

        // Restore selection states
        foreach (var ns in snapshot.Nodes)
        {
            if (idMap.TryGetValue(ns.Id, out var node))
                node.IsSelected = ns.IsSelected;
            else
            {
                // Calculate positions for orphaned selection states
            }
        }
    }

    /// <summary>
    /// Queries the last snapshot on the undo stack without popping it.
    /// Used to compare and avoid recording duplicate snapshots.
    /// </summary>
    public GraphSnapshot? PeekUndo() => _undoStack.Count > 0 ? _undoStack.Last!.Value : null;

    private static GraphSnapshot CreateSnapshot(
        ObservableCollection<NodeViewModel> nodes,
        ObservableCollection<NodeConnectionViewModel> connections)
    {
        var nodeSnapshots = new List<NodeSnapshot>(nodes.Count);
        foreach (var node in nodes)
        {
            var paramSnapshots = new List<ParameterSnapshot>(node.Parameters.Count);
            foreach (var p in node.Parameters)
            {
                paramSnapshots.Add(new ParameterSnapshot(
                    p.Name, p.Kind,
                    p.NumberValue, p.IntValue, p.BoolValue,
                    p.SelectedChoice,
                    p.PointListValue.ToArray(),
                    p.ColorValue));
            }

            var inputPorts = node.InputPorts.Select(p => new PortSnapshot(p.Name, p.Type, p.IsOutput)).ToArray();
            var outputPorts = node.OutputPorts.Select(p => new PortSnapshot(p.Name, p.Type, p.IsOutput)).ToArray();

            nodeSnapshots.Add(new NodeSnapshot(
                node.Id, node.Title,
                node.X, node.Y,
                node.Kind, node.Category,
                node.IsSelected,
                node.TileType,
                node.TileProperties?.Clone(),
                paramSnapshots, inputPorts, outputPorts));
        }

        var connectionSnapshots = new List<ConnectionSnapshot>(connections.Count);
        foreach (var c in connections)
        {
            if (c.StartNode != null && c.EndNode != null)
            {
                connectionSnapshots.Add(new ConnectionSnapshot(
                    c.StartNode.Id, c.StartPortIndex,
                    c.EndNode.Id, c.EndPortIndex));
            }
        }

        return new GraphSnapshot(nodeSnapshots, connectionSnapshots);
    }
}
