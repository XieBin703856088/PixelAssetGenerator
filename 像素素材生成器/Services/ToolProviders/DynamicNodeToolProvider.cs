using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// IToolProvider implementation for dynamic/script node operations.
/// </summary>
public sealed class DynamicNodeToolProvider : IToolProvider
{
    private readonly DynamicNodeService _dynamicNodeService;

    public string ProviderName => "dynamic_nodes";

    public DynamicNodeToolProvider(DynamicNodeService dynamicNodeService)
    {
        _dynamicNodeService = dynamicNodeService;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "create_script_node",
            "Creates a script node driven by a C# code snippet for custom pixel processing logic.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"name":{"type":"string","description":"Node name"},"description":{"type":"string","description":"Node function description"},"code":{"type":"string","description":"C# script code. Only System, System.Math, System.Drawing namespaces allowed. No IO or network operations. Code should implement a function that accepts PixelBuffer input and returns PixelBuffer."},"parameters":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"type":{"type":"string","description":"Parameter type: number/integer/boolean/color"},"defaultValue":{"type":"number"},"description":{"type":"string"}}}}},"required":["name","description","code"]}
            """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "create_script_node" => Task.FromResult(CreateScriptNode(arguments)),
            _ => Task.FromResult(new ToolResult($"{{\"success\":false,\"error\":\"Unknown dynamic node tool: {toolName}\"}}", true) { IsUnhandled = true })
        };
    }

    private ToolResult CreateScriptNode(JsonElement args)
    {
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : "";
        var description = args.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        var code = args.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(name))
            return new ToolResult("{\"success\":false,\"error\":\"Missing name parameter\"}", true);
        if (string.IsNullOrEmpty(code))
            return new ToolResult("{\"success\":false,\"error\":\"Missing code parameter\"}", true);

        // Validate
        var parameters = new List<ScriptNodeParameter>();
        if (args.TryGetProperty("parameters", out var paramsEl))
        {
            foreach (var p in paramsEl.EnumerateArray())
            {
                parameters.Add(new ScriptNodeParameter
                {
                    Name = p.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "",
                    Type = p.TryGetProperty("type", out var pt) ? pt.GetString() ?? "number" : "number",
                    Description = p.TryGetProperty("description", out var pd) ? pd.GetString() ?? "" : ""
                });
            }
        }

        try
        {
            var def = _dynamicNodeService.CreateScriptNode(name, description, code, parameters);
            return new ToolResult($"{{\"success\":true,\"name\":\"{Escape(name)}\",\"parameters\":{parameters.Count},\"message\":\"Script node '{Escape(name)}'  has been created and is available in the node library.\"}}");
        }
        catch (InvalidOperationException ex)
        {
            return new ToolResult($"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}", true);
        }
    }

    private static string Escape(string s) => AiHelpers.Escape(s);
}
