using System.Collections.Generic;
using System.Linq;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Core.Nodes.Sources;

/// <summary>
/// Wraps the compile-time <see cref="GraphNodeRegistry"/> as an <see cref="INodeSource"/>.
/// Built-in nodes cannot be deleted or modified at runtime.
/// </summary>
public sealed class BuiltInNodeSource : INodeSource
{
    public string SourceName => "builtin";
    public NodeResourceSourceKind SourceKind => NodeResourceSourceKind.BuiltIn;

    public IReadOnlyList<NodeResourceMetadata> GetAvailableTypes()
    {
        var catalog = GraphNodeRegistry.GetCatalog();
        var catalogLookup = catalog.ToDictionary(r => r.Identity.TypeName, r => r.Identity.Description.Get("zh-Hans"));

        return GraphNodeRegistry.GetAllPrototypes().Select(p =>
        {
            catalogLookup.TryGetValue(p.TypeName, out var desc);
            desc ??= "";
            return new NodeResourceMetadata
            {
                TypeName = p.TypeName,
                DisplayName = p.TypeName,
                Category = p.Category.ToString(),
                Subcategory = "",
                Description = desc,
                SourceKind = NodeResourceSourceKind.BuiltIn,
                InputPortCount = p.InputPorts.Count,
                OutputPortCount = p.OutputPorts.Count,
                ParameterCount = p.Parameters.Count
            };
        }).ToList();
    }

    public IGraphNode? CreateNode(string typeName)
    {
        return GraphNodeRegistry.Create(typeName);
    }

    public NodeResource? LoadResource(string typeName)
    {
        // Built-in nodes don't have .node.json resources — return null
        return null;
    }
}
