using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PixelAssetGenerator.Models;

/// <summary>
/// Persistent chat message model for serialization.
/// </summary>
public sealed class ChatMessage
{
    public string Role { get; set; } = ""; // "user", "assistant", "tool", "system"
    public string? Content { get; set; }
    public List<ChatToolCall>? ToolCalls { get; set; }
    public string? ReasoningContent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class ChatToolCall
{
    public string Id { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public string? Result { get; set; }
}
