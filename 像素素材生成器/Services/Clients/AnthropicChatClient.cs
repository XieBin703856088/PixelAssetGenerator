using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PixelAssetGenerator.Services.Clients;

/// <summary>Anthropic Claude streaming chat client.</summary>
public sealed class AnthropicChatClient : IStreamingChatClient
{
    private readonly HttpClient _http;

    public AnthropicChatClient(HttpClient http)
    {
        _http = http;
    }

    public async IAsyncEnumerable<AiStreamEvent> ChatStreamAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = request.Settings;
        yield return new AiStatus("Preparing request");
        _http.DefaultRequestHeaders.Authorization = null;
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey ?? "");
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // 1M context beta header (activate when context limit > 200K)
        _http.DefaultRequestHeaders.Remove("anthropic-beta");
        if (settings.ContextLimit > 200_000)
            _http.DefaultRequestHeaders.Add("anthropic-beta", "context-1m-2025-08-07");

        var isDeepSeekAnthropic = settings.BaseUrl?.Contains("deepseek", StringComparison.OrdinalIgnoreCase) == true;

        var messagesArray = BuildAnthropicMessages(request, isDeepSeekAnthropic);
        var toolsArray = BuildAnthropicTools(request.ToolDefinitions);

        var bodyObj = new Dictionary<string, object?>
        {
            ["model"] = settings.Model ?? "claude-sonnet-4-20250514",
            ["messages"] = messagesArray,
            ["stream"] = true,
            ["max_tokens"] = Math.Min(settings.MaxTokens, 8192),
            ["temperature"] = settings.Temperature
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            bodyObj["system"] = request.SystemPrompt;
        if (toolsArray != null)
            bodyObj["tools"] = toolsArray;

        if (isDeepSeekAnthropic)
        {
            // DeepSeek Anthropic-compatible endpoint uses "thinking" (OpenAI-style)
            // instead of Anthropic's output_config.
            if (!string.IsNullOrEmpty(settings.ReasoningEffort) && settings.ReasoningEffort != "off")
            {
                bodyObj["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" };
            }
        }
        else
        {
            // output_config effort (Anthropic extended thinking — replaces the old "thinking" block)
            if (!string.IsNullOrEmpty(settings.ReasoningEffort) && settings.ReasoningEffort != "off")
            {
                var effort = MapReasoningEffort(settings.ReasoningEffort);
                bodyObj["output_config"] = new Dictionary<string, object>
                {
                    ["effort"] = effort
                };
            }
        }

        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? "https://api.anthropic.com";
        var url = $"{baseUrl}/v1/messages";

        // Debug logging
        var debugRequestId = Guid.NewGuid().ToString("N")[..8];
        System.Diagnostics.Debug.WriteLine($"[AnthropicChatClient:{debugRequestId}] URL: {url}");
        System.Diagnostics.Debug.WriteLine($"[AnthropicChatClient:{debugRequestId}] Body: {bodyJson}");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        yield return new AiStatus("Sending request");
        using var response = await _http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[AnthropicChatClient:{debugRequestId}] Error {(int)response.StatusCode}: {errorBody}");
            yield return new AiError($"API Error ({(int)response.StatusCode}): {errorBody}");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        yield return new AiStatus("Model processing initial context");

        string? currentToolId = null;
        string? currentToolName = null;
        var currentToolArgs = new StringBuilder();
        var receivedFirstDelta = false;
        // Accumulate all tool calls across content blocks in this response
        var accumulatedToolCalls = new List<AiToolCallData>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            if (line.AsSpan(6).Trim().SequenceEqual("[DONE]"))
            {
                yield return new AiDone();
                yield break;
            }

            JsonElement eventData;
            try { eventData = JsonSerializer.Deserialize<JsonElement>(line.AsSpan(6)); }
            catch { continue; }

            var type = eventData.TryGetProperty("type", out var t) ? t.GetString() : "";

            switch (type)
            {
                case "content_block_delta":
                    if (eventData.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("type", out var dt))
                        {
                            var dtStr = dt.GetString();
                            if (dtStr == "text_delta" && delta.TryGetProperty("text", out var textEl))
                            {
                                if (!receivedFirstDelta)
                                {
                                    receivedFirstDelta = true;
                                    yield return new AiStatus("Streaming output");
                                }
                                yield return new AiTextDelta(textEl.GetString() ?? "");
                            }
                            else if (dtStr == "input_json_delta" && currentToolId != null && delta.TryGetProperty("partial_json", out var pj))
                                currentToolArgs.Append(pj.GetString());
                            else if (dtStr == "thinking_delta" && delta.TryGetProperty("thinking", out var thinkingEl))
                            {
                                if (!receivedFirstDelta)
                                {
                                    receivedFirstDelta = true;
                                    yield return new AiStatus("Reasoning");
                                }
                                yield return new AiReasoningDelta(thinkingEl.GetString() ?? "");
                            }
                        }
                        // DeepSeek Anthropic endpoint may send OpenAI-style reasoning_content
                        else if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                        {
                            var reasoning = rc.GetString();
                            if (!string.IsNullOrEmpty(reasoning))
                            {
                                if (!receivedFirstDelta)
                                {
                                    receivedFirstDelta = true;
                                    yield return new AiStatus("Reasoning");
                                }
                                yield return new AiReasoningDelta(reasoning);
                            }
                        }
                    }
                    break;

                case "content_block_start":
                    if (eventData.TryGetProperty("content_block", out var block) && block.TryGetProperty("type", out var bt))
                    {
                        var blockType = bt.GetString();
                        if (blockType == "tool_use")
                        {
                            currentToolId = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            currentToolName = block.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                            currentToolArgs = new StringBuilder();
                        }
                        // Skip "thinking" block starts silently
                    }
                    break;

                case "content_block_stop":
                    if (currentToolId != null)
                    {
                        accumulatedToolCalls.Add(new(currentToolId, currentToolName ?? "", currentToolArgs.ToString()));
                        currentToolId = null;
                        currentToolName = null;
                        currentToolArgs = new StringBuilder();
                    }
                    break;

                case "message_delta":
                    if (eventData.TryGetProperty("delta", out var msgDelta) && msgDelta.TryGetProperty("stop_reason", out var sr))
                    {
                        var stopReason = sr.GetString();
                        if (stopReason == "tool_use" && accumulatedToolCalls.Count > 0)
                        {
                            yield return new AiStatus("Preparing tool calls");
                            yield return new AiToolCallBatch(accumulatedToolCalls);
                            accumulatedToolCalls = new List<AiToolCallData>();
                        }
                        else if (stopReason is "end_turn" or "max_tokens" or "stop_sequence")
                        {
                            yield return new AiDone();
                            yield break;
                        }
                    }
                    break;

                case "message_stop":
                    yield return new AiDone();
                    yield break;
            }
        }

        yield return new AiDone();
    }

    private static string MapReasoningEffort(string effort) => effort switch
    {
        "minimal" => "low",
        "max" => "max",
        _ => effort // low, medium, high pass through
    };

    private static JsonElement BuildAnthropicMessages(AiChatRequest request, bool isDeepSeekAnthropic = false)
    {
        // First pass: collect consecutive tool messages into groups per round.
        // Each group becomes a single user message with multiple tool_result blocks.
        var merged = new List<AiMessage>();
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "system") continue;

            if (msg.Role == "tool" && merged.Count > 0 && merged[^1].Role == "tool")
            {
                // Merge consecutive tool messages: combine tool_calls lists
                var last = merged[^1];
                var mergedToolCalls = new List<AiToolCallData>();
                if (last.ToolCalls != null)
                    mergedToolCalls.AddRange(last.ToolCalls);
                if (msg.ToolCalls != null)
                    mergedToolCalls.AddRange(msg.ToolCalls);

                merged[^1] = last with
                {
                    ToolCalls = mergedToolCalls
                };
            }
            else
            {
                merged.Add(msg);
            }
        }

        // Second pass: build Anthropic-format messages
        var messages = new List<object>();
        foreach (var msg in merged)
        {
            if (isDeepSeekAnthropic && msg.Role == "assistant")
            {
                // DeepSeek Anthropic endpoint expects content as blocks array.
                // When thinking/reasoning exists, it goes as a "thinking" content block.
                // DeepSeek requires that thinking blocks are always followed by at least one text block.
                var hasReasoning = !string.IsNullOrEmpty(msg.ReasoningContent);
                var hasContent = !string.IsNullOrEmpty(msg.Content);
                var hasToolCalls = msg.ToolCalls is { Count: > 0 };

                // No content at all → empty string (not null, not empty array)
                if (!hasReasoning && !hasContent && !hasToolCalls)
                {
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "assistant",
                        ["content"] = ""
                    });
                    continue;
                }

                var contentList = new List<object>();

                // DeepSeek: only assistant messages with tool_calls need thinking block
                if (hasToolCalls && hasReasoning)
                {
                    contentList.Add(new { type = "thinking", thinking = msg.ReasoningContent });
                }
                else if (hasToolCalls)
                {
                    // Tool-call assistant messages still need a thinking block (can be empty)
                    contentList.Add(new { type = "thinking", thinking = "" });
                }

                if (hasContent)
                    contentList.Add(new { type = "text", text = msg.Content });
                else if (hasToolCalls && !hasReasoning && !hasContent)
                {
                    // tool_calls with no reasoning and no content: add empty text block
                    contentList.Add(new { type = "text", text = "" });
                }

                if (hasToolCalls)
                {
                    foreach (var tc in msg.ToolCalls!)
                    {
                        JsonElement toolInput = JsonSerializer.Deserialize<JsonElement>("{}");
                        if (!string.IsNullOrEmpty(tc.Arguments))
                        {
                            try { toolInput = JsonSerializer.Deserialize<JsonElement>(tc.Arguments); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AnthropicChatClient] Tool arg parse failed: {ex.Message}"); }
                        }
                        contentList.Add(new { type = "tool_use", id = tc.Id, name = tc.FunctionName, input = toolInput });
                    }
                }

                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = contentList
                });
                continue;
            }

            if (msg.Role == "tool")
            {
                // DeepSeek Anthropic: single user message with multiple tool_result blocks.
                // Each tool_result gets its own content from the corresponding ChatToolCall.Result
                var toolResults = new List<object>();
                foreach (var tc in msg.ToolCalls ?? Enumerable.Empty<AiToolCallData>())
                {
                    var resultContent = tc.Arguments; // default fallback
                    // Look for stored result — the original ChatToolCall had a Result field
                    // but AiToolCallData doesn't. Use the message Content as fallback.
                    toolResults.Add(new { type = "tool_result", tool_use_id = tc.Id, content = msg.Content ?? "" });
                }
                messages.Add(new { role = "user", content = toolResults });
            }
            else if (msg.ToolCalls is { Count: > 0 })
            {
                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contentList.Add(new { type = "text", text = msg.Content });
                foreach (var tc in msg.ToolCalls)
                {
                    JsonElement toolInput = JsonSerializer.Deserialize<JsonElement>("{}");
                    if (!string.IsNullOrEmpty(tc.Arguments))
                    {
                        try { toolInput = JsonSerializer.Deserialize<JsonElement>(tc.Arguments); }
                        catch { }
                    }
                    contentList.Add(new { type = "tool_use", id = tc.Id, name = tc.FunctionName, input = toolInput });
                }
                messages.Add(new { role = msg.Role, content = contentList });
            }
            else if (msg.Attachments is { Count: > 0 })
            {
                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contentList.Add(new { type = "text", text = msg.Content });
                foreach (var file in msg.Attachments)
                {
                    if (file.MediaType.StartsWith("image/"))
                        contentList.Add(new { type = "image", source = new { type = "base64", media_type = file.MediaType, data = file.ToBase64() } });
                    else
                        contentList.Add(new { type = "text", text = $"[Attachment: {file.FileName} ({file.MediaType}, {file.Data.Length} bytes)]" });
                }
                messages.Add(new { role = msg.Role, content = contentList });
            }
            else
            {
                messages.Add(new { role = msg.Role, content = msg.Content ?? "" });
            }
        }
        return JsonSerializer.SerializeToElement(messages);
    }

    private static JsonElement? BuildAnthropicTools(IReadOnlyList<JsonElement>? toolDefinitions)
    {
        if (toolDefinitions == null || toolDefinitions.Count == 0) return null;
        var tools = new List<object>();
        foreach (var td in toolDefinitions)
        {
            if (td.TryGetProperty("function", out var fn))
            {
                var name = fn.TryGetProperty("name", out var n) ? n.GetString() : "";
                var desc = fn.TryGetProperty("description", out var d) ? d.GetString() : "";
                var inputSchema = fn.TryGetProperty("parameters", out var p) ? p : JsonSerializer.Deserialize<JsonElement>("{}");
                tools.Add(new { name, description = desc, input_schema = inputSchema });
            }
            else
            {
                var name = td.TryGetProperty("name", out var n) ? n.GetString() : "";
                var desc = td.TryGetProperty("description", out var d) ? d.GetString() : "";
                var inputSchema = td.TryGetProperty("input_schema", out var s) ? s : JsonSerializer.Deserialize<JsonElement>("{}");
                tools.Add(new { name, description = desc, input_schema = inputSchema });
            }
        }
        return JsonSerializer.SerializeToElement(tools);
    }
}
