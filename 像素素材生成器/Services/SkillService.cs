using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Manages skill definitions: CRUD, serialization, and discovery.
/// Supports two skill formats:
///   - SKILL.md (Markdown + YAML frontmatter) — for built-in instruction skills
///   - JSON file — for user-created graph/recipe skills
/// Built-in skills are loaded from Resources/Skills/*.skill.md on startup.
/// User skills are persisted as JSON in the app data skills directory.
/// </summary>
public sealed class SkillService
{
    private readonly string _userSkillsDir;
    private string _builtInSkillsDir;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private List<SkillDefinition> _skills = new();

    public SkillService()
    {
        _userSkillsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelAssetGenerator", "skills");
        Directory.CreateDirectory(_userSkillsDir);

        // Built-in skills are in Resources/Skills/ relative to the app base directory
        _builtInSkillsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Skills");

        LoadAll();
    }

    public IReadOnlyList<SkillDefinition> GetAll() => _skills;
    public IReadOnlyList<SkillDefinition> GetAllEnabled() => _skills.Where(s => s.Enabled).ToList();

    public SkillDefinition? GetById(string id)
        => _skills.FirstOrDefault(s => s.Id == id);

    public SkillDefinition? GetByName(string name)
        => _skills.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Save(SkillDefinition skill)
    {
        var existing = _skills.FindIndex(s => s.Id == skill.Id);
        if (existing >= 0)
            _skills[existing] = skill;
        else
            _skills.Add(skill);

        var path = Path.Combine(_userSkillsDir, $"{skill.Id}.json");
        var json = JsonSerializer.Serialize(skill, _jsonOptions);
        File.WriteAllText(path, json);
    }

    public bool Delete(string id)
    {
        var skill = GetById(id);
        if (skill == null) return false;
        if (skill.IsBuiltIn) return false;

        _skills.Remove(skill);
        var path = Path.Combine(_userSkillsDir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
        return true;
    }

    public SkillDefinition Create(string name, string description, string category,
        string? serializedGraph = null, List<SkillParameter>? parameters = null,
        List<string>? tags = null,
        string displayNameZh = "", string displayNameEn = "",
        string descriptionZh = "", string descriptionEn = "",
        string kind = "graph", List<SkillStep>? steps = null)
    {
        var skill = new SkillDefinition
        {
            Name = name,
            Description = description,
            Category = category ?? "My Skills",
            Kind = kind,
            SerializedGraph = serializedGraph ?? "",
            Steps = steps ?? new List<SkillStep>(),
            Parameters = parameters ?? new List<SkillParameter>(),
            CreatedAt = DateTime.UtcNow,
            Tags = tags ?? new List<string>(),
            DisplayNameZh = displayNameZh,
            DisplayNameEn = displayNameEn,
            DescriptionZh = descriptionZh,
            DescriptionEn = descriptionEn,
            Enabled = true
        };
        Save(skill);
        return skill;
    }

    private void LoadAll()
    {
        _skills.Clear();

        // 1. Load built-in skills from SKILL.md files
        LoadBuiltInSkills();

        // 2. Load user skills from JSON files
        LoadUserSkills();
    }

    private void LoadBuiltInSkills()
    {
        if (!Directory.Exists(_builtInSkillsDir))
        {
            // Fallback: try to find Resources/Skills relative to the project root
            var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "Skills");
            var resolvedFallback = Path.GetFullPath(fallback);
            if (Directory.Exists(resolvedFallback))
                _builtInSkillsDir = resolvedFallback;
            else
                return;
        }

        foreach (var file in Directory.GetFiles(_builtInSkillsDir, "*.skill.md"))
        {
            try
            {
                var skill = SkillMarkdownParser.Parse(file);
                if (skill != null)
                {
                    skill.IsBuiltIn = true;
                    skill.Enabled = true;
                    _skills.Add(skill);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"SkillService failed to load built-in skill '{file}': {ex.Message}");
            }
        }
    }

    private void LoadUserSkills()
    {
        if (!Directory.Exists(_userSkillsDir)) return;

        foreach (var file in Directory.GetFiles(_userSkillsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var skill = JsonSerializer.Deserialize<SkillDefinition>(json);
                if (skill != null && !_skills.Any(s => s.Id == skill.Id))
                    _skills.Add(skill);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"SkillService failed to load user skill file '{file}': {ex.Message}");
            }
        }
    }
}
