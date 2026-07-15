using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelAssetGenerator.Core;

public enum GraphDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A machine-readable graph problem that can be shown by the UI or returned to AI.</summary>
public sealed record GraphDiagnostic(
    string Code,
    GraphDiagnosticSeverity Severity,
    string Message,
    int? NodeId = null,
    int? PortIndex = null,
    string? Suggestion = null);

public sealed class GraphValidationResult
{
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.All(d => d.Severity != GraphDiagnosticSeverity.Error);

    public GraphValidationResult(IEnumerable<GraphDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics.ToArray();
    }
}

/// <summary>
/// Single source of truth for graph structure and port compatibility checks.
/// The evaluator, canvas and AI tools all use this contract.
/// </summary>
public static class GraphValidator
{
    public static bool AreCompatible(GraphPortType output, GraphPortType input)
    {
        if (output == GraphPortType.Any || input == GraphPortType.Any || output == input)
            return true;

        // Images are intentionally accepted as masks; the evaluator creates a luminance view.
        return output == GraphPortType.Image && input == GraphPortType.Mask;
    }

    public static GraphValidationResult Validate(
        IReadOnlyList<GraphNodeInstance> nodes,
        IReadOnlyList<GraphConnection> connections,
        bool requireCompleteGraph = false)
    {
        var diagnostics = new List<GraphDiagnostic>();
        var nodeById = new Dictionary<int, GraphNodeInstance>();

        foreach (var node in nodes)
        {
            if (!nodeById.TryAdd(node.Id, node))
                diagnostics.Add(new("duplicate_node_id", GraphDiagnosticSeverity.Error,
                    $"Node id {node.Id} is duplicated.", node.Id));
        }

        var validConnections = new List<GraphConnection>();
        var uniqueConnections = new HashSet<GraphConnection>();
        var occupiedInputs = new Dictionary<(int NodeId, int Port), GraphConnection>();

        foreach (var connection in connections)
        {
            if (!uniqueConnections.Add(connection))
            {
                diagnostics.Add(new("duplicate_connection", GraphDiagnosticSeverity.Warning,
                    "The same connection appears more than once.", connection.TargetNodeId,
                    connection.TargetPortIndex, "Keep only one copy of the connection."));
                continue;
            }

            if (!nodeById.TryGetValue(connection.SourceNodeId, out var source))
            {
                diagnostics.Add(new("dangling_source", GraphDiagnosticSeverity.Error,
                    $"Connection references missing source node {connection.SourceNodeId}.",
                    connection.SourceNodeId));
                continue;
            }

            if (!nodeById.TryGetValue(connection.TargetNodeId, out var target))
            {
                diagnostics.Add(new("dangling_target", GraphDiagnosticSeverity.Error,
                    $"Connection references missing target node {connection.TargetNodeId}.",
                    connection.TargetNodeId));
                continue;
            }

            if (connection.SourceNodeId == connection.TargetNodeId)
            {
                diagnostics.Add(new("self_connection", GraphDiagnosticSeverity.Error,
                    "A node cannot connect to itself.", connection.TargetNodeId));
                continue;
            }

            if (connection.SourcePortIndex < 0 || connection.SourcePortIndex >= source.Node.OutputPorts.Count)
            {
                diagnostics.Add(new("invalid_source_port", GraphDiagnosticSeverity.Error,
                    $"Output port {connection.SourcePortIndex} does not exist on {source.Node.TypeName}.",
                    source.Id, connection.SourcePortIndex));
                continue;
            }

            if (connection.TargetPortIndex < 0 || connection.TargetPortIndex >= target.Node.InputPorts.Count)
            {
                diagnostics.Add(new("invalid_target_port", GraphDiagnosticSeverity.Error,
                    $"Input port {connection.TargetPortIndex} does not exist on {target.Node.TypeName}.",
                    target.Id, connection.TargetPortIndex));
                continue;
            }

            var output = source.Node.OutputPorts[connection.SourcePortIndex];
            var input = target.Node.InputPorts[connection.TargetPortIndex];
            if (!AreCompatible(output.Type, input.Type))
            {
                diagnostics.Add(new("incompatible_ports", GraphDiagnosticSeverity.Error,
                    $"{source.Node.TypeName}.{output.Name} ({output.Type}) cannot connect to " +
                    $"{target.Node.TypeName}.{input.Name} ({input.Type}).",
                    target.Id, connection.TargetPortIndex, "Choose a compatible port or insert a conversion node."));
                continue;
            }

            var inputKey = (connection.TargetNodeId, connection.TargetPortIndex);
            if (!input.AllowsMultipleConnections && occupiedInputs.ContainsKey(inputKey))
            {
                diagnostics.Add(new("input_already_connected", GraphDiagnosticSeverity.Error,
                    $"Input {target.Node.TypeName}.{input.Name} accepts only one connection.",
                    target.Id, connection.TargetPortIndex, "Disconnect the old edge before connecting a new one."));
                continue;
            }

            occupiedInputs[inputKey] = connection;
            validConnections.Add(connection);
        }

        foreach (var node in nodes)
        {
            for (var i = 0; i < node.Node.InputPorts.Count; i++)
            {
                if (node.Node.InputPorts[i].IsRequired && !occupiedInputs.ContainsKey((node.Id, i)))
                    diagnostics.Add(new("required_input_missing",
                        requireCompleteGraph ? GraphDiagnosticSeverity.Error : GraphDiagnosticSeverity.Warning,
                        $"Required input {node.Node.TypeName}.{node.Node.InputPorts[i].Name} is not connected.",
                        node.Id, i));
            }
        }

        if (HasCycle(nodeById.Keys, validConnections))
            diagnostics.Add(new("cycle", GraphDiagnosticSeverity.Error,
                "The graph contains a circular dependency.", Suggestion: "Remove one edge from the cycle."));

        if (nodes.Count > 0 && !nodes.Any(n => n.Node.TypeName.Equals("Output", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add(new("no_output_node", GraphDiagnosticSeverity.Warning,
                "The graph has no explicit Output node.", Suggestion: "Add an Output node to make the final result unambiguous."));

        return new GraphValidationResult(diagnostics);
    }

    public static bool WouldCreateCycle(
        IEnumerable<int> nodeIds,
        IEnumerable<GraphConnection> connections,
        GraphConnection candidate) => HasCycle(nodeIds, connections.Append(candidate));

    private static bool HasCycle(IEnumerable<int> nodeIds, IEnumerable<GraphConnection> connections)
    {
        var ids = nodeIds.ToHashSet();
        var inDegree = ids.ToDictionary(id => id, _ => 0);
        var outgoing = ids.ToDictionary(id => id, _ => new List<int>());

        foreach (var edge in connections)
        {
            if (!ids.Contains(edge.SourceNodeId) || !ids.Contains(edge.TargetNodeId)) continue;
            outgoing[edge.SourceNodeId].Add(edge.TargetNodeId);
            inDegree[edge.TargetNodeId]++;
        }

        var queue = new Queue<int>(inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            visited++;
            foreach (var target in outgoing[id])
                if (--inDegree[target] == 0) queue.Enqueue(target);
        }

        return visited != ids.Count;
    }
}
