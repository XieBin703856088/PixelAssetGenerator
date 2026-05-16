using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Manages dynamic node types and script node execution.
/// Supports three levels:
/// 1. Script nodes (C# snippets with sandboxed execution)
/// 2. Composition nodes (covered by SkillService)
/// 3. Plugin nodes (reserved for future IDynamicNodeProvider loading)
/// </summary>
public sealed class DynamicNodeService
{
    private readonly List<IDynamicNodeProvider> _providers = new();
    private readonly Dictionary<string, ScriptNodeDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an external node provider (plugin).
    /// </summary>
    public void RegisterProvider(IDynamicNodeProvider provider)
    {
        _providers.Add(provider);
    }

    public IReadOnlyList<IDynamicNodeProvider> GetProviders() => _providers;

    /// <summary>
    /// Validates a script node definition without executing it.
    /// Returns an error message if invalid, null if valid.
    /// </summary>
    public string? ValidateScriptNode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Code cannot be empty";

        // Check for blocked patterns
        var blockedPatterns = new[]
        {
            "System.IO", "System.Net", "System.Diagnostics",
            "System.Reflection", "System.Management",
            "System.Threading.Tasks", "System.Console",
            "Microsoft.Win32", "System.Runtime.InteropServices",
            "DllImport", "Process.Start", "File.",
            "Socket", "WebClient", "HttpClient"
        };

        foreach (var pattern in blockedPatterns)
        {
            if (code.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return $"Code contains disallowed namespace or API: {pattern}";
        }

        return null; // valid
    }

    /// <summary>
    /// Creates a script node definition from the given parameters
    /// and registers it so it can be instantiated in the node graph.
    /// </summary>
    public ScriptNodeDefinition CreateScriptNode(
        string name,
        string description,
        string code,
        List<ScriptNodeParameter>? parameters = null)
    {
        var validationError = ValidateScriptNode(code);
        if (validationError != null)
            throw new InvalidOperationException(validationError);

        var def = new ScriptNodeDefinition
        {
            Name = name,
            Description = description,
            Code = code,
            Parameters = parameters ?? new List<ScriptNodeParameter>()
        };

        // Register so DynamicNodeSource can find it (B4 fix)
        _definitions[name] = def;

        return def;
    }

    /// <summary>Returns all registered script definitions.</summary>
    public IReadOnlyList<ScriptNodeDefinition> GetAllDefinitions() => _definitions.Values.ToList();

    /// <summary>Gets a registered definition by name (case-insensitive).</summary>
    public ScriptNodeDefinition? GetDefinition(string name)
    {
        _definitions.TryGetValue(name, out var def);
        return def;
    }

    /// <summary>Removes a definition by name.</summary>
    public bool RemoveDefinition(string name) => _definitions.Remove(name);
}

/// <summary>
/// Definition for a script-based node created at runtime.
/// </summary>
public sealed class ScriptNodeDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Code { get; set; } = "";
    public List<ScriptNodeParameter> Parameters { get; set; } = new();
}

public sealed class ScriptNodeParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "number"; // "number", "integer", "boolean", "color"
    public object? DefaultValue { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Plugin node provider interface for future DLL-based extensions.
/// </summary>
public interface IDynamicNodeProvider
{
    string TypeName { get; }
}
