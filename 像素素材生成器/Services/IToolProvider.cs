using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Result from executing a tool call.
/// </summary>
public sealed record ToolResult(string Content, bool IsError = false)
{
    /// <summary>
    /// 创建一个"此 provider 不认识此工具"的结果，让 AiToolService 继续尝试其他 provider。
    /// 与 IsError=true 的区别：IsUnhandled 表示不处理，IsError 表示处理了但出错。
    /// </summary>
    public bool IsUnhandled { get; init; }
}

/// <summary>
/// Tool schema definition for AI model consumption.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

/// <summary>
/// Interface for modular tool providers. Each provider contributes a set of
/// tools that can be discovered and executed by AiToolService.
/// </summary>
public interface IToolProvider
{
    string ProviderName { get; }
    IEnumerable<ToolDefinition> GetToolDefinitions();
    Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default);
}
