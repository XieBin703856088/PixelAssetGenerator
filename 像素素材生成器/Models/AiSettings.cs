using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PixelAssetGenerator;

/// <summary>AI integration configuration.</summary>
public sealed class AiSettings
{
    /// <summary>API provider name: "OpenAI兼容" or "Anthropic Claude".</summary>
    public string Provider { get; set; } = "OpenAI Compatible";

    /// <summary>API key for authentication.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base URL for the API endpoint.</summary>
    public string? BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>Model identifier.</summary>
    public string? Model { get; set; } = "gpt-4o-mini";

    /// <summary>List of available models for quick switching within this profile.</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>Index of the currently selected model in <see cref="Models"/>.</summary>
    public int ActiveModelIndex { get; set; } = 0;

    /// <summary>Sampling temperature (0.0–2.0).</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>Maximum tokens per response. Defaults to the same value as ContextLimit.</summary>
    public int MaxTokens { get; set; } = 65536;

    /// <summary>Maximum tool call rounds per user message (prevents infinite loops).</summary>
    public int MaxToolCallRounds { get; set; } = 8;

    /// <summary>Security switch: enable script node creation.</summary>
    public bool EnableScriptNode { get; set; } = false;

    /// <summary>Reasoning effort: "off", "none", "minimal", "low", "medium", "high", "max". Maps to provider's extended thinking parameter.</summary>
    public string ReasoningEffort { get; set; } = "medium";

    /// <summary>Context length limit in tokens. When exceeded, old messages are compressed via LLM.</summary>
    public int ContextLimit { get; set; } = 65536;

    /// <summary>Supplementary system prompt / configuration code added by user.</summary>
    public string? CustomConfigCode { get; set; }

    [JsonIgnore]
    public bool IsAnthropicProvider =>
        Provider.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) ||
        Provider.Contains("Claude", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsLocalProvider =>
        Provider.Contains("Local", StringComparison.OrdinalIgnoreCase) ||
        IsLocalBaseUrl(BaseUrl);

    [JsonIgnore]
    public bool RequiresApiKey => !IsLocalProvider;

    public void Normalize()
    {
        Provider = NormalizeProvider(Provider);
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? (IsAnthropicProvider ? "https://api.anthropic.com" : "https://api.openai.com/v1")
            : BaseUrl.Trim();
        Model = string.IsNullOrWhiteSpace(Model)
            ? (IsAnthropicProvider ? "claude-sonnet-4-20250514" : "gpt-4o-mini")
            : Model.Trim();
        Temperature = Math.Clamp(Temperature, 0d, 2d);
        MaxTokens = Math.Clamp(MaxTokens <= 0 ? 4096 : MaxTokens, 1, 1_048_576);
        MaxToolCallRounds = Math.Clamp(MaxToolCallRounds <= 0 ? 8 : MaxToolCallRounds, 1, 64);
        ContextLimit = Math.Clamp(ContextLimit <= 0 ? 65536 : ContextLimit, 4096, 1_048_576);
        ReasoningEffort = NormalizeReasoningEffort(ReasoningEffort);

        // Ensure Models list exists and Model is included
        if (Models == null) Models = new();
        if (Models.Count == 0)
        {
            Models.Add(Model);
            ActiveModelIndex = 0;
        }
        else
        {
            // Keep ActiveModelIndex in bounds, pointing to the effective Model
            if (!Models.Contains(Model))
            {
                Models.Insert(0, Model);
                ActiveModelIndex = 0;
            }
            else
            {
                // Sync ActiveModelIndex to match the current Model
                var idx = Models.IndexOf(Model);
                if (idx >= 0) ActiveModelIndex = idx;
                if (ActiveModelIndex < 0) ActiveModelIndex = 0;
                if (ActiveModelIndex >= Models.Count) ActiveModelIndex = Models.Count - 1;
            }
        }
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "OpenAI Compatible";
        }

        if (provider.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("Claude", StringComparison.OrdinalIgnoreCase))
        {
            return "Anthropic Claude";
        }

        if (provider.Contains("Local", StringComparison.OrdinalIgnoreCase))
        {
            return "Local OpenAI Compatible";
        }

        return "OpenAI Compatible";
    }

    private static string NormalizeReasoningEffort(string? effort)
    {
        var normalized = effort?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "none" => "off",
            "off" => "off",
            "minimal" => "minimal",
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "max" => "max",
            _ => "medium"
        };
    }

    private static bool IsLocalBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.IsLoopback)
        {
            return string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
