using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PixelAssetGenerator.Services.Clients;

/// <summary>OpenAI-compatible streaming chat client.</summary>
public sealed class OpenAiChatClient : IStreamingChatClient
{
    private readonly HttpClient _http;
    // Buffer for reassembling JSON lines split across multiple TCP segments
    private readonly StringBuilder _pendingJson = new();

    public OpenAiChatClient(HttpClient http)
    {
        _http = http;
    }

    public async IAsyncEnumerable<AiStreamEvent> ChatStreamAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var ev in StreamEvents(request, ct))
            yield return ev;
    }

    private async IAsyncEnumerable<AiStreamEvent> StreamEvents(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = request.Settings;
        yield return new AiStatus("Preparing request");
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Remove("anthropic-beta");
        _http.DefaultRequestHeaders.Authorization =
            string.IsNullOrWhiteSpace(settings.ApiKey)
                ? null
                : new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var messagesJson = BuildOpenAiMessages(request);
        var toolsJson = BuildOpenAiTools(request.ToolDefinitions);

        var model = settings.Model ?? "gpt-4o-mini";
        var isReasoningModel = model.StartsWith("o1") || model.StartsWith("o3") || model.StartsWith("o4");

        var bodyObj = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messagesJson,
            ["stream"] = true,
        };

        if (isReasoningModel)
        {
            bodyObj["max_completion_tokens"] = Math.Min(settings.MaxTokens, 1_048_576);
        }
        else
        {
            bodyObj["temperature"] = settings.Temperature;
            bodyObj["max_tokens"] = Math.Min(settings.MaxTokens, 1_048_576);
        }

        if (toolsJson != null)
            bodyObj["tools"] = toolsJson;

        if (!string.IsNullOrEmpty(settings.ReasoningEffort) && settings.ReasoningEffort != "off")
        {
            if (model.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase))
            {
                // DeepSeek: uses "thinking" instead of "reasoning_effort"
                bodyObj["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" };
            }
            else
            {
                // OpenAI o1/o3 / other OpenAI-compatible: uses "reasoning_effort"
                bodyObj["reasoning_effort"] = settings.ReasoningEffort;
            }
        }

        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? "https://api.openai.com";
        var url = baseUrl.Contains("/v1") ? $"{baseUrl}/chat/completions" : $"{baseUrl}/v1/chat/completions";

        // Debug logging
        var debugRequestId = Guid.NewGuid().ToString("N")[..8];
        System.Diagnostics.Debug.WriteLine($"[OpenAiChatClient:{debugRequestId}] URL: {url}");
        System.Diagnostics.Debug.WriteLine($"[OpenAiChatClient:{debugRequestId}] Body: {bodyJson}");

        HttpResponseMessage? response = null;
        yield return new AiStatus("Sending request");
        var (sendResponse, preSendError) = await SendStreamingRequestAsync(url, content, ct);
        response = sendResponse;

        if (preSendError != null)
        {
            yield return new AiError($"Request failed: {preSendError}");
            yield break;
        }

#if DEBUG
        try
        {
            var debugDir = AppDomain.CurrentDomain.BaseDirectory;
            File.WriteAllText(Path.Combine(debugDir, "last_request.json"), bodyJson);
            File.WriteAllText(Path.Combine(debugDir, "last_response_status.txt"), $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OpenAiChatClient] Debug log write failed: {ex.Message}"); }
#endif

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine($"[OpenAiChatClient:{debugRequestId}] Error {(int)response.StatusCode}: {errorBody}");
            if (errorBody.Length > 500)
                errorBody = errorBody[..500] + "...";
            yield return new AiError($"API Error ({(int)response.StatusCode}): {errorBody}");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        yield return new AiStatus("Model processing initial context");

        var currentToolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        var receivedFirstDelta = false;
        string? streamError = null;

        while (!reader.EndOfStream)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (Exception ex)
            {
                streamError = ex.Message;
                break;
            }
            if (line == null) break;

            // Some providers send leading whitespace or empty lines between SSE events
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.Length == 0)
                continue;

            if (!trimmed.StartsWith("data: "))
            {
                continue;
            }

            var data = trimmed.Slice(6).Trim();
            if (data.SequenceEqual("[DONE]"))
            {
                yield return new AiDone();
                yield break;
            }

            // Some providers send "data: ,{...}" with a leading comma; strip it
            if (data.Length > 0 && data[0] == ',')
                data = data.Slice(1);

            // Some providers append metadata or event type after data content;
            // extract only the JSON value by trimming trailing non-JSON content.
            // This guards against e.g. "...}\n\nG" or "..."id":"Gen-xxx" where
            // a trailing G or other char breaks JSON parsing.
            {
                // Find the last '}' (end of JSON object) and slice there.
                // If no '}' found, keep the original span — TryParseJson will handle it.
                var lastBrace = data.LastIndexOf('}');
                if (lastBrace >= 0 && lastBrace < data.Length - 1)
                    data = data[..(lastBrace + 1)];
            }

            if (data.Length == 0)
                continue;

            // Reassemble JSON split across TCP segments: buffer failed parse attempts
            // and try again on the next line.
            ReadOnlySpan<char> jsonToParse;
            bool hasPending = _pendingJson.Length > 0;
            if (hasPending)
            {
                _pendingJson.Append(data);
                jsonToParse = _pendingJson.ToString().AsSpan();
            }
            else
            {
                jsonToParse = data;
            }

            if (TryParseJson(jsonToParse, out var chunk))
            {
                _pendingJson.Clear();
            }
            else if (hasPending)
            {
                // Still invalid after appending — discard both fragments
                _pendingJson.Clear();
                continue;
            }
            else
            {
                // Buffer this fragment and wait for the next line
                _pendingJson.Append(data);
                continue;
            }

            if (!chunk.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            var delta = choice.TryGetProperty("delta", out var d) ? d : default;

            if (delta.TryGetProperty("content", out var contentVal) && contentVal.ValueKind == JsonValueKind.String)
            {
                var text = contentVal.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    if (!receivedFirstDelta)
                    {
                        receivedFirstDelta = true;
                        yield return new AiStatus("Streaming output");
                    }
                    yield return new AiTextDelta(text);
                }
            }

            if (delta.TryGetProperty("reasoning_content", out var reasoningText) && reasoningText.ValueKind == JsonValueKind.String)
            {
                var reasoning = reasoningText.GetString();
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

            if (delta.TryGetProperty("reasoning", out var reasoningNode))
            {
                if (reasoningNode.ValueKind == JsonValueKind.String)
                {
                    var reasoning = reasoningNode.GetString();
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
                else if (reasoningNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in reasoningNode.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var reasoning = item.GetString();
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
                }
            }

            if (delta.TryGetProperty("tool_calls", out var toolCallsArr))
            {
                foreach (var tc in toolCallsArr.EnumerateArray())
                {
                    var idx = tc.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                    if (!currentToolCalls.ContainsKey(idx))
                    {
                        var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() : $"call_{idx}";
                        var name = tc.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var nEl)
                            ? nEl.GetString() ?? "" : "";
                        currentToolCalls[idx] = (id ?? $"call_{idx}", name, new StringBuilder());
                    }
                    if (tc.TryGetProperty("function", out var fn2) && fn2.TryGetProperty("arguments", out var argsEl))
                        currentToolCalls[idx].Args.Append(argsEl.GetString());
                }
            }

            if (choice.TryGetProperty("finish_reason", out var finish))
            {
                var finishReason = finish.GetString();
                if (finishReason == "tool_calls" && currentToolCalls.Count > 0)
                {
                    yield return new AiStatus("Preparing tool calls");
                    var calls = new List<AiToolCallData>();
                    foreach (var kv in currentToolCalls)
                        calls.Add(new AiToolCallData(kv.Value.Id, kv.Value.Name, SanitizeToolArgs(kv.Value.Args.ToString())));
                    yield return new AiToolCallBatch(calls);
                    currentToolCalls.Clear();
                }
                else if (finishReason is "stop" or "content_filter")
                {
                    yield return new AiDone();
                    yield break;
                }
                else if (finishReason == "length")
                {
                    yield return new AiDone(IsTruncated: true);
                    yield break;
                }
            }
        }

        if (streamError != null)
        {
            yield return new AiError($"Stream error: {streamError}");
            yield break;
        }

        if (currentToolCalls.Count > 0)
        {
            var calls = new List<AiToolCallData>();
            foreach (var kv in currentToolCalls)
                calls.Add(new AiToolCallData(kv.Value.Id, kv.Value.Name, SanitizeToolArgs(kv.Value.Args.ToString())));
            yield return new AiToolCallBatch(calls);
        }

        yield return new AiDone();
    }

    private static string SanitizeToolArgs(string? args)
    {
        if (string.IsNullOrEmpty(args)) return "{}";
        try { JsonSerializer.Deserialize<JsonElement>(args); return args; }
        catch { return "{}"; }
    }

    private async Task<(HttpResponseMessage? Response, string? Error)> SendStreamingRequestAsync(string url, HttpContent content, CancellationToken ct)
    {
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            var response = await _http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return (response, null);
        }
        catch (Exception ex)
        {
#if DEBUG
            try
            {
                var debugDir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(Path.Combine(debugDir, "last_error.txt"),
                    $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
            catch (Exception debugEx) { System.Diagnostics.Debug.WriteLine($"[OpenAiChatClient] Error log write failed: {debugEx.Message}"); }
#endif

            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static JsonElement BuildOpenAiMessages(AiChatRequest request)
    {
        // DeepSeek thinking mode requires "thinking" field in assistant messages history,
        // not "reasoning_content". Detect this once at the top.
        var isDeepSeekThinking = request.Settings?.Model?.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase) == true
            && !string.IsNullOrEmpty(request.Settings?.ReasoningEffort)
            && request.Settings.ReasoningEffort != "off";

        var messages = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new Dictionary<string, object?> { ["role"] = "system", ["content"] = request.SystemPrompt });

        foreach (var msg in request.Messages)
        {
            if (msg.Role == "tool")
            {
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = msg.ToolCalls?[0]?.Id ?? "",
                    ["content"] = msg.Content ?? ""
                });
            }
            else if (msg.ToolCalls is { Count: > 0 })
            {
                var toolCalls = new List<object>();
                foreach (var tc in msg.ToolCalls)
                {
                    toolCalls.Add(new { id = tc.Id, type = "function", function = new { name = tc.FunctionName, arguments = tc.Arguments } });
                }

                // DeepSeek thinking mode: content must be string or array (never null),
                // and "thinking" field must always be present as a string
                var hasContent = !string.IsNullOrEmpty(msg.Content);
                var hasReasoning = !string.IsNullOrEmpty(msg.ReasoningContent);

                var assistantMsg = new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = hasContent ? msg.Content : (isDeepSeekThinking ? "" : null),
                    ["tool_calls"] = toolCalls
                };
                if (isDeepSeekThinking)
                {
                    // DeepSeek: "thinking" must be a string, never null or omitted
                    assistantMsg["thinking"] = hasReasoning ? msg.ReasoningContent : "";
                }
                else if (hasReasoning)
                {
                    assistantMsg["reasoning_content"] = msg.ReasoningContent;
                }
                messages.Add(assistantMsg);
            }
            else if (msg.Attachments is { Count: > 0 })
            {
                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contentList.Add(new { type = "text", text = msg.Content });
                foreach (var file in msg.Attachments)
                {
                    if (file.MediaType.StartsWith("image/"))
                        contentList.Add(new { type = "image_url", image_url = new { url = file.ToDataUri() } });
                    else
                        contentList.Add(new { type = "text", text = $"[Attachment: {file.FileName} ({file.MediaType}, {file.Data.Length} bytes)]" });
                }
                messages.Add(new Dictionary<string, object?> { ["role"] = msg.Role, ["content"] = contentList });
            }
            else
            {
                // DeepSeek: non-tool-call assistant messages do NOT need reasoning_content
                var dict = new Dictionary<string, object?>
                {
                    ["role"] = msg.Role,
                    ["content"] = string.IsNullOrEmpty(msg.Content)
                        ? (isDeepSeekThinking ? "" : null)
                        : msg.Content
                };
                if (!isDeepSeekThinking && !string.IsNullOrEmpty(msg.ReasoningContent))
                {
                    dict["reasoning_content"] = msg.ReasoningContent;
                }
                messages.Add(dict);
            }
        }
        return JsonSerializer.SerializeToElement(messages);
    }

    private static JsonElement? BuildOpenAiTools(IReadOnlyList<JsonElement>? toolDefinitions)
    {
        if (toolDefinitions == null || toolDefinitions.Count == 0) return null;
        return JsonSerializer.SerializeToElement(toolDefinitions);
    }

    /// <summary>
    /// Safely attempts to parse a ReadOnlySpan as JSON. Returns false on any failure
    /// (malformed JSON, truncated data, non-JSON text) without throwing.
    /// </summary>
    private static bool TryParseJson(ReadOnlySpan<char> text, out JsonElement result)
    {
        result = default;
        if (text.Length == 0) return false;
        try
        {
            result = JsonSerializer.Deserialize<JsonElement>(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
