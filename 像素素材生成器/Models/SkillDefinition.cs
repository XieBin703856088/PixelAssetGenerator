using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PixelAssetGenerator.Models;

/// <summary>Parameter exposed by a skill.</summary>
public sealed class SkillParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Node ID within the skill graph that this parameter maps to.</summary>
    public string TargetNodeId { get; set; } = "";
    /// <summary>Parameter name on the target node.</summary>
    public string TargetParamName { get; set; } = "";
    /// <summary>Default value as JSON token.</summary>
    public JsonElement? DefaultValue { get; set; }
}

/// <summary>
/// A skill is a reusable capability — either a sub-graph (node connections) or
/// a set of step-by-step instructions for AI execution (e.g. CLI commands, Python scripts, workflows).
/// </summary>
public sealed class SkillDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Skill kind: "graph" (node connections) or "instructions" (step-by-step AI guidance).
    /// </summary>
    public string Kind { get; set; } = "graph";

    public string Category { get; set; } = "My Skills";
    public List<SkillParameter> Parameters { get; set; } = new();

    /// <summary>For graph skills: serialized node graph JSON.</summary>
    public string SerializedGraph { get; set; } = "";

    /// <summary>For instruction skills: step-by-step guidance for the AI to follow.</summary>
    public List<SkillStep> Steps { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Whether this skill is enabled (visible in library and usable by AI).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Built-in skills cannot be deleted by the user.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBuiltIn { get; set; }
    /// <summary>Tags for AI search matching.</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>Multi-locale display names.</summary>
    public string DisplayNameZh { get; set; } = "";
    public string DisplayNameEn { get; set; } = "";
    /// <summary>Multi-locale descriptions.</summary>
    public string DescriptionZh { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
}

/// <summary>A single step in an instruction-based skill.</summary>
public sealed class SkillStep
{
    /// <summary>Step title, e.g. "创建节点文件".</summary>
    public string Title { get; set; } = "";
    /// <summary>Detailed instructions for the AI to execute.</summary>
    public string Instructions { get; set; } = "";
    /// <summary>Optional code/command block to execute.</summary>
    public string? Code { get; set; }
    /// <summary>Language of the code block: "shell", "python", "csharp", "json", or null.</summary>
    public string? CodeLanguage { get; set; }
    /// <summary>Expected result or success criteria.</summary>
    public string? ExpectedResult { get; set; }
}
