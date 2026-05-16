using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Manages conversation history with token-aware trimming, summarization,
/// and optional persistence to disk.
/// </summary>
public sealed class ConversationHistoryManager
{
    private readonly string _chatsDir;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly List<ChatMessage> _messages = new();

    private const int MaxMessages = 50;
    private const int MaxToolPairs = 10;
    private const int TargetTrimCount = 30;

    public ConversationHistoryManager()
    {
        _chatsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelAssetGenerator", "chats");
        Directory.CreateDirectory(_chatsDir);
    }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public void AddMessage(ChatMessage message)
    {
        _messages.Add(message);
        TrimIfNeeded();
    }

    public void AddUserMessage(string content)
    {
        AddMessage(new ChatMessage { Role = "user", Content = content });
    }

    public void AddAssistantMessage(string? content, List<ChatToolCall>? toolCalls = null, string? reasoningContent = null)
    {
        AddMessage(new ChatMessage
        {
            Role = "assistant",
            Content = content,
            ToolCalls = toolCalls,
            ReasoningContent = reasoningContent
        });
    }

    public void AddToolResult(string toolCallId, string functionName, string arguments, string result)
    {
        AddMessage(new ChatMessage
        {
            Role = "tool",
            Content = result,
            ToolCalls = new List<ChatToolCall>
            {
                new() { Id = toolCallId, FunctionName = functionName, Arguments = arguments, Result = result }
            }
        });
    }

    public void Clear()
    {
        _messages.Clear();
    }

    /// <summary>
    /// 替换最后一条以指定前缀开头的消息，如果没有则追加。
    /// 用于更新状态摘要，避免历史无限膨胀。
    /// </summary>
    public void ReplaceOrAddMessage(string rolePrefix, string newContent)
    {
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Role == "user" && _messages[i].Content != null && _messages[i].Content!.StartsWith(rolePrefix))
            {
                _messages[i] = new ChatMessage { Role = "user", Content = newContent, Timestamp = DateTime.UtcNow };
                return;
            }
        }
        _messages.Add(new ChatMessage { Role = "user", Content = newContent, Timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Estimates token count for a string (~4 chars per token for mixed CJK/English).
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4 + 1;
    }

    /// <summary>
    /// Estimates total tokens in the conversation history.
    /// </summary>
    public int EstimateTotalTokens()
    {
        int total = 0;
        foreach (var msg in _messages)
        {
            total += EstimateTokens(msg.Content);
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                {
                    total += EstimateTokens(tc.Arguments);
                    total += EstimateTokens(tc.Result);
                }
            }
        }
        return total;
    }

    /// <summary>
    /// Trims the conversation when it exceeds limits, keeping the most
    /// recent messages and optionally summarising older ones.
    /// </summary>
    private void TrimIfNeeded()
    {
        TrimToolCallPairs();

        if (_messages.Count > MaxMessages)
        {
            int toRemove = _messages.Count - TargetTrimCount;
            toRemove = Math.Min(toRemove, _messages.Count - 10);

            // Adjust toRemove so we never split a tool_use/tool_result pair.
            // Walk back from the cut point to a safe boundary:
            //   "user" or "assistant(no tool_calls)" or the start of a complete tool round.
            if (toRemove > 0)
            {
                int cutIdx = toRemove;
                // Scan backward from cutIdx to find a safe cut point.
                for (int i = cutIdx - 1; i >= 0; i--)
                {
                    var m = _messages[i];
                    if (m.Role == "tool")
                    {
                        // Can't cut here — the tool_result belongs to an assistant before the cut.
                        // Move cutIdx to just before the corresponding assistant.
                        for (int a = i - 1; a >= 0; a--)
                        {
                            if (_messages[a].Role == "assistant" &&
                                _messages[a].ToolCalls is { Count: > 0 })
                            {
                                cutIdx = a;
                                i = a; // continue scanning from here
                                break;
                            }
                        }
                    }
                    else if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
                    {
                        // Check if any of this assistant's tool_results would be orphaned.
                        bool hasToolAfterCut = false;
                        for (int t = cutIdx; t < _messages.Count && _messages[t].Role == "tool"; t++)
                        {
                            hasToolAfterCut = true;
                            break;
                        }
                        if (!hasToolAfterCut)
                            break; // safe cut: no orphaned tool_results
                        // Move cutIdx to include this assistant
                        cutIdx = i;
                    }
                    else
                    {
                        // user message or plain assistant → safe cut point
                        break;
                    }
                }

                if (cutIdx > 0)
                    _messages.RemoveRange(0, cutIdx);
            }
        }
    }

    private void TrimToolCallPairs()
    {
        // First, clean up any orphaned tool messages (no corresponding assistant with tool_use).
        // This can happen from previous TrimIfNeeded RemoveRange operations.
        CleanOrphanedToolMessages();

        int toolRounds = 0;
        foreach (var msg in _messages)
        {
            if (msg.Role == "tool")
                toolRounds++;
        }

        if (toolRounds <= MaxToolPairs) return;

        int toRemove = toolRounds - MaxToolPairs;

        // Remove entire tool-rounds (assistant + its tool messages) from the back.
        // This guarantees no orphaned tool_result without a corresponding tool_use.
        while (toRemove > 0)
        {
            // Find the last assistant message that has tool_calls
            int asstIdx = -1;
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Role == "assistant" && _messages[i].ToolCalls is { Count: > 0 })
                {
                    asstIdx = i;
                    break;
                }
            }
            if (asstIdx < 0) break; // no more assistant tool-call messages

            int removeStart = asstIdx;
            int removeCount = 1; // always remove the assistant

            // Also remove all following consecutive tool messages
            for (int k = asstIdx + 1; k < _messages.Count && _messages[k].Role == "tool"; k++)
                removeCount++;

            _messages.RemoveRange(removeStart, removeCount);
            toRemove--;
        }
    }

    /// <summary>
    /// Removes tool_result messages that have no corresponding assistant(tool_use)
    /// message before them. These orphans cause API validation errors.
    /// </summary>
    private void CleanOrphanedToolMessages()
    {
        // Remove leading tool messages (no assistant before them)
        while (_messages.Count > 0 && _messages[0].Role == "tool")
        {
            _messages.RemoveAt(0);
        }

        // Find tool messages whose immediate preceding assistant has no tool_calls,
        // meaning they're orphaned from a previous round.
        for (int i = 1; i < _messages.Count; i++)
        {
            if (_messages[i].Role != "tool") continue;

            // Find the nearest preceding assistant that (may) have tool_calls
            int nearestAsstIdx = -1;
            for (int j = i - 1; j >= 0; j--)
            {
                if (_messages[j].Role == "assistant")
                {
                    nearestAsstIdx = j;
                    break;
                }
            }

            if (nearestAsstIdx < 0)
            {
                // No assistant at all before this tool → orphan
                _messages.RemoveAt(i);
                i--;
                continue;
            }

            var asst = _messages[nearestAsstIdx];
            if (asst.ToolCalls is not { Count: > 0 })
            {
                // Nearest assistant has no tool_calls → this tool is orphaned
                _messages.RemoveAt(i);
                i--;
                continue;
            }

            // Verify the tool's ID actually exists in the assistant's tool_calls
            var toolId = _messages[i].ToolCalls?.FirstOrDefault()?.Id;
            if (toolId != null && !asst.ToolCalls.Any(tc => tc.Id == toolId))
            {
                // Tool call ID not found in the nearest assistant → orphan
                _messages.RemoveAt(i);
                i--;
            }
        }
    }

    /// <summary>
    /// Saves the current session to disk.
    /// </summary>
    public void SaveSession(string sessionId, string title = "新对话", string? planFilePath = null)
    {
        var session = new ChatSession
        {
            Id = sessionId,
            Title = title,
            Messages = new List<ChatMessage>(_messages),
            UpdatedAt = DateTime.UtcNow,
            PlanFilePath = planFilePath
        };
        var path = Path.Combine(_chatsDir, $"{sessionId}.json");
        var json = JsonSerializer.Serialize(session, _jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads a session from disk.
    /// </summary>
    public static ChatSession? LoadSession(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ChatSession>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a session file and its associated plan file from disk.
    /// </summary>
    public void DeleteSession(string sessionId)
    {
        var sessionPath = Path.Combine(_chatsDir, $"{sessionId}.json");
        if (File.Exists(sessionPath))
        {
            try
            {
                // Load to find PlanFilePath before deleting
                var session = LoadSession(sessionPath);
                if (session?.PlanFilePath != null && File.Exists(session.PlanFilePath))
                    File.Delete(session.PlanFilePath);
                File.Delete(sessionPath);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ConversationHistoryManager.DeleteSession failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Returns summaries of all saved sessions, sorted by most recently updated.
    /// </summary>
    public IReadOnlyList<ChatSessionSummary> GetAllSessionSummaries()
    {
        var result = new List<ChatSessionSummary>();
        try
        {
            foreach (var file in Directory.GetFiles(_chatsDir, "*.json"))
            {
                var session = LoadSession(file);
                if (session == null) continue;
                result.Add(new ChatSessionSummary
                {
                    Id = session.Id,
                    Title = session.Title,
                    UpdatedAt = session.UpdatedAt,
                    HasPlan = session.PlanFilePath != null && File.Exists(session.PlanFilePath)
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ConversationHistoryManager.GetAllSessionSummaries failed: {ex.Message}"); }
        return result.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    /// <summary>
    /// Loads a session from disk and populates the internal message list.
    /// Returns true on success.
    /// </summary>
    public bool LoadSessionById(string sessionId)
    {
        var path = Path.Combine(_chatsDir, $"{sessionId}.json");
        var session = LoadSession(path);
        if (session == null) return false;

        _messages.Clear();
        _messages.AddRange(session.Messages);

        // Update timestamp
        session.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(session, _jsonOptions);
        File.WriteAllText(path, json);

        return true;
    }

    /// <summary>
    /// Gets the plan file path for a session, or null if none.
    /// </summary>
    public string? GetSessionPlanPath(string sessionId)
    {
        var path = Path.Combine(_chatsDir, $"{sessionId}.json");
        var session = LoadSession(path);
        return session?.PlanFilePath;
    }

    /// <summary>
    /// Returns the path to the chats directory so callers can resolve plan file paths.
    /// </summary>
    public string ChatsDirectory => _chatsDir;

    /// <summary>
    /// Converts internal messages to AiMessage list for the AI service.
    /// </summary>
    public List<AiMessage> ToAiMessages()
    {
        var result = new List<AiMessage>();
        foreach (var msg in _messages)
        {
            if (msg.Role == "system") continue;

            var toolCalls = msg.ToolCalls?
                .Select(tc =>
                {
                    var cleanArgs = tc.Arguments;
                    if (!string.IsNullOrEmpty(cleanArgs))
                    {
                        try { cleanArgs = JsonSerializer.Deserialize<JsonElement>(cleanArgs).GetRawText(); }
                        catch { cleanArgs = "{}"; }
                    }
                    return new AiToolCallData(tc.Id, tc.FunctionName, cleanArgs);
                })
                .ToList();

            result.Add(new AiMessage(msg.Role, msg.Content, toolCalls, ReasoningContent: msg.ReasoningContent));
        }
        return result;
    }

    /// <summary>
    /// Returns the oldest messages (first N) as a single text block for compression.
    /// </summary>
    public string GetMessagesText(int count)
    {
        var sb = new StringBuilder();
        int limit = Math.Min(count, _messages.Count);
        for (int i = 0; i < limit; i++)
        {
            var m = _messages[i];
            sb.Append('[').Append(m.Role).AppendLine("]");
            if (!string.IsNullOrEmpty(m.Content))
                sb.AppendLine(m.Content);
            if (m.ToolCalls != null)
            {
                foreach (var tc in m.ToolCalls)
                {
                    sb.AppendLine($"  [工具调用: {tc.FunctionName}]");
                    var resultPreview = tc.Result;
                    if (resultPreview?.Length > 200)
                        resultPreview = resultPreview[..200] + "...";
                    if (!string.IsNullOrEmpty(resultPreview))
                        sb.AppendLine($"  → {resultPreview}");
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Replaces the first <paramref name="count"/> messages with a compressed system message.
    /// </summary>
    public void ReplaceWithSummary(int count, string summary)
    {
        if (count <= 0 || count > _messages.Count) return;

        int actualCount = Math.Min(count, _messages.Count);
        _messages.RemoveRange(0, actualCount);
        _messages.Insert(0, new ChatMessage
        {
            Role = "system",
            Content = $"[压缩的上文]\n{summary}"
        });
    }
}
