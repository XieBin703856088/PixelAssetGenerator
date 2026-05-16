using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Multi-strategy output parser for extracting tool calls from AI responses.
/// Handles: &lt;tool&gt; tags, JSON code blocks, and inline JSON objects.
///
/// NOTE: This parser exists for compatibility with legacy models that do not
/// support native tool_calls. All current models (GPT-4o, DeepSeek, Claude)
/// support native tool_calls, so this code is no longer actively used.
/// It is retained for reference and for potential local model fallback.
/// </summary>
[Obsolete("All supported models now use native tool_calls. This parser is retained only for local model fallback.")]
public static class OutputParser
{
    /// <summary>Extracts tool calls from assistant output using multiple strategies.</summary>
    public static List<ParsedToolCall>? ExtractToolCalls(string assistantOutput)
    {
        if (string.IsNullOrWhiteSpace(assistantOutput))
            return null;

        // Strategy 1: <tool>name</tool> tags with JSON parameters
        var tagResult = ExtractFromToolTags(assistantOutput);
        if (tagResult != null && tagResult.Count > 0)
            return tagResult;

        // Strategy 2: JSON code block with tool call format
        var jsonResult = ExtractFromJsonBlock(assistantOutput);
        if (jsonResult != null && jsonResult.Count > 0)
            return jsonResult;

        // Strategy 3: Inline JSON object
        var inlineResult = ExtractInlineJson(assistantOutput);
        if (inlineResult != null && inlineResult.Count > 0)
            return inlineResult;

        // Strategy 4: Function-call syntax like modify_nodes(action="create", typeName="PatternGrid", ...)
        var funcResult = ExtractFromFunctionCall(assistantOutput);
        if (funcResult != null && funcResult.Count > 0)
            return funcResult;

        return null;
    }

    /// <summary>
    /// Extracts tool name and parameters from &lt;tool&gt;...&lt;/tool&gt; tags.
    /// Format: <tool>toolName</tool>\n{ "param": "value" }
    /// </summary>
    private static List<ParsedToolCall>? ExtractFromToolTags(string text)
    {
        var match = Regex.Match(text, @"<tool>\s*(\w+)\s*</tool>", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var toolName = match.Groups[1].Value;

        // Find JSON after the tag - try code block first, then inline
        var remaining = text.Substring(match.Index + match.Length);

        // Try JSON code block
        var jsonBlock = ExtractJsonFromCodeBlock(remaining);
        if (jsonBlock != null)
        {
            var fixResult = FixAndValidateJson(jsonBlock);
            return new List<ParsedToolCall>
            {
                new() { Name = toolName, Arguments = fixResult.FixedJson ?? jsonBlock }
            };
        }

        // Try inline JSON object
        var inlineJson = ExtractFirstJsonObject(remaining);
        if (inlineJson != null)
        {
            var fixResult = FixAndValidateJson(inlineJson);
            return new List<ParsedToolCall>
            {
                new() { Name = toolName, Arguments = fixResult.FixedJson ?? inlineJson }
            };
        }

        // Return tool name with empty args
        return new List<ParsedToolCall>
        {
            new() { Name = toolName, Arguments = "{}" }
        };
    }

    /// <summary>Extracts tool call from a JSON code block (```json ... ```).</summary>
    private static List<ParsedToolCall>? ExtractFromJsonBlock(string text)
    {
        var jsonBlock = ExtractJsonFromCodeBlock(text);
        if (jsonBlock == null) return null;

        var fixResult = FixAndValidateJson(jsonBlock);
        var cleanJson = fixResult.FixedJson ?? jsonBlock;

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // Format: { "tool": "name", "parameters": { ... } }
            if (root.TryGetProperty("tool", out var toolEl) || root.TryGetProperty("name", out toolEl))
            {
                var name = toolEl.GetString() ?? "";
                var args = root.TryGetProperty("parameters", out var paramsEl)
                    ? paramsEl.GetRawText()
                    : root.TryGetProperty("arguments", out var argsEl)
                        ? argsEl.GetRawText()
                        : "{}";
                return new List<ParsedToolCall> { new() { Name = name, Arguments = args } };
            }

            // Format: [ { "name": "...", "arguments": { ... } }, ... ]
            if (root.ValueKind == JsonValueKind.Array)
            {
                var calls = new List<ParsedToolCall>();
                foreach (var item in root.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? ""
                        : item.TryGetProperty("tool", out var t) ? t.GetString() ?? "" : "";
                    var args = item.TryGetProperty("arguments", out var a) ? a.GetRawText()
                        : item.TryGetProperty("parameters", out var p) ? p.GetRawText() : "{}";
                    calls.Add(new() { Name = name, Arguments = args });
                }
                return calls.Count > 0 ? calls : null;
            }
        }
        catch
        {
            // Failed to parse as JSON
        }

        return null;
    }

    /// <summary>Extracts inline JSON objects from text (last resort).</summary>
    private static List<ParsedToolCall>? ExtractInlineJson(string text)
    {
        var jsonStr = ExtractFirstJsonObject(text);
        if (jsonStr == null) return null;

        var fixResult = FixAndValidateJson(jsonStr);
        var cleanJson = fixResult.FixedJson ?? jsonStr;

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // Try to infer tool name from context - look for preceding word
            var toolName = "";
            var jsonIndex = text.IndexOf(jsonStr!, StringComparison.Ordinal);
            if (jsonIndex > 0)
            {
                var before = text[..jsonIndex].TrimEnd();
                var lastWord = before.Split(' ', '\n', '\r', '\t').LastOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(lastWord) && !lastWord.Contains('{') && !lastWord.Contains('}'))
                    toolName = lastWord;
            }

            if (root.TryGetProperty("action", out var actionEl))
            {
                // { "action": "create", "nodes": [...] }
                toolName = actionEl.GetString() ?? "";
                return new List<ParsedToolCall> { new() { Name = $"modify_{toolName}", Arguments = cleanJson } };
            }
            if (root.TryGetProperty("tool", out var tEl) || root.TryGetProperty("name", out tEl))
            {
                toolName = tEl.GetString() ?? "";
                return new List<ParsedToolCall> { new() { Name = toolName, Arguments = cleanJson } };
            }

            return new List<ParsedToolCall> { new() { Name = toolName, Arguments = cleanJson } };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts the first JSON code block from text (```json ... ```).</summary>
    private static string? ExtractJsonFromCodeBlock(string text)
    {
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var json = match.Groups[1].Value.Trim();
            if (json.Length > 0) return json;
        }
        return null;
    }

    /// <summary>Extracts the first JSON object or array from text.</summary>
    private static string? ExtractFirstJsonObject(string text)
    {
        // Try to find { ... } or [ ... ]
        int start = -1;
        char openChar = '{';
        char closeChar = '}';

        var braceStart = text.IndexOf('{');
        var bracketStart = text.IndexOf('[');

        if (braceStart >= 0 && (bracketStart < 0 || braceStart < bracketStart))
        {
            start = braceStart;
            openChar = '{';
            closeChar = '}';
        }
        else if (bracketStart >= 0)
        {
            start = bracketStart;
            openChar = '[';
            closeChar = ']';
        }

        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        char escape = '\\';
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == escape) { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == openChar) depth++;
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }
        return null;
    }

    /// <summary>
    /// Attempts to fix common JSON issues in small-model output and validates the result.
    /// </summary>
    public static (string? FixedJson, List<string> Errors) FixAndValidateJson(string json)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add("Input is null");
            return (null, errors);
        }

        var fixedJson = json.Trim();

        // Fix 1: Missing quotes around property names
        fixedJson = Regex.Replace(fixedJson, @"(\w+)(?=\s*:)", "\"$1\"");

        // Fix 2: Trailing commas before ] or }
        fixedJson = Regex.Replace(fixedJson, @",\s*([}\]])", "$1");

        // Fix 3: Single quotes instead of double quotes
        fixedJson = fixedJson.Replace('\'', '"');

        // Fix 4: Remove comments (// ...)
        fixedJson = Regex.Replace(fixedJson, @"//[^\n\r]*", "");

        try
        {
            JsonDocument.Parse(fixedJson);
            return (fixedJson, errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"Fixed JSON still invalid: {ex.Message}");
            return (null, errors);
        }
    }

    /// <summary>
    /// Strategy 4: 解析函数调用语法，如:
    ///   modify_nodes(action="create", typeName="PatternGrid", x=50, y=50)
    ///   query_info(query_type="graph_summary")
    ///   query_info{query_type:"graph_summary"}  — 花括号格式
    ///   modify_nodes{action:"create", typeName:"PatternGrid"}
    /// </summary>
    private static List<ParsedToolCall>? ExtractFromFunctionCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 尝试匹配已知工具名 + 括号/花括号内的参数
        foreach (var toolName in KnownTools)
        {
            // Pattern 1: funcName(param=value, ...)  — 圆括号
            var pattern1 = $@"\b{Regex.Escape(toolName)}\s*\(([^)]*)\)";
            var match1 = Regex.Match(text, pattern1, RegexOptions.IgnoreCase);
            if (match1.Success)
            {
                var argsStr = match1.Groups[1].Value.Trim();
                var jsonArgs = ParseFuncArgsToJson(argsStr);
                return new List<ParsedToolCall> { new() { Name = toolName, Arguments = jsonArgs } };
            }

            // Pattern 2: funcName{key:value, ...}  — 花括号，冒号分隔
            var pattern2 = $@"\b{Regex.Escape(toolName)}\s*\{{([^}}]*)\}}";
            var match2 = Regex.Match(text, pattern2, RegexOptions.IgnoreCase);
            if (match2.Success)
            {
                var argsStr = match2.Groups[1].Value.Trim();
                var jsonArgs = ParseBraceArgsToJson(argsStr);
                return new List<ParsedToolCall> { new() { Name = toolName, Arguments = jsonArgs } };
            }

            // Pattern 3: funcName key=value  — 无括号，空格分隔
            var pattern3 = $@"\b{Regex.Escape(toolName)}\s+([a-z_]\w*)\s*=\s*";
            var match3 = Regex.Match(text, pattern3, RegexOptions.IgnoreCase);
            if (match3.Success)
            {
                // 提取整行/段中的 key=value 对
                var afterTool = text[match3.Index..];
                var endIdx = afterTool.IndexOfAny(new[] { '\n', '\r', '<', '>' });
                var argLine = endIdx > 0 ? afterTool[..endIdx].Trim() : afterTool.Trim();
                var jsonArgs = ParseFuncArgsToJson(argLine);
                return new List<ParsedToolCall> { new() { Name = toolName, Arguments = jsonArgs } };
            }
        }

        return null;
    }

    /// <summary>
    /// 将 {key:value, key2:"str"} 格式转换为 JSON。
    /// 支持值类型: 字符串(""), 数字, 布尔, 嵌套 JSON。
    /// </summary>
    private static string ParseBraceArgsToJson(string argsStr)
    {
        if (string.IsNullOrWhiteSpace(argsStr)) return "{}";

        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        int i = 0;
        bool first = true;

        while (i < argsStr.Length)
        {
            // Skip whitespace and commas
            if (argsStr[i] == ',' || char.IsWhiteSpace(argsStr[i]))
            {
                i++;
                continue;
            }

            // Parse key
            int keyStart = i;
            while (i < argsStr.Length && (char.IsLetterOrDigit(argsStr[i]) || argsStr[i] == '_' || argsStr[i] == '"'))
            {
                if (argsStr[i] == '"') { i++; continue; } // skip surrounding quotes in key
                i++;
            }
            if (i == keyStart) { i++; continue; }
            var key = argsStr[keyStart..i].Trim('"');

            // Skip : or =
            while (i < argsStr.Length && (argsStr[i] == ':' || argsStr[i] == '=' || char.IsWhiteSpace(argsStr[i])))
                i++;

            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(key).Append('"').Append(':');

            // Parse value
            if (i >= argsStr.Length)
            {
                sb.Append("null");
                break;
            }

            if (argsStr[i] == '"')
            {
                // String value
                i++;
                var valStart = i;
                while (i < argsStr.Length && argsStr[i] != '"')
                    i++;
                var val = argsStr[valStart..i];
                sb.Append('"').Append(EscapeJsonStr(val)).Append('"');
                if (i < argsStr.Length) i++;
            }
            else if (argsStr[i] == '{' || argsStr[i] == '[')
            {
                // Nested JSON: find matching close
                var open = argsStr[i];
                var close = open == '{' ? '}' : ']';
                var depth = 0;
                var valStart = i;
                while (i < argsStr.Length)
                {
                    if (argsStr[i] == open) depth++;
                    else if (argsStr[i] == close) { depth--; if (depth == 0) break; }
                    i++;
                }
                if (i < argsStr.Length) i++;
                sb.Append(argsStr[valStart..i]);
            }
            else
            {
                // Number, boolean, or unquoted string
                var valStart = i;
                while (i < argsStr.Length && argsStr[i] != ',' && argsStr[i] != '}' && !char.IsWhiteSpace(argsStr[i]))
                    i++;
                var val = argsStr[valStart..i];
                if (val is "true" or "false" or "null" || double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    sb.Append(val);
                else
                    sb.Append('"').Append(val).Append('"');
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJsonStr(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static readonly HashSet<string> KnownTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "modify_nodes", "modify_connections", "set_parameter", "query_info",
        "use_skill", "create_resource_node", "update_resource_node",
        "delete_resource_node", "get_node_resource",
        "create_recipe", "mutate_recipe", "random_recipe",
        "save_recipe", "apply_recipe", "list_recipes", "evaluate_recipe",
        "create_script_node", "execute_command", "run_python", "run_script",
        "write_file", "delete_file",
    };

    private static bool IsKnownTool(string name) => KnownTools.Contains(name);

    /// <summary>
    /// 将 key=value, key2="str", key3=123 格式的参数字符串转换为 JSON。
    /// </summary>
    private static string ParseFuncArgsToJson(string argsStr)
    {
        if (string.IsNullOrWhiteSpace(argsStr)) return "{}";

        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        int i = 0;
        bool first = true;

        while (i < argsStr.Length)
        {
            // Skip whitespace and commas
            if (argsStr[i] == ',' || char.IsWhiteSpace(argsStr[i]))
            {
                i++;
                continue;
            }

            // Parse key
            int keyStart = i;
            while (i < argsStr.Length && (char.IsLetterOrDigit(argsStr[i]) || argsStr[i] == '_'))
                i++;
            if (i == keyStart) { i++; continue; }
            var key = argsStr[keyStart..i];

            // Skip =
            while (i < argsStr.Length && (argsStr[i] == '=' || char.IsWhiteSpace(argsStr[i])))
                i++;

            if (!first) sb.Append(',');
            first = false;

            sb.Append('"').Append(key).Append('"').Append(':');

            // Parse value
            if (i >= argsStr.Length)
            {
                sb.Append("null");
                break;
            }

            if (argsStr[i] == '"' || argsStr[i] == '\'')
            {
                // String value
                var quote = argsStr[i];
                i++;
                var valStart = i;
                while (i < argsStr.Length && argsStr[i] != quote)
                    i++;
                var val = argsStr[valStart..i];
                sb.Append('"').Append(System.Security.SecurityElement.Escape(val) ?? val).Append('"');
                if (i < argsStr.Length) i++;
            }
            else
            {
                // Number, boolean, or null
                var valStart = i;
                while (i < argsStr.Length && argsStr[i] != ',' && !char.IsWhiteSpace(argsStr[i]))
                    i++;
                var val = argsStr[valStart..i];
                if (val is "true" or "false" or "null" || double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    sb.Append(val);
                else
                    sb.Append('"').Append(val).Append('"');
            }
        }

        sb.Append('}');
        return sb.ToString();
    }
}

/// <summary>Represents a parsed tool call from AI output.</summary>
public sealed class ParsedToolCall
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "{}";
}
