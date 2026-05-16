using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PixelAssetGenerator.Core.Nodes;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Core.Nodes.Sources;

/// <summary>
/// Scans a directory for *.node.json files and loads them as node types.
/// Each .node.json file represents one dynamically-loadable node.
/// </summary>
public sealed class FileNodeSource : INodeSource
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private ConcurrentDictionary<string, FileNodeEntry> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private List<NodeResourceMetadata> _cachedMetadata = new();

    public string SourceName => "file";
    public NodeResourceSourceKind SourceKind => NodeResourceSourceKind.File;
    public bool CanDelete => true;

    public FileNodeSource(string directory)
    {
        _directory = directory;
        Refresh();
    }

    /// <summary>Directory where .node.json files are stored.</summary>
    public string DirectoryPath => _directory;

    public void Refresh()
    {
        var updated = new ConcurrentDictionary<string, FileNodeEntry>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_directory))
        {
            foreach (var filePath in Directory.EnumerateFiles(_directory, "*.node.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var resource = JsonSerializer.Deserialize<NodeResource>(json, _jsonOptions);
                    if (resource?.Identity == null || string.IsNullOrWhiteSpace(resource.Identity.TypeName))
                        continue;

                    var typeName = resource.Identity.TypeName;
                    updated[typeName] = new FileNodeEntry
                    {
                        FilePath = filePath,
                        Resource = resource
                    };
                }
                catch
                {
                    // Skip invalid files
                }
            }
        }

        _nodes = updated;

        // Rebuild metadata cache
        _cachedMetadata = _nodes.Values.Select(e => new NodeResourceMetadata
        {
            TypeName = e.Resource.Identity.TypeName,
            DisplayName = e.Resource.Identity.DisplayName.Get("zh-Hans"),
            Category = e.Resource.Identity.Category,
            Subcategory = e.Resource.Identity.Subcategory ?? "",
            Description = e.Resource.Identity.Description.Get("zh-Hans"),
            SourceKind = NodeResourceSourceKind.File,
            InputPortCount = e.Resource.Ports?.Inputs?.Count ?? 0,
            OutputPortCount = e.Resource.Ports?.Outputs?.Count ?? 0,
            ParameterCount = e.Resource.Parameters?.Count ?? 0
        }).ToList();
    }

    public IReadOnlyList<NodeResourceMetadata> GetAvailableTypes() => _cachedMetadata;

    public IGraphNode? CreateNode(string typeName)
    {
        if (!_nodes.TryGetValue(typeName, out var entry))
            return null;

        var res = entry.Resource;
        return ResourceNodeInstance.FromResource(res, entry.FilePath);
    }

    public NodeResource? LoadResource(string typeName)
    {
        return _nodes.TryGetValue(typeName, out var entry) ? entry.Resource : null;
    }

    public void Delete(string typeName)
    {
        if (!_nodes.TryGetValue(typeName, out var entry))
            throw new InvalidOperationException($"Node '{typeName}' not found in file source.");

        try
        {
            if (File.Exists(entry.FilePath))
                File.Delete(entry.FilePath);
            _nodes.TryRemove(typeName, out _);
            Refresh();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to delete '{typeName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes a .node.json file and refreshes the catalog.
    /// </summary>
    public void WriteResource(NodeResource resource)
    {
        if (resource.Identity == null || string.IsNullOrWhiteSpace(resource.Identity.TypeName))
            throw new ArgumentException("Resource must have a typeName");

        Directory.CreateDirectory(_directory);

        var fileName = SanitizeFileName(resource.Identity.TypeName) + ".node.json";
        var filePath = System.IO.Path.Combine(_directory, fileName);

        var json = JsonSerializer.Serialize(resource, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);

        Refresh();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private sealed class FileNodeEntry
    {
        public string FilePath { get; set; } = "";
        public NodeResource Resource { get; set; } = new();
    }
}
