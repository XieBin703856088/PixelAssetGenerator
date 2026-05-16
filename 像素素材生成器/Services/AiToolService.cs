using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Aggregates multiple IToolProvider instances and exposes their combined
/// functionality as a unified tool execution + schema service.
/// </summary>
public sealed class AiToolService
{
    private readonly List<IToolProvider> _providers = new();

    /// <summary>
    /// Registers a tool provider. Order matters — first match wins on execution.
    /// </summary>
    public void RegisterProvider(IToolProvider provider)
    {
        _providers.Add(provider);
    }

    /// <summary>
    /// Executes a tool by name with JSON string arguments. Each provider's ExecuteToolAsync
    /// receives a freshly-parsed JsonElement to avoid JsonDocument lifecycle issues.
    /// </summary>
    public async Task<string> ExecuteToolAsync(string toolName, string argsJson, CancellationToken ct = default)
    {
        // 先解析 JSON，解析失败直接返回错误，不静默回退到 {}
        JsonElement parsed;
        try { parsed = JsonSerializer.Deserialize<JsonElement>(argsJson); }
        catch (JsonException ex)
        {
            return "{\"success\":false,\"error\":\"Invalid JSON arguments: " + EscapeJson(ex.Message) + "\"}";
        }

        foreach (var provider in _providers)
        {
            ToolResult result;
            try
            {
                result = await provider.ExecuteToolAsync(toolName, parsed, ct);
            }
            catch (Exception ex)
            {
                result = new ToolResult("{\"success\":false,\"error\":\"" + EscapeJson(ex.GetType().Name + ": " + ex.Message) + "\"}", true);
            }

            if (!result.IsError && !result.IsUnhandled)
                return result.Content;

            if (result.IsUnhandled)
                continue;

            return result.Content;
        }
        return "{\"success\":false,\"error\":\"" + EscapeJson("Unknown tool: " + toolName) + "\"}";
    }

    private static string EscapeJson(string s) => AiHelpers.Escape(s);

    /// <summary>
    /// Legacy overload with JsonElement — delegates to string overload.
    /// </summary>
    public Task<string> ExecuteToolAsync(string toolName, JsonElement args, CancellationToken ct = default)
    {
        return ExecuteToolAsync(toolName, args.GetRawText(), ct);
    }

    /// <summary>
    /// Aggregates tool schemas from all registered providers.
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolSchemas()
    {
        return BuildToolSchemas(_providers.SelectMany(p => p.GetToolDefinitions()));
    }

    /// <summary>
    /// 返回核心工具集（用于阶段 2 执行步骤），不包含资源/配方管理等辅助工具。
    /// 大幅减少每次请求的 token 消耗。
    /// </summary>
    public IReadOnlyList<JsonElement> GetCoreToolSchemas()
    {
        var coreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "modify_nodes", "modify_connections", "set_parameter", "query_info",
            "aesthetic_eval", "update_plan", "get_plan"
        };

        var defs = _providers
            .SelectMany(p => p.GetToolDefinitions())
            .Where(d => coreNames.Contains(d.Name));

        return BuildToolSchemas(defs);
    }

    private static IReadOnlyList<JsonElement> BuildToolSchemas(IEnumerable<ToolDefinition> defs)
    {
        var toolList = new List<object>();
        foreach (var def in defs)
        {
            toolList.Add(new
            {
                type = "function",
                function = new
                {
                    name = def.Name,
                    description = def.Description,
                    parameters = def.Parameters
                }
            });
        }
        var json = JsonSerializer.Serialize(toolList);
        var arr = JsonSerializer.Deserialize<JsonElement>(json);
        return arr.EnumerateArray().ToList();
    }
}
