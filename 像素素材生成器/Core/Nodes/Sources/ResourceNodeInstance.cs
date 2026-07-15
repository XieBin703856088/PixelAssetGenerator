using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PixelAssetGenerator.Core.PixelArt;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Core.Nodes.Sources;

/// <summary>
/// Runtime IGraphNode wrapper that compiles and executes C# script from a .node.json resource.
/// Uses Roslyn for on-demand compilation with caching.
/// Automatically provides multi-output support: nodes with a Mask output port in their JSON
/// definition get an auto-derived grayscale mask from the primary image output.
/// </summary>
public sealed class ResourceNodeInstance : IGraphNode, IMultiOutputNode
{
    private readonly NodeResource _resource;
    private readonly string _filePath;
    private Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>? _compiledFunc;
    private bool _compilationAttempted;

    public string TypeName => _resource.Identity.TypeName;

    public string Category
    {
        get
        {
            var cat = _resource.Identity.Category;
            if (!string.IsNullOrWhiteSpace(cat))
                return cat;
            return "Utility";
        }
    }

    public IReadOnlyList<GraphNodePort> InputPorts { get; private set; } = Array.Empty<GraphNodePort>();
    public IReadOnlyList<GraphNodePort> OutputPorts { get; private set; } = Array.Empty<GraphNodePort>();
    public IReadOnlyList<NodeParameterDefinition> Parameters { get; private set; } = Array.Empty<NodeParameterDefinition>();

    /// <summary>
    /// Cached flag: does this node have a Mask output port in its definition?
    /// Used to decide whether ProcessMulti should return a mask buffer.
    /// </summary>
    private bool _hasMaskOutput;
    private bool _hasMaskChecked;

    private static string CurrentCulture
        => Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>().CurrentCulture;

    public ResourceNodeInstance(NodeResource resource, string filePath)
    {
        _resource = resource;
        _filePath = filePath;

        ApplyLocale(CurrentCulture);
    }

    /// <summary>
    /// Re-applies the current locale to ports and parameters.
    /// Called on language switch so that resource-based nodes reflect the new locale.
    /// </summary>
    public void RefreshLocale()
    {
        ApplyLocale(CurrentCulture);
    }

    private void ApplyLocale(string locale)
    {
        InputPorts = (_resource.Ports?.Inputs ?? new List<NodeResourcePortDef>())
            .Select(p => new GraphNodePort(p.GetName(locale), ParsePortType(p.Type), p.Key, p.IsRequired, p.AllowsMultipleConnections))
            .ToList();

        OutputPorts = (_resource.Ports?.Outputs ?? new List<NodeResourcePortDef>())
            .Select(p => new GraphNodePort(p.GetName(locale), ParsePortType(p.Type), p.Key, p.IsRequired, p.AllowsMultipleConnections))
            .ToList();

        Parameters = (_resource.Parameters ?? new List<NodeResourceParameter>())
            .Select(p => ParseParameter(p, locale))
            .ToList();
    }

    /// <summary>
    /// Creates a new instance from a .node.json file.
    /// </summary>
    public static ResourceNodeInstance? FromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resource = JsonSerializer.Deserialize<NodeResource>(json, options);
            if (resource?.Identity == null || string.IsNullOrWhiteSpace(resource.Identity.TypeName))
                return null;
            return new ResourceNodeInstance(resource, filePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new instance from a deserialized NodeResource.
    /// </summary>
    public static ResourceNodeInstance FromResource(NodeResource resource, string filePath)
    {
        return new ResourceNodeInstance(resource, filePath);
    }

    public PixelBuffer Process(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        EnsureCompiled();

        if (_compiledFunc != null)
        {
            var continuousResult = _compiledFunc(inputs, parameters, context);
            var style = PixelArtStyleProfile.ForLegacyNode(Category, TypeName, context.TileSize);
            if (!style.Enabled)
                return continuousResult;

            var pixelArtResult = PixelArtKernel.Stylize(continuousResult, style);
            continuousResult.Dispose();
            return pixelArtResult;
        }

        // Fallback: pass-through first input or return solid color
        return inputs.Length > 0 && inputs[0] is { } firstInput
            ? firstInput.Clone()
            : PixelBuffer.CreateSolid(context.TileSize, context.TileSize, 0, 0, 0, 255);
    }

    public PixelBuffer[] ProcessMulti(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Get the primary output first
        var primary = Process(inputs, parameters, context);

        // Check if this node has a Mask output port in its definition
        if (!_hasMaskChecked)
        {
            _hasMaskOutput = _resource.Ports?.Outputs?.Any(o =>
                string.Equals(o.Type, "Mask", StringComparison.OrdinalIgnoreCase)) == true;
            _hasMaskChecked = true;
        }

        if (!_hasMaskOutput)
            return new[] { primary };

        // Derive mask from the primary output using CreateMaskView which intelligently
        // picks between alpha channel (coverage-style, e.g. ShapeNode) and luminance
        // (pattern-style, e.g. BrickNode) — whichever best represents the shape silhouette.
        var mask = PixelBuffer.CreateMaskView(primary);
        return new[] { primary, mask };
    }

    private void EnsureCompiled()
    {
        if (_compilationAttempted) return;
        _compilationAttempted = true;

        var code = _resource.Script?.Code;
        if (string.IsNullOrWhiteSpace(code))
            return;

        try
        {
            _compiledFunc = CompileScript(code, TypeName);
            if (_compiledFunc == null)
            {
                // Compilation reported errors — log via Debug output
                System.Diagnostics.Debug.WriteLine($"[ResourceNodeInstance] CompileScript returned null for '{TypeName}', using fallback");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResourceNodeInstance] CompileScript threw for '{TypeName}': {ex.Message}");
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>?> ScriptCache = new();

    /// <summary>Directory for caching compiled script assemblies to disk.</summary>
    private static readonly string ScriptCacheDir;

    static ResourceNodeInstance()
    {
        ScriptCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelAssetGenerator", "ScriptCache");
        try { Directory.CreateDirectory(ScriptCacheDir); } catch { }
    }

    /// <summary>Returns the path to the script cache directory.</summary>
    public static string GetScriptCacheDir() => ScriptCacheDir;

    /// <summary>
    /// Pre-compiles the script without executing it. Call during startup to warm the cache.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public void WarmCompile()
    {
        EnsureCompiled();
    }

    private static Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>? CompileScript(string code, string typeName = "Unknown")
    {
        // Use a stable content-hash based cache key so the same code always maps to the same file
        var codeHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(code)));
        var cacheKey = typeName + "::" + codeHash;
        if (ScriptCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Try disk cache first
        var cachedAssemblyPath = Path.Combine(ScriptCacheDir, codeHash + ".dll");
        if (File.Exists(cachedAssemblyPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(cachedAssemblyPath);
                var asm = Assembly.Load(bytes);
                var compiledType = asm.GetType("CompiledNode");
                var executeMethod = compiledType?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                if (executeMethod != null)
                {
                    var fn = (Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>)
                        Delegate.CreateDelegate(typeof(Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>), executeMethod);
                    ScriptCache[cacheKey] = fn;
                    System.Diagnostics.Debug.WriteLine($"[ResourceNodeInstance] Loaded cached assembly for '{typeName}'");
                    return fn;
                }
            }
            catch
            {
                // Corrupted cache — fall through to recompile
                try { File.Delete(cachedAssemblyPath); } catch { }
            }
        }

        // Split script into inline body (Execute内) and class-level members (private/static methods etc.)
        var (inlineCode, classMemberCode) = SplitScriptCode(code);

        var wrapper = $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PixelAssetGenerator;
using PixelAssetGenerator.Core;
using static PixelAssetGenerator.Core.GraphNodeBase;

public sealed class CompiledNode
{
    public static PixelBuffer Execute(PixelBuffer?[] inputs, IReadOnlyDictionary<string, object> parameters, PixelGraphContext context)
    {
        // Short helpers forward to GraphNodeBase public helpers
        float F(string n, float fb = 0f) => GetFloat(parameters, n, fb);
        int I(string n, int fb = 0) => GetInt(parameters, n, fb);
        bool B(string n, bool fb = false) => GetBool(parameters, n, fb);
        string S(string n, string fb = "") => GetChoice(parameters, n, fb);

        // Generic multi-output passthrough: clones each input. Used by stub scripts.
        PixelBuffer[] ProcessMulti(PixelBuffer?[] ins, IReadOnlyDictionary<string, object> par, PixelGraphContext ctx)
        {
            var results = new PixelBuffer[Math.Max(1, ins.Length)];
            for (var i = 0; i < results.Length; i++)
                results[i] = ins[i]?.Clone() ?? PixelBuffer.CreateSolid(ctx.TileSize, ctx.TileSize, 0, 0, 0, 255);
            return results;
        }

        {{inlineCode}}
    }

    {{classMemberCode}}
}
""";

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(wrapper, parseOptions);

        // Load required assemblies for script compilation
        var referencedAssemblies = new HashSet<string>();
        void AddAssembly(Assembly asm)
        {
            if (asm == null || asm.IsDynamic) return;
            try
            {
                var loc = asm.Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    referencedAssemblies.Add(loc);
            }
            catch { }
        }

        // Add the main assembly (exe) which contains all node types, PixelBuffer, etc.
        AddAssembly(typeof(PixelBuffer).Assembly);

        // Core framework assemblies
        AddAssembly(typeof(object).Assembly);               // mscorlib/System.Private.CoreLib
        AddAssembly(typeof(Enumerable).Assembly);           // System.Linq
        AddAssembly(typeof(System.Collections.Generic.List<>).Assembly); // System.Collections
        AddAssembly(typeof(System.Windows.Media.Color).Assembly); // PresentationCore (WPF)
        AddAssembly(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly);

        // Try to load by name for framework assemblies that might not resolve via type
        var coreNames = new[] { "System.Runtime", "System.Collections", "System.Linq",
                                "System.Console", "System.Threading", "System.Memory",
                                "System.Text.Json", "netstandard" };
        foreach (var name in coreNames)
        {
            try { AddAssembly(Assembly.Load(name)); } catch { }
        }

        // All referenced assemblies of the main assembly (covers transitive deps)
        try
        {
            foreach (var refAsm in typeof(PixelBuffer).Assembly.GetReferencedAssemblies())
            {
                try { AddAssembly(Assembly.Load(refAsm)); } catch { }
            }
        }
        catch { }

        // Also include System.Runtime via explicit path if not already found
        if (!referencedAssemblies.Any(p => p.Contains("System.Runtime")))
        {
            try
            {
                var rtPath = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location) ?? ".",
                    "System.Runtime.dll");
                if (File.Exists(rtPath))
                    referencedAssemblies.Add(rtPath);
            }
            catch { }
        }

        var references = referencedAssemblies
            .Where(loc => !string.IsNullOrEmpty(loc) && File.Exists(loc))
            .Distinct()
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        // Debug: log assembly count and names
        System.Diagnostics.Debug.WriteLine($"[ResourceNodeInstance] Compiled with {references.Length} assembly references");

        var compilation = CSharpCompilation.Create(
            "ResourceNodeScript",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            System.Diagnostics.Debug.WriteLine($"[ResourceNodeInstance] Compilation failed: {errors}");

            // Write error log to file for debugging
            try
            {
                var logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "PixelAssetGenerator_compile_errors.txt");
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss}] ScriptNode [{typeName}]: {errors}\n");

                // Also dump the generated wrapper to a separate file for debugging
                var dumpPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"PixelAssetGenerator_wrapper_{typeName.Replace(".", "_")}.cs");
                System.IO.File.WriteAllText(dumpPath, wrapper);
            }
            catch { }

            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assemblyBytes = ms.ToArray();

        // Save to disk cache for next startup
        try
        {
            File.WriteAllBytes(cachedAssemblyPath, assemblyBytes);
        }
        catch { }

        var assembly = Assembly.Load(assemblyBytes);
        var type = assembly.GetType("CompiledNode");
        var method = type?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);

        if (method != null)
        {
            var fn = (Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>)
                Delegate.CreateDelegate(typeof(Func<PixelBuffer?[], IReadOnlyDictionary<string, object>, PixelGraphContext, PixelBuffer>), method);
            ScriptCache[cacheKey] = fn;
            return fn;
        }

        ScriptCache[cacheKey] = null;
        return null;
    }

    private static GraphPortType ParsePortType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "image" => GraphPortType.Image,
            "mask" => GraphPortType.Mask,
            "float" => GraphPortType.Float,
            "color" or "colour" => GraphPortType.Color,
            "any" => GraphPortType.Any,
            _ => GraphPortType.Image
        };
    }

    /// <summary>
    /// Derives a canonical parameter key from the English name (e.g., "Blur Type" → "blurType").
    /// Falls back to the Chinese name if no English name is available.
    /// </summary>
    private static string DeriveParameterKey(NodeLocText name)
    {
        var enName = name.Get("en");
        if (!string.IsNullOrEmpty(enName) && enName != name.Get("zh-Hans"))
        {
            // CamelCase the English name: "Blur Type" → "blurType"
            var parts = enName.Split(' ', '-', '_');
            var result = char.ToLowerInvariant(parts[0][0]) + parts[0].Substring(1);
            for (var i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    result += char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return result;
        }
        // Fallback to Chinese name
        return name.Get("zh-Hans");
    }

    private static NodeParameterDefinition ParseParameter(NodeResourceParameter p, string? locale = null)
    {
        locale ??= CurrentCulture;
        var displayName = p.GetName(locale);
        var key = DeriveParameterKey(p.Name);
        var kind = p.Kind?.ToLowerInvariant() ?? "number";

        return kind switch
        {
            "integer" => NodeParameterDefinition.Integer(key, GetDefaultInt(p), (int)(p.Min ?? 0), (int)(p.Max ?? 100), 1, displayName),
            "boolean" or "bool" => NodeParameterDefinition.Boolean(key, p.Default?.ValueKind == JsonValueKind.True, displayName),
            "choice" => NodeParameterDefinition.Choice(key, GetDefaultChoice(p),
                p.Choices?.Select(c => c.Value).ToList() ?? new List<string>(),
                p.Choices?.Select(c => c.GetLabel(locale)).ToList() ?? new List<string>(),
                displayName),
            "color" => NodeParameterDefinition.Color(key, GetDefaultColor(p), displayName),
            "seed" => NodeParameterDefinition.Seed(key, GetDefaultInt(p), (int)(p.Min ?? 0), (int)(p.Max ?? 9999), displayName),
            "text" => NodeParameterDefinition.Text(key, GetDefaultString(p), displayName),
            "pointlist" => NodeParameterDefinition.PointList(key, displayName),
            _ => NodeParameterDefinition.Number(key, GetDefaultDouble(p), p.Min ?? 0, p.Max ?? 1, 0.01, displayName)
        };
    }

    private static double GetDefaultDouble(NodeResourceParameter p)
    {
        if (p.Default?.ValueKind == JsonValueKind.Number)
            return p.Default.Value.GetDouble();
        return 0;
    }

    private static int GetDefaultInt(NodeResourceParameter p)
    {
        if (p.Default?.ValueKind == JsonValueKind.Number)
            return p.Default.Value.GetInt32();
        return 0;
    }

    private static string GetDefaultChoice(NodeResourceParameter p)
    {
        if (p.Default?.ValueKind == JsonValueKind.String)
            return p.Default.Value.GetString() ?? "";
        return p.Choices?.FirstOrDefault()?.Value ?? "";
    }

    private static System.Windows.Media.Color GetDefaultColor(NodeResourceParameter p)
    {
        if (p.Default?.ValueKind == JsonValueKind.String)
        {
            var str = p.Default.Value.GetString() ?? "";
            if (str.Length >= 8 && str.StartsWith("#"))
            {
                try
                {
                    var r = Convert.ToByte(str.Substring(1, 2), 16);
                    var g = Convert.ToByte(str.Substring(3, 2), 16);
                    var b = Convert.ToByte(str.Substring(5, 2), 16);
                    return System.Windows.Media.Color.FromRgb(r, g, b);
                }
                catch { }
            }
        }
        return System.Windows.Media.Colors.White;
    }

    private static string GetDefaultString(NodeResourceParameter p)
    {
        if (p.Default?.ValueKind == JsonValueKind.String)
            return p.Default.Value.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// Splits script code into two parts:
    /// - inlineCode: statements placed inside Execute() method body
    /// - classMemberCode: method/field declarations placed at class level (outside Execute)
    ///
    /// Scripts may be wrapped in outer braces { ... } or not.
    /// Member declarations (private/public/protected/internal/static) at the top level of the
    /// script (depth=1 if wrapped, depth=0 if not) are extracted as class members.
    /// </summary>
    private static (string inlineCode, string classMemberCode) SplitScriptCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (code, "");

        var trimmedCode = code.Trim();

        // Detect if the script is wrapped in outer braces { ... }
        // If so, strip the outer braces to get the real content depth-0
        string workingCode;
        if (trimmedCode.StartsWith("{") && trimmedCode.EndsWith("}"))
        {
            // Remove outer braces
            workingCode = trimmedCode.Substring(1, trimmedCode.Length - 2);
        }
        else
        {
            workingCode = trimmedCode;
        }

        var lines = workingCode.Split('\n');
        var inlineLines = new System.Text.StringBuilder();
        var memberLines = new System.Text.StringBuilder();

        int depth = 0;
        bool inMember = false;
        bool memberBodyStarted = false; // tracks whether we have seen the opening '{' of the member

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();

            // At depth=0 (top level of script), detect class-level member declarations
            if (!inMember && depth == 0 && IsClassMemberDeclaration(trimmed))
            {
                inMember = true;
                memberBodyStarted = false;
            }

            if (inMember)
                memberLines.AppendLine(rawLine);
            else
                inlineLines.AppendLine(rawLine);

            // Update brace depth after deciding where to put the line
            foreach (var ch in rawLine)
            {
                if (ch == '{') { depth++; memberBodyStarted = true; }
                else if (ch == '}') depth--;
            }

            // Only reset inMember once the member body has been entered AND fully closed
            if (inMember && memberBodyStarted && depth <= 0)
            {
                depth = 0;
                inMember = false;
                memberBodyStarted = false;
            }
        }

        return (inlineLines.ToString().TrimEnd(), memberLines.ToString().TrimEnd());
    }

    private static readonly System.Text.RegularExpressions.Regex _memberDeclRegex =
        new(@"^(private|public|protected|internal|static)\s+",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsClassMemberDeclaration(string trimmed)
    {
        if (string.IsNullOrWhiteSpace(trimmed)) return false;
        // Must start with an access/static modifier AND contain a method signature (parentheses)
        // or be a field declaration. Exclude local variable declarations that happen to start with these words.
        if (!_memberDeclRegex.IsMatch(trimmed)) return false;
        // Heuristic: if the line (or it's a multi-line signature) contains '(' it's likely a method
        // If no '(' it could be a field - still treat as member
        // Exclude: lines that are just "static" local variable declarations like "static var x = ..."
        // Key check: must NOT be a local function (those are fine inside Execute as local funcs)
        // We identify class methods by: modifier + return type + name + ( pattern
        // Simple approach: if it matches "modifier ... ( ... )" with a { somewhere, it's a method
        return trimmed.Contains('(') || trimmed.Contains(';');
    }
}
