using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using PixelAssetGenerator.Core.Nodes;
using PixelAssetGenerator.Core.Nodes.Sources;
using PixelAssetGenerator.Models;
namespace PixelAssetGenerator.Core;

/// <summary>
/// Registry of all available graph node types. Used by the UI to populate the node library
/// and by the evaluator to instantiate nodes by type name.
///
/// At startup, scans the Nodes/ directory for .node.json files. Each file's
/// processorType field maps to a compiled C# processor class. Nodes without
/// a processorType are instantiated via ResourceNodeInstance (script nodes).
/// </summary>
public static class GraphNodeRegistry
{
    private static readonly Dictionary<string, Func<IGraphNode>> NodeFactories = new();
    private static readonly List<IGraphNode> Prototypes = new();
    private static readonly List<NodeResource> Catalog = new();
    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>
    /// Optional fallback factory used by the Services layer to provide node types
    /// that are not registered in the Core layer (e.g. resource-based script nodes).
    /// Set this during application startup to break the Core→Services dependency.
    /// </summary>
    public static Func<string, IGraphNode?>? NodeFactoryFallback { get; set; }

    /// <summary>JSON options matching the .node.json schema.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Ensures the registry is loaded. Safe to call multiple times.</summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            LoadFromNodeFiles();
            _initialized = true;
        }
    }

    /// <summary>Scans the Nodes/ directory for .node.json files and registers factories.</summary>
    private static void LoadFromNodeFiles()
    {
        // Try multiple possible paths for the Nodes directory:
        // 1. Base directory (normal publish / debug)
        // 2. Assembly location directory (single-file publish extracts alongside)
        // 3. Parent of base directory (some single-file extraction layouts)
        var nodesDir = FindNodesDirectory();
        if (nodesDir == null) return;

        var nodeFiles = Directory.GetFiles(nodesDir, "*.node.json", SearchOption.AllDirectories);
        var typeMap = BuildProcessorTypeMap();

        foreach (var file in nodeFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var resource = JsonSerializer.Deserialize<NodeResource>(json, JsonOptions);
                if (resource == null || string.IsNullOrWhiteSpace(resource.Identity?.TypeName))
                    continue;

                // Apply category from parent directory name if not set
                if (string.IsNullOrWhiteSpace(resource.Identity.Category)
                    || resource.Identity.Category == "Tool")
                {
                    var parentDir = Path.GetDirectoryName(file);
                    if (parentDir != null)
                    {
                        var dirName = Path.GetFileName(parentDir);
                        if (dirName is not "Nodes" and not "Custom")
                            resource.Identity.Category = dirName;
                    }
                }

                var typeName = resource.Identity.TypeName;
                var processorType = resource.ProcessorType;

                if (!string.IsNullOrEmpty(processorType) && typeMap.TryGetValue(processorType, out var clrType))
                {
                    // Compiled C# processor node
                    var factory = CreateFactory(clrType);
                    NodeFactories[typeName] = factory;
                    Prototypes.Add(factory());
                    System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] Loaded compiled node '{typeName}' (proc={processorType}, cat={resource.Identity.Category})");
                }
                else if (resource.Script != null && !string.IsNullOrWhiteSpace(resource.Script.Code))
                {
                    // Script node — compiled at runtime via Roslyn
                    var instance = new ResourceNodeInstance(resource, file);
                    NodeFactories[typeName] = () => instance;
                    Prototypes.Add(instance);
                    System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] Loaded script node '{typeName}' (cat={resource.Identity.Category})");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] SKIPPED '{typeName}': processorType='{processorType}' foundInTypeMap={typeMap.ContainsKey(processorType ?? "")} hasScript={resource.Script?.Code?.Length > 0}");
                }

                Catalog.Add(resource);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GraphNodeRegistry skipped invalid node file '{file}': {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] ERROR loading '{file}': {ex.Message}");
            }
        }
    }

    /// <summary>Builds a map of {TypeName → CLR Type} for all GraphNodeBase subclasses in the assembly.</summary>
    private static Dictionary<string, Type> BuildProcessorTypeMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var allTypes = Assembly.GetExecutingAssembly().GetTypes();
            System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] Assembly.GetTypes() returned {allTypes.Length} types");
            foreach (var type in allTypes)
            {
                if (typeof(IGraphNode).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IGraphNode node)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry]  Registering type {type.FullName} with TypeName='{node.TypeName}' Category='{node.Category}'");
                            map[node.TypeName] = type;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry]  FAILED CreateInstance for {type.FullName}: Activator.CreateInstance succeeded but is not IGraphNode");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry]  FAILED to instantiate {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // Also log all IGraphNode types found (even failed ones)
            foreach (var type in allTypes)
            {
                if (typeof(IGraphNode).IsAssignableFrom(type) && !type.IsAbstract)
                    System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry]  IGraphNode type found: {type.FullName}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"GraphNodeRegistry.BuildProcessorTypeMap failed: {ex.Message}"); }

        System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] BuildProcessorTypeMap: {map.Count} types");
        foreach (var __kv in map)
            System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry]  TypeMap '{__kv.Key}' -> {__kv.Value.Name}");
        // Check for Animation-relevant types specifically
        System.Diagnostics.Debug.WriteLine($"[GraphNodeRegistry] Contains 'AnimatedParameter': {map.ContainsKey("AnimatedParameter")}, 'FrameBlend': {map.ContainsKey("FrameBlend")}, 'Time': {map.ContainsKey("Time")}");

        return map;
    }

    private static Func<IGraphNode> CreateFactory(Type type) => () => (IGraphNode)Activator.CreateInstance(type)!;

    /// <summary>
    /// Finds the Nodes/ directory across possible publish layouts.
    /// Returns null if not found anywhere.
    /// </summary>
    private static string? FindNodesDirectory()
    {
        // Candidates ordered by likelihood
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes"),
            Path.Combine(Path.GetDirectoryName(typeof(GraphNodeRegistry).Assembly.Location) ?? ".", "Nodes"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Nodes"),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static IReadOnlyList<NodeResource> GetCatalog() { EnsureInitialized(); return Catalog; }
    public static string GetNodesDirectory() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes");

    public static void Reload()
    {
        lock (_lock)
        {
            NodeFactories.Clear();
            Prototypes.Clear();
            Catalog.Clear();
            _initialized = false;
            LoadFromNodeFiles();
            _initialized = true;
        }
    }

    public static IGraphNode? Create(string typeName)
    {
        EnsureInitialized();
        if (NodeFactories.TryGetValue(typeName, out var factory))
            return factory();
        return NodeFactoryFallback?.Invoke(typeName);
    }

    public static IReadOnlyList<IGraphNode> GetAllPrototypes()
    {
        EnsureInitialized();
        return Prototypes;
    }

    public static IEnumerable<IGraphNode> GetByCategory(string category)
    {
        EnsureInitialized();
        return Prototypes.Where(n => string.Equals(n.Category, category, StringComparison.OrdinalIgnoreCase));
    }
}
