using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Represents a loaded skill document parsed from a .skill.md file.
/// </summary>
public sealed record SkillDoc(
    string Name,
    string DisplayNameZh,
    string Description,
    string Category,
    string Kind,
    IReadOnlyList<string> Tags,
    string Body
);

/// <summary>
/// Loads and caches skill documents from Resources/Skills/*.skill.md files.
/// Parses YAML frontmatter (name, displayName, description, category, kind, tags)
/// and exposes the markdown body for injection into system prompts.
/// </summary>
public sealed class SkillLoader
{
    private readonly string _skillsDir;
    private List<SkillDoc>? _cache;

    public SkillLoader()
    {
        _skillsDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Skills");
    }

    public SkillLoader(string skillsDir)
    {
        _skillsDir = skillsDir;
    }

    /// <summary>
    /// Returns all loaded skill documents. Loads from disk on first call, then caches.
    /// </summary>
    public IReadOnlyList<SkillDoc> GetAll()
    {
        if (_cache != null) return _cache;
        _cache = LoadAll();
        return _cache;
    }

    /// <summary>
    /// Returns skills whose tags overlap with any of the provided tags (case-insensitive).
    /// </summary>
    public IReadOnlyList<SkillDoc> FindByTags(IEnumerable<string> tags)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        return GetAll()
            .Where(s => s.Tags.Any(t => tagSet.Contains(t)))
            .ToList();
    }

    /// <summary>
    /// Returns skills matching the given kind (e.g. "instructions", "example").
    /// </summary>
    public IReadOnlyList<SkillDoc> FindByKind(string kind)
    {
        return GetAll()
            .Where(s => string.Equals(s.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Forces a reload from disk on the next GetAll() call.</summary>
    public void Invalidate() => _cache = null;

    // ── Private ──────────────────────────────────────────────────────────

    private List<SkillDoc> LoadAll()
    {
        var result = new List<SkillDoc>();
        if (!Directory.Exists(_skillsDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(_skillsDir, "*.skill.md", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var doc = ParseFile(file);
                if (doc != null)
                    result.Add(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkillLoader] Failed to parse {file}: {ex.Message}");
            }
        }
        return result;
    }

    private static SkillDoc? ParseFile(string path)
    {
        var text = File.ReadAllText(path, System.Text.Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Split frontmatter (--- ... ---) from body
        string frontmatter;
        string body;
        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) return null;
            frontmatter = text[3..end].Trim();
            body = text[(end + 4)..].TrimStart('\r', '\n');
        }
        else
        {
            // No frontmatter — use filename as name
            frontmatter = string.Empty;
            body = text;
        }

        // Parse YAML-like key: value pairs (flat only, no nested objects)
        var fm = ParseFrontmatter(frontmatter);

        var name = fm.GetValueOrDefault("name") ?? Path.GetFileNameWithoutExtension(path);
        var displayNameZh = ExtractLocalized(fm, "displayName", "zh") ?? name;
        var description = ExtractLocalized(fm, "description", "zh") ?? string.Empty;
        var category = fm.GetValueOrDefault("category") ?? "General";
        var kind = fm.GetValueOrDefault("kind") ?? "instructions";
        var tags = ParseTags(fm.GetValueOrDefault("tags") ?? string.Empty);

        return new SkillDoc(name, displayNameZh, description, category, kind, tags, body);
    }

    /// <summary>
    /// Parses a simple flat YAML-like frontmatter block into a string dictionary.
    /// Handles both plain values and indented sub-keys (e.g. displayName.zh).
    /// </summary>
    private static Dictionary<string, string> ParseFrontmatter(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;

        string? currentKey = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Indented sub-key (e.g. "  zh: value")
            if (line.StartsWith("  ", StringComparison.Ordinal) && currentKey != null)
            {
                var sub = line.Trim();
                var colonIdx = sub.IndexOf(':', StringComparison.Ordinal);
                if (colonIdx > 0)
                {
                    var subKey = $"{currentKey}.{sub[..colonIdx].Trim()}";
                    var subVal = sub[(colonIdx + 1)..].Trim();
                    result[subKey] = UnquoteYaml(subVal);
                }
                continue;
            }

            // Top-level key: value
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            currentKey = key;
            if (!string.IsNullOrEmpty(value))
                result[key] = UnquoteYaml(value);
        }
        return result;
    }

    /// <summary>Extracts a localized value like "displayName.zh" or falls back to "displayName".</summary>
    private static string? ExtractLocalized(Dictionary<string, string> fm, string key, string locale)
    {
        if (fm.TryGetValue($"{key}.{locale}", out var localized)) return localized;
        if (fm.TryGetValue(key, out var fallback)) return fallback;
        return null;
    }

    /// <summary>Parses a YAML array like [a, b, c] or multiline dash list into a list of strings.</summary>
    private static IReadOnlyList<string> ParseTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        // Inline array: [tag1, tag2]
        raw = raw.Trim();
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var inner = raw[1..^1];
            return inner.Split(',')
                .Select(t => t.Trim().Trim('"', '\''))
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
        }

        // Comma-separated plain string
        return raw.Split(',')
            .Select(t => t.Trim().Trim('"', '\'', '-'))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    private static string UnquoteYaml(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }
        return value;
    }
}
