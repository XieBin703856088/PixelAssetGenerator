using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Resolves a preview target inside the workflow selected by the user. Directional
/// traversal keeps independent animation graphs from stealing each other's preview.
/// </summary>
public static class WorkflowPreviewResolver
{
    public static NodeViewModel? Resolve(
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<NodeConnectionViewModel> connections,
        NodeViewModel? selected,
        Func<NodeViewModel, bool>? canPreview = null)
    {
        canPreview ??= static _ => true;
        var orderedNodes = nodes.Where(canPreview).ToList();
        if (orderedNodes.Count == 0) return selected;

        var graphConnections = connections
            .Where(connection => !connection.IsPreview
                && connection.StartNode != null
                && connection.EndNode != null)
            .ToList();

        var candidates = selected == null
            ? orderedNodes
            : GetDownstreamNodes(selected, orderedNodes, graphConnections);

        if (candidates.Count == 0 && selected != null && canPreview(selected))
            candidates.Add(selected);

        return candidates.FirstOrDefault(node => node.TypeName == "AnimationWorkflowOutput")
            ?? candidates.FirstOrDefault(node => node.TypeName == "ParticleRender")
            ?? candidates.FirstOrDefault(IsOutputNode)
            ?? candidates.FirstOrDefault(node => !graphConnections.Any(connection =>
                connection.StartNode?.Id == node.Id))
            ?? (selected != null && canPreview(selected) ? selected : candidates.FirstOrDefault());
    }

    private static List<NodeViewModel> GetDownstreamNodes(
        NodeViewModel selected,
        IReadOnlyList<NodeViewModel> orderedNodes,
        IReadOnlyList<NodeConnectionViewModel> connections)
    {
        var reachableIds = new HashSet<int> { selected.Id };
        var pending = new Queue<int>();
        pending.Enqueue(selected.Id);
        while (pending.Count > 0)
        {
            var currentId = pending.Dequeue();
            foreach (var connection in connections)
            {
                if (connection.StartNode?.Id != currentId || connection.EndNode == null) continue;
                if (reachableIds.Add(connection.EndNode.Id)) pending.Enqueue(connection.EndNode.Id);
            }
        }

        return orderedNodes.Where(node => reachableIds.Contains(node.Id)).ToList();
    }

    private static bool IsOutputNode(NodeViewModel node)
        => string.Equals(node.TypeName, "Output", StringComparison.Ordinal)
            || string.Equals(node.RegistryKey, "Output", StringComparison.Ordinal);
}
