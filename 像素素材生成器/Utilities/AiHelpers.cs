using System;
using System.Collections.Generic;
using System.Text.Json;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator.Utilities;

/// <summary>
/// Shared helper methods used across AI services and tool providers.
/// </summary>
public static class AiHelpers
{
    /// <summary>
    /// Escapes a string for safe inclusion in JSON string values.
    /// Handles backslash, quote, newline, carriage return, and tab.
    /// </summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    /// <summary>
    /// Validates and normalizes a JSON string. Returns "{}" on failure.
    /// </summary>
    public static string Sanitize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "{}";
        try { return JsonSerializer.Deserialize<JsonElement>(json).GetRawText(); }
        catch { return "{}"; }
    }

    /// <summary>
    /// Checks if a JSON response indicates success.
    /// Arrays are always success; objects are success if they don't contain "error".
    /// </summary>
    public static bool IsSuccess(string result)
    {
        if (string.IsNullOrEmpty(result)) return false;
        if (result.StartsWith("[")) return true;
        if (result.StartsWith("{") && !result.Contains("\"error\"")) return true;
        return result.Contains("\"success\":true");
    }

    /// <summary>
    /// Generates a human-readable preview of a tool call's arguments.
    /// </summary>
    public static string PreviewArgs(string toolName, string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argsJson);
            return toolName switch
            {
                "modify_nodes" => PreviewModifyNodes(args),
                "modify_connections" => "Connect/Disconnect",
                "query_info" => $"Query {GetStringProperty(args, "query_type", "?")}",
                "set_parameter" => "Set param",
                "use_skill" => $"Use skill {GetStringProperty(args, "skillId", "?")}",
                "create_resource_node" => $"Create node {GetStringProperty(args, "typeName", "?")}",
                "update_plan" => $"Plan: {GetStringProperty(args, "action", "?")}",
                _ => toolName
            };
        }
        catch { return toolName; }
    }

    private static string PreviewModifyNodes(JsonElement args)
    {
        var action = GetStringProperty(args, "action", "");
        return action switch
        {
            "create" => $"Create {GetStringProperty(args, "typeName", "?")}",
            "delete" => "Delete",
            "move" => "Move",
            "select" => "Select",
            _ => action
        };
    }

    private static string GetStringProperty(JsonElement el, string name, string fallback)
    {
        return el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? fallback : fallback;
    }

    /// <summary>
    /// Builds a ToolResult indicating success.
    /// </summary>
    public static ToolResult SuccessResult(string content)
        => new(content);

    /// <summary>
    /// Builds a ToolResult indicating an error.
    /// Uses proper JSON serialization to avoid injection via error messages.
    /// </summary>
    public static ToolResult ErrorResult(string error)
    {
        var obj = new { success = false, error };
        return new ToolResult(JsonSerializer.Serialize(obj), true);
    }

    /// <summary>
    /// Escapes and wraps a value into a JSON string property value pattern.
    /// </summary>
    public static string JsonStr(string value)
        => $"\"{Escape(value)}\"";
}

/// <summary>字符串截断扩展方法，用于调试日志</summary>
public static class StringExtensions
{
    /// <summary>截断字符串到指定长度，超过则加"…"。</summary>
    public static string Truncate(this string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Length <= maxLen ? s : s[..maxLen] + "…";
    }
}
