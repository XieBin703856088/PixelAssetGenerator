using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PixelAssetGenerator.Core.Nodes.Sources;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// IToolProvider that exposes AI tools for managing resource nodes:
/// create, delete, and read resource node definitions.
/// </summary>
public sealed class ResourceNodeToolProvider : IToolProvider
{
    private readonly NodeResourceRegistry _registry;
    private readonly FileNodeSource? _fileSource;
    private readonly DynamicNodeService? _dynamicNodeService;

    public string ProviderName => "resource_nodes";

    public ResourceNodeToolProvider(
        NodeResourceRegistry registry,
        FileNodeSource? fileSource = null,
        DynamicNodeService? dynamicNodeService = null)
    {
        _registry = registry;
        _fileSource = fileSource;
        _dynamicNodeService = dynamicNodeService;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "create_resource_node",
            "Creates a new resource node. Generates a .node.json file in the Custom/ directory and registers it, ready for immediate use by both AI and users.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"typeName":{"type":"string","description":"Unique type identifier (English PascalCase), e.g. RedFilter"},"displayName":{"type":"string","description":"Display name in Chinese, e.g. 红色滤镜"},"displayNameEn":{"type":"string","description":"Display name in English, e.g. Red Filter"},"category":{"type":"string","enum":["Source","Nature","Material","Structure","Adjust","Effect","Utility","Custom"],"description":"Node category"},"description":{"type":"string","description":"Node function description in Chinese"},"descriptionEn":{"type":"string","description":"Node function description in English"},"inputPorts":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","description":"Port name in Chinese, e.g. 输入图像"},"nameEn":{"type":"string","description":"Port name in English, e.g. Input Image"},"type":{"type":"string","enum":["Image","Mask","Float","Color","Any"]}},"required":["name","type"]},"description":"Input port list"},"outputPorts":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","description":"Port name in Chinese, e.g. 输出结果"},"nameEn":{"type":"string","description":"Port name in English, e.g. Output Result"},"type":{"type":"string","enum":["Image","Mask","Float","Color","Any"]}},"required":["name","type"]},"description":"Output port list"},"parameters":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","description":"Parameter name in Chinese, e.g. 强度"},"nameEn":{"type":"string","description":"Parameter name in English, e.g. Strength"},"kind":{"type":"string","enum":["Number","Integer","Boolean","Choice","Color","Seed","Text"]},"default":{"type":"number"},"min":{"type":"number"},"max":{"type":"number"},"choices":{"type":"array","items":{"type":"string"}}},"description":"Parameter definition list"}},"code":{"type":"string","description":"C# processing code. Available helpers: F(name,fallback) GetFloat, I(name,fallback) GetInt, B(name,fallback) GetBool, S(name,fallback) GetChoice. Also GraphNodeBase static methods: SmoothStep(), Lerp(), Mod(), HashToUnit(), TileableFractalNoise(), TileableVoronoi(), PerturbUv(). Variables: inputs(PixelBuffer?[]), parameters(IReadOnlyDictionary<string,object>), context(PixelGraphContext). Must return PixelBuffer."}},"required":["typeName","displayName","code"]}
            """)
        );

        yield return new ToolDefinition(
            "delete_resource_node",
            "Deletes a resource node. Removes the .node.json file from disk and unregisters from the node library. Cannot delete built-in nodes.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"typeName":{"type":"string","description":"Node type name to delete"},"confirm":{"type":"boolean","description":"Confirm deletion, must be true"}},"required":["typeName","confirm"]}
            """)
        );

        yield return new ToolDefinition(
            "get_node_resource",
            "Gets the complete resource definition of a node (JSON format), including identity, ports, parameters and script code. Can be used by AI to read and then modify node configuration.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"typeName":{"type":"string","description":"node type name"}},"required":["typeName"]}
            """)
        );

        yield return new ToolDefinition(
            "update_resource_node",
            "Updates an existing resource node. Modifies fields in the .node.json file, node is immediately available. First use get_node_resource to get current content, then modify and submit.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"typeName":{"type":"string","description":"Node type name to update"},"resourceJson":{"type":"string","description":"Complete .node.json content (JSON string) with all modified fields."}},"required":["typeName","resourceJson"]}
            """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "create_resource_node" => Task.FromResult(CreateResourceNode(arguments)),
            "delete_resource_node" => Task.FromResult(DeleteResourceNode(arguments)),
            "get_node_resource" => Task.FromResult(GetNodeResource(arguments)),
            "update_resource_node" => Task.FromResult(UpdateResourceNode(arguments)),
            _ => Task.FromResult(new ToolResult($"{{\"success\":false,\"error\":\"Unknown resource node tool: {toolName}\"}}", true) { IsUnhandled = true })
        };
    }

    private ToolResult CreateResourceNode(JsonElement args)
    {
        if (_fileSource == null)
            return new ToolResult("{\"success\":false,\"error\":\"File node source not initialized\"}", true);

        var typeName = args.TryGetProperty("typeName", out var tn) ? tn.GetString() ?? "" : "";
        var displayName = args.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? typeName : typeName;
        var displayNameEn = args.TryGetProperty("displayNameEn", out var dne) ? dne.GetString() ?? typeName : typeName;
        var category = args.TryGetProperty("category", out var cat) ? cat.GetString() ?? "Custom" : "Custom";
        var description = args.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
        var descriptionEn = args.TryGetProperty("descriptionEn", out var desce) ? desce.GetString() ?? "" : "";
        var code = args.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(typeName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing typeName parameter\"}", true);
        if (string.IsNullOrWhiteSpace(code))
            return new ToolResult("{\"success\":false,\"error\":\"Missing code parameter\"}", true);

        // Check for duplicates
        if (_registry.GetSource(typeName) != null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Type name '{Escape(typeName)}'' already exists.\"}}", true);

        // Validate code
        if (_dynamicNodeService != null)
        {
            var validationError = _dynamicNodeService.ValidateScriptNode(code);
            if (validationError != null)
                return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(validationError)}\"}}", true);
        }

        // Build port lists with multilingual names
        var inputs = new List<NodeResourcePortDef>();
        if (args.TryGetProperty("inputPorts", out var inputsEl))
        {
            foreach (var p in inputsEl.EnumerateArray())
            {
                var portName = p.TryGetProperty("name", out var pn) ? pn.GetString() ?? "输入" : "输入";
                var portNameEn = p.TryGetProperty("nameEn", out var pne) ? pne.GetString() ?? "Input" : "Input";
                var portType = p.TryGetProperty("type", out var pt) ? pt.GetString() ?? "Image" : "Image";
                inputs.Add(new NodeResourcePortDef
                {
                    Name = new NodeLocText { { "zh-Hans", portName }, { "en", portNameEn } },
                    Type = portType
                });
            }
        }

        var outputs = new List<NodeResourcePortDef>
        {
            new() { Name = new NodeLocText { { "zh-Hans", "输出" }, { "en", "Output" } }, Type = "Image" }
        };
        if (args.TryGetProperty("outputPorts", out var outputsEl))
        {
            outputs.Clear();
            foreach (var p in outputsEl.EnumerateArray())
            {
                var outPortName = p.TryGetProperty("name", out var pn2) ? pn2.GetString() ?? "输出" : "输出";
                var outPortNameEn = p.TryGetProperty("nameEn", out var pne2) ? pne2.GetString() ?? "Output" : "Output";
                var outPortType = p.TryGetProperty("type", out var pt2) ? pt2.GetString() ?? "Image" : "Image";
                outputs.Add(new NodeResourcePortDef
                {
                    Name = new NodeLocText { { "zh-Hans", outPortName }, { "en", outPortNameEn } },
                    Type = outPortType
                });
            }
        }

        // Build parameters with multilingual names
        var parameters = new List<NodeResourceParameter>();
        if (args.TryGetProperty("parameters", out var paramsEl))
        {
            foreach (var p in paramsEl.EnumerateArray())
            {
                var paramName = p.TryGetProperty("name", out var pn3) ? pn3.GetString() ?? "" : "";
                var paramNameEn = p.TryGetProperty("nameEn", out var pne3) ? pne3.GetString() ?? paramName : paramName;
                var paramKind = p.TryGetProperty("kind", out var pk) ? pk.GetString() ?? "Number" : "Number";
                var param = new NodeResourceParameter
                {
                    Name = new NodeLocText { { "zh-Hans", paramName }, { "en", paramNameEn } },
                    Kind = paramKind,
                };
                if (p.TryGetProperty("default", out var pd)) param.Default = pd;
                if (p.TryGetProperty("min", out var pmin)) param.Min = pmin.GetDouble();
                if (p.TryGetProperty("max", out var pmax)) param.Max = pmax.GetDouble();
                if (p.TryGetProperty("choices", out var pchoices))
                {
                    param.Choices = pchoices.EnumerateArray()
                        .Select(e => new Models.NodeResourceChoice { Value = e.GetString() ?? "" })
                        .ToList();
                }
                parameters.Add(param);
            }
        }

        var resource = new NodeResource
        {
            FormatVersion = 2,
            ProcessorType = null,
            Identity = new NodeResourceIdentity
            {
                TypeName = typeName,
                DisplayName = new NodeLocText { { "zh-Hans", displayName }, { "en", displayNameEn } },
                Category = category,
                Description = new NodeLocText { { "zh-Hans", description }, { "en", descriptionEn } },
                Author = "AI",
                Version = "1.0"
            },
            Ports = new NodeResourcePorts { Inputs = inputs, Outputs = outputs },
            Parameters = parameters,
            Script = new NodeResourceScript { Language = "csharp", Code = code }
        };

        try
        {
            // Write to the Custom/ subdirectory of the nodes folder
            var customDir = Path.Combine(Path.GetDirectoryName(_fileSource.DirectoryPath) ?? _fileSource.DirectoryPath, "Custom");
            Directory.CreateDirectory(customDir);
            var filePath = Path.Combine(customDir, typeName + ".node.json");
            var json = JsonSerializer.Serialize(resource, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            _fileSource.Refresh();
            _registry.RefreshAll();
            return new ToolResult($"{{\"success\":true,\"typeName\":\"{Escape(typeName)}\",\"message\":\"自定义节点 '{Escape(displayName)}' 已创建，立即可用。\"}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult DeleteResourceNode(JsonElement args)
    {
        var typeName = args.TryGetProperty("typeName", out var tn) ? tn.GetString() ?? "" : "";
        var confirm = args.TryGetProperty("confirm", out var cv) && cv.ValueKind == JsonValueKind.True && cv.GetBoolean();

        if (string.IsNullOrWhiteSpace(typeName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing typeName parameter\"}", true);
        if (!confirm)
            return new ToolResult("{\"success\":false,\"error\":\"Requires confirm=true\"}", true);

        try
        {
            if (!_registry.Delete(typeName))
                return new ToolResult($"{{\"success\":false,\"error\":\"Delete failed\"}}", true);

            return new ToolResult($"{{\"success\":true,\"message\":\"Node '{Escape(typeName)}'' has been deleted.\"}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult GetNodeResource(JsonElement args)
    {
        var typeName = args.TryGetProperty("typeName", out var tn) ? tn.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(typeName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing typeName parameter\"}", true);

        var resource = _registry.LoadResource(typeName);
        if (resource == null)
        {
            // Check if it's a built-in node
            var source = _registry.GetSource(typeName);
            if (source?.SourceKind == NodeResourceSourceKind.BuiltIn)
                return new ToolResult($"{{\"success\":false,\"error\":\"'{Escape(typeName)}' is a built-in node，has no editable resource file\"}}", true);

            return new ToolResult($"{{\"success\":false,\"error\":\"Node not found: '{Escape(typeName)}'\"}}", true);
        }

        try
        {
            var json = JsonSerializer.Serialize(resource, new JsonSerializerOptions { WriteIndented = true });
            return new ToolResult($"{{\"success\":true,\"resource\":{json}}}");
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"Serialization failed: {Escape(ex.Message)}\"}}", true);
        }
    }

    private ToolResult UpdateResourceNode(JsonElement args)
    {
        if (_fileSource == null)
            return new ToolResult("{\"success\":false,\"error\":\"File node source not initialized\"}", true);

        var typeName = args.TryGetProperty("typeName", out var tn) ? tn.GetString() ?? "" : "";
        var resourceJson = args.TryGetProperty("resourceJson", out var rj) ? rj.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(typeName))
            return new ToolResult("{\"success\":false,\"error\":\"Missing typeName parameter\"}", true);
        if (string.IsNullOrWhiteSpace(resourceJson))
            return new ToolResult("{\"success\":false,\"error\":\"Missing resourceJson parameter\"}", true);

        // Verify the node exists and is a file node
        var source = _registry.GetSource(typeName);
        if (source == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Node not found: '{Escape(typeName)}'\"}}", true);
        if (source.SourceKind != NodeResourceSourceKind.File)
            return new ToolResult($"{{\"success\":false,\"error\":\"'{Escape(typeName)}' is not an editable resource node.\"}}", true);

        try
        {
            var updated = JsonSerializer.Deserialize<NodeResource>(resourceJson);
            if (updated?.Identity == null || string.IsNullOrWhiteSpace(updated.Identity.TypeName))
                return new ToolResult("{\"success\":false,\"error\":\"Invalid resource JSON\"}", true);

            _fileSource.WriteResource(updated);
            _registry.RefreshAll();

            return new ToolResult($"{{\"success\":true,\"typeName\":\"{Escape(updated.Identity.TypeName)}\",\"message\":\"Node '{Escape(updated.Identity.DisplayName.Get("zh-Hans"))}'' has been updated.\"}}");
        }
        catch (JsonException ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"JSON parse error: {Escape(ex.Message)}\"}}", true);
        }
        catch (Exception ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private static string Escape(string s) => AiHelpers.Escape(s);
}
