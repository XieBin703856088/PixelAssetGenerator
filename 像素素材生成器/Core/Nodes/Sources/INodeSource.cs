using System.Collections.Generic;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Core.Nodes.Sources;

/// <summary>
/// Abstraction over a source of graph node types.
/// Each source provides a set of node types that can be discovered,
/// instantiated, and (for some sources) deleted or modified.
/// </summary>
public interface INodeSource
{
    /// <summary>Unique name for this source (e.g. "builtin", "file", "dynamic").</summary>
    string SourceName { get; }

    /// <summary>The kind of source — determines editability / deletability.</summary>
    NodeResourceSourceKind SourceKind { get; }

    /// <summary>Returns metadata for all available node types from this source.</summary>
    IReadOnlyList<NodeResourceMetadata> GetAvailableTypes();

    /// <summary>Creates a new instance of a node by type name. Returns null if not found.</summary>
    IGraphNode? CreateNode(string typeName);

    /// <summary>Returns the full resource definition (null if this source doesn't store it).</summary>
    NodeResource? LoadResource(string typeName);

    /// <summary>Whether nodes from this source can be deleted at runtime.</summary>
    bool CanDelete => false;

    /// <summary>Delete a node type from this source. Throws if CanDelete is false.</summary>
    void Delete(string typeName) => throw new System.InvalidOperationException($"Source '{SourceName}' does not support deletion.");

    /// <summary>Called when the source should refresh its available types (if applicable).</summary>
    void Refresh() { }
}
