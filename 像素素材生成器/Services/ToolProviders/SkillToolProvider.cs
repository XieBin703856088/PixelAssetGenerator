using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// IToolProvider implementation for skill-related operations.
/// </summary>
public sealed class SkillToolProvider : IToolProvider
{
    private readonly SkillService _skillService;

    /// <summary>
    /// Fired when the AI wants to use a skill.
    /// Parameters: (skillId, skillName, serializedGraph, x, y).
    /// The subscriber (UI layer) should deserialize and instantiate nodes on the canvas.
    /// </summary>
    public Func<string, string, string, double, double, Task<bool>>? OnUseSkill { get; set; }

    public string ProviderName => "skills";

    public SkillToolProvider(SkillService skillService)
    {
        _skillService = skillService;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "list_skills",
            "Get a brief list of all installed skills (name, description, category).",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{},"required":[]}
            """)
        );

        yield return new ToolDefinition(
            "get_skill_detail",
            "Get detailed information about a specified skill, including parameter definitions.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"skillId":{"type":"string","description":"Skill ID"}},"required":["skillId"]}
            """)
        );

        yield return new ToolDefinition(
            "create_skill",
            "Save the currently selected node group as a reusable skill.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"name":{"type":"string","description":"Skill name"},"description":{"type":"string","description":"Skill description"},"category":{"type":"string","description":"Skill category, e.g. Generate/Filter/My Skills"},"parameterMappings":{"type":"array","items":{"type":"object","properties":{"paramName":{"type":"string"},"targetNodeId":{"type":"string"},"targetParamName":{"type":"string"}}},"description":"Parameter mapping list, mapping skill parameters to internal node parameters."}},"required":["name","description"]}
            """)
        );

        yield return new ToolDefinition(
            "use_skill",
            "Instantiates a saved skill on the canvas (expands the skill sub-graph onto the current canvas).",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"skillId":{"type":"string","description":"Skill ID"},"x":{"type":"number","description":"X coordinate for placement"},"y":{"type":"number","description":"Y coordinate for placement"}},"required":["skillId"]}
            """)
        );

        yield return new ToolDefinition(
            "delete_skill",
            "Delete the specified skill.",
            JsonSerializer.Deserialize<JsonElement>("""
                {"type":"object","properties":{"skillId":{"type":"string","description":"Skill ID"}},"required":["skillId"]}
            """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "list_skills" => Task.FromResult(ListSkills()),
            "get_skill_detail" => Task.FromResult(GetSkillDetail(arguments)),
            "create_skill" => Task.FromResult(CreateSkill(arguments)),
            "use_skill" => UseSkillAsync(arguments),
            "delete_skill" => Task.FromResult(DeleteSkill(arguments)),
            _ => Task.FromResult(new ToolResult($"{{\"success\":false,\"error\":\"Unknown skill tool: {toolName}\"}}", true) { IsUnhandled = true })
        };
    }

    private ToolResult ListSkills()
    {
        var skills = _skillService.GetAllEnabled();
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < skills.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var s = skills[i];
            sb.Append($"{{\"id\":\"{Escape(s.Id)}\",\"name\":\"{Escape(s.Name)}\",\"description\":\"{Escape(s.Description)}\",\"category\":\"{Escape(s.Category)}\",\"kind\":\"{Escape(s.Kind)}\",\"parameterCount\":{s.Parameters.Count}}}");
        }
        sb.Append(']');
        return new ToolResult(sb.ToString());
    }

    private ToolResult GetSkillDetail(JsonElement args)
    {
        var skillId = args.TryGetProperty("skillId", out var id) ? id.GetString() : "";
        if (string.IsNullOrEmpty(skillId))
            return new ToolResult("{\"error\":\"Missing skillId parameter\"}", true);

        var skill = _skillService.GetById(skillId) ?? _skillService.GetByName(skillId);
        if (skill == null)
            return new ToolResult($"{{\"error\":\"Skill not found: {Escape(skillId)}\"}}", true);

        var sb = new StringBuilder();
        sb.Append($"{{\"id\":\"{Escape(skill.Id)}\",\"name\":\"{Escape(skill.Name)}\",\"description\":\"{Escape(skill.Description)}\",\"category\":\"{Escape(skill.Category)}\",\"kind\":\"{Escape(skill.Kind)}\"");
        sb.Append(",\"parameters\":[");
        for (int i = 0; i < skill.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = skill.Parameters[i];
            sb.Append($"{{\"name\":\"{Escape(p.Name)}\",\"description\":\"{Escape(p.Description)}\"}}");
        }
        sb.Append("],\"steps\":[");
        for (int i = 0; i < skill.Steps.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var step = skill.Steps[i];
            sb.Append($"{{\"title\":\"{Escape(step.Title)}\",\"instructions\":\"{Escape(step.Instructions)}\"");
            if (!string.IsNullOrEmpty(step.Code))
                sb.Append($",\"code\":\"{Escape(step.Code)}\",\"codeLanguage\":\"{Escape(step.CodeLanguage ?? "")}\"");
            if (!string.IsNullOrEmpty(step.ExpectedResult))
                sb.Append($",\"expectedResult\":\"{Escape(step.ExpectedResult)}\"");
            sb.Append("}");
        }
        sb.Append("]}");
        return new ToolResult(sb.ToString());
    }

    private ToolResult CreateSkill(JsonElement args)
    {
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : "";
        var description = args.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        var category = args.TryGetProperty("category", out var c) ? c.GetString() ?? "My Skills" : "My Skills";

        if (string.IsNullOrEmpty(name))
            return new ToolResult("{\"success\":false,\"error\":\"Missing name parameter\"}", true);

        var parameters = new List<SkillParameter>();
        if (args.TryGetProperty("parameterMappings", out var mappings))
        {
            foreach (var m in mappings.EnumerateArray())
            {
                parameters.Add(new SkillParameter
                {
                    Name = m.TryGetProperty("paramName", out var pn) ? pn.GetString() ?? "" : "",
                    TargetNodeId = m.TryGetProperty("targetNodeId", out var tn) ? tn.GetString() ?? "" : "",
                    TargetParamName = m.TryGetProperty("targetParamName", out var tp) ? tp.GetString() ?? "" : ""
                });
            }
        }

        _skillService.Create(name, description, category, "{}", parameters);
        return new ToolResult($"{{\"success\":true,\"skillName\":\"{Escape(name)}\"}}");
    }

    private async Task<ToolResult> UseSkillAsync(JsonElement args)
    {
        var skillId = args.TryGetProperty("skillId", out var id) ? id.GetString() : "";
        if (string.IsNullOrEmpty(skillId))
            return new ToolResult("{\"success\":false,\"error\":\"Missing skillId parameter\"}", true);

        var skill = _skillService.GetById(skillId) ?? _skillService.GetByName(skillId);
        if (skill == null)
            return new ToolResult($"{{\"success\":false,\"error\":\"Skill not found: {Escape(skillId)}\"}}", true);
        if (!skill.Enabled)
            return new ToolResult($"{{\"success\":false,\"error\":\"Skill '{Escape(skill.Name)}' is disabled.\"}}", true);

        // Try to instantiate the skill on the canvas via the callback (set by UI layer)
        if (OnUseSkill != null)
        {
            var x = args.TryGetProperty("x", out var xVal) ? xVal.GetDouble() : double.NaN;
            var y = args.TryGetProperty("y", out var yVal) ? yVal.GetDouble() : double.NaN;
            var success = await OnUseSkill(skill.Id, skill.Name, skill.SerializedGraph, x, y);

            if (success)
                return new ToolResult($"{{\"success\":true,\"skillName\":\"{Escape(skill.Name)}\",\"parameterCount\":{skill.Parameters.Count},\"note\":\"Skill has been instantiated on the canvas.\"}}");
            else
                return new ToolResult($"{{\"success\":false,\"error\":\"Skill instantiation failed: {Escape(skill.Name)}\"}}", true);
        }

        // Fallback when no UI subscriber
        return new ToolResult($"{{\"success\":true,\"skillName\":\"{Escape(skill.Name)}\",\"parameterCount\":{skill.Parameters.Count},\"note\":\"Skill loaded, complete instantiation in UI.\"}}");
    }

    private ToolResult DeleteSkill(JsonElement args)
    {
        var skillId = args.TryGetProperty("skillId", out var id) ? id.GetString() : "";
        if (string.IsNullOrEmpty(skillId))
            return new ToolResult("{\"success\":false,\"error\":\"Missing skillId parameter\"}", true);

        var success = _skillService.Delete(skillId);
        // Also try by name
        if (!success)
        {
            var skill = _skillService.GetByName(skillId);
            if (skill != null)
                success = _skillService.Delete(skill.Id);
        }

        return new ToolResult($"{{\"success\":{success.ToString().ToLower()}}}");
    }

    private static string Escape(string s) => AiHelpers.Escape(s);
}
