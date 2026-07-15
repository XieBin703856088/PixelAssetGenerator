using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PixelAssetGenerator.Core;

public sealed record GraphEvaluationMetrics(
    TimeSpan Duration,
    int NodeCount,
    int EvaluatedNodeCount,
    int CacheHitCount,
    bool ReusedExecutionPlan,
    int DiagnosticCount)
{
    public double CacheHitRate => NodeCount == 0 ? 0 : CacheHitCount / (double)NodeCount;
}

/// <summary>
/// Keeps one immutable output snapshot per node instance. Entries are replaced when the
/// structural fingerprint changes, so memory use is bounded by the size of the active graph.
/// </summary>
internal sealed class GraphEvaluationCache : IDisposable
{
    private sealed record Entry(ulong Fingerprint, PixelBuffer[] Buffers);

    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = new();

    public bool TryGet(int nodeId, ulong fingerprint, out PixelBuffer[] buffers)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(nodeId, out var entry) && entry.Fingerprint == fingerprint)
            {
                buffers = Clone(entry.Buffers);
                return true;
            }
        }

        buffers = Array.Empty<PixelBuffer>();
        return false;
    }

    public void Store(int nodeId, ulong fingerprint, PixelBuffer[] buffers)
    {
        var snapshot = Clone(buffers);
        lock (_gate)
        {
            if (_entries.Remove(nodeId, out var previous)) Dispose(previous.Buffers);
            _entries[nodeId] = new Entry(fingerprint, snapshot);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values) Dispose(entry.Buffers);
            _entries.Clear();
        }
    }

    public void RetainNodes(IReadOnlySet<int> activeNodeIds)
    {
        lock (_gate)
        {
            foreach (var nodeId in _entries.Keys.Where(id => !activeNodeIds.Contains(id)).ToArray())
            {
                Dispose(_entries[nodeId].Buffers);
                _entries.Remove(nodeId);
            }
        }
    }

    public void Dispose() => Clear();

    private static PixelBuffer[] Clone(PixelBuffer[] buffers) => buffers.Select(buffer => buffer.Clone()).ToArray();

    private static void Dispose(IEnumerable<PixelBuffer> buffers)
    {
        foreach (var buffer in buffers) buffer.Dispose();
    }
}

internal static class GraphFingerprint
{
    // FNV-1a is fast and deterministic. This fingerprint is only an in-process cache key.
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong ForNode(
        GraphNodeInstance instance,
        IReadOnlyDictionary<string, object> parameters,
        PixelGraphContext context,
        IEnumerable<(int Port, ulong Fingerprint)> inputs)
    {
        var builder = new StringBuilder(256);
        builder.Append(instance.Node.TypeName).Append('|')
            .Append(context.Seed).Append('|')
            .Append(context.TileSize).Append('|')
            .Append(context.OutputSize?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');

        foreach (var parameter in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(parameter.Key).Append('=');
            AppendValue(builder, parameter.Value);
            builder.Append(';');
        }

        if (context.SemanticOverrides != null)
        {
            foreach (var value in context.SemanticOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append("semantic:").Append(value.Key).Append('=')
                    .Append(value.Value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        foreach (var input in inputs.OrderBy(value => value.Port))
            builder.Append("input:").Append(input.Port).Append('=')
                .Append(input.Fingerprint.ToString("X16", CultureInfo.InvariantCulture)).Append(';');

        return Hash(builder.ToString());
    }

    public static ulong ForStructure(
        IReadOnlyList<GraphNodeInstance> nodes,
        IReadOnlyList<GraphConnection> connections)
    {
        var builder = new StringBuilder(nodes.Count * 12 + connections.Count * 24);
        foreach (var node in nodes.OrderBy(node => node.Id))
            builder.Append(node.Id).Append(':').Append(node.Node.TypeName).Append(';');
        builder.Append('|');
        foreach (var edge in connections.OrderBy(edge => edge.SourceNodeId)
                     .ThenBy(edge => edge.SourcePortIndex).ThenBy(edge => edge.TargetNodeId)
                     .ThenBy(edge => edge.TargetPortIndex))
            builder.Append(edge.SourceNodeId).Append(':').Append(edge.SourcePortIndex).Append('>')
                .Append(edge.TargetNodeId).Append(':').Append(edge.TargetPortIndex).Append(';');
        return Hash(builder.ToString());
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case string text:
                builder.Append(text);
                break;
            case IFormattable formattable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            case IEnumerable sequence:
                builder.Append('[');
                foreach (var item in sequence)
                {
                    AppendValue(builder, item);
                    builder.Append(',');
                }
                builder.Append(']');
                break;
            default:
                builder.Append(value);
                break;
        }
    }

    private static ulong Hash(string value)
    {
        var hash = Offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= Prime;
        }
        return hash;
    }
}
