using System;
using System.Collections.Generic;

namespace PixelAssetGenerator.Models;

/// <summary>
/// A chat session containing conversation history and metadata.
/// </summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Conversation";
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Path of the associated MD plan file, if one was generated for this session.
    /// Null when no plan has been generated yet.
    /// </summary>
    public string? PlanFilePath { get; set; }
}

/// <summary>Lightweight summary of a session for the history sidebar.</summary>
public sealed class ChatSessionSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "New Conversation";
    public DateTime UpdatedAt { get; set; }
    public bool HasPlan { get; set; }
}
