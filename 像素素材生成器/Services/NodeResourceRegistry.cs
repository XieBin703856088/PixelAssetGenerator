using System;
using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Nodes.Sources;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Singleton aggregator for all node sources.
/// Provides a unified view of all available node types across built-in, file, and dynamic sources.
/// </summary>
public sealed class NodeResourceRegistry
{
    private static readonly Lazy<NodeResourceRegistry> _instance = new(() => new NodeResourceRegistry());
    public static NodeResourceRegistry Instance => _instance.Value;

    private readonly List<INodeSource> _sources = new();

    private NodeResourceRegistry() { }

    /// <summary>Registers a node source. Sources are queried in registration order.</summary>
    public void AddSource(INodeSource source)
    {
        _sources.Add(source);
    }

    /// <summary>Returns all registered sources.</summary>
    public IReadOnlyList<INodeSource> GetSources() => _sources;

    /// <summary>Finds which source owns a given type name.</summary>
    public INodeSource? GetSource(string typeName)
    {
        foreach (var source in _sources)
        {
            foreach (var meta in source.GetAvailableTypes())
            {
                if (string.Equals(meta.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                    return source;
            }
        }
        return null;
    }

    /// <summary>Creates a node instance by type name, checking all sources.</summary>
    public IGraphNode? Create(string typeName)
    {
        foreach (var source in _sources)
        {
            var node = source.CreateNode(typeName);
            if (node != null)
                return node;
        }
        return null;
    }

    /// <summary>Gets metadata for all available node types across all sources.</summary>
    public IReadOnlyList<Models.NodeResourceMetadata> GetAll()
    {
        return _sources.SelectMany(s => s.GetAvailableTypes()).ToList();
    }

    /// <summary>Loads the full resource definition for a node type (null for built-in nodes).</summary>
    public Models.NodeResource? LoadResource(string typeName)
    {
        foreach (var source in _sources)
        {
            var resource = source.LoadResource(typeName);
            if (resource != null)
                return resource;
        }
        return null;
    }

    /// <summary>Deletes a node type from its owning source. Throws if not deletable.</summary>
    public bool Delete(string typeName)
    {
        var source = GetSource(typeName);
        if (source == null)
            throw new InvalidOperationException($"Node '{typeName}' not found in any source.");

        if (!source.CanDelete)
            throw new InvalidOperationException($"Node '{typeName}' is from source '{source.SourceName}' and cannot be deleted.");

        source.Delete(typeName);
        return true;
    }

    /// <summary>Refreshes all sources (reloads file nodes, etc.).</summary>
    public void RefreshAll()
    {
        foreach (var source in _sources)
            source.Refresh();
    }
}
