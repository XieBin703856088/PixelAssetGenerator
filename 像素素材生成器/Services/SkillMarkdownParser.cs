using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// Parses SKILL.md files (Markdown + YAML frontmatter) into SkillDefinition objects.
/// Format (compatible with ECC SKILL.md):
///   ---
///   name: skill-name
///   displayName:
///     zh: 中文名
///     en: English Name
///   description:
///     zh: 中文描述
///     en: English description
///   category: BuiltIn
///   tags: [tag1, tag2]
///   kind: instructions
///   origin: ECC-inspired
///   ---
///   # Title
///   ## Step 1: Name
///   ...text...
///   ```lang
///   code
///   ```
///   **预期结果**: ...
/// </summary>
public static class SkillMarkdownParser
{
    /// <summary>Parse a SKILL.md file into a SkillDefinition.</summary>
    public static SkillDefinition? Parse(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var text = File.ReadAllText(filePath);
        return ParseText(text, filePath);
    }

    /// <summary>Parse SKILL.md text content into a SkillDefinition.</summary>
    public static SkillDefinition? ParseText(string text, string? sourcePath = null)
    {
        // Extract YAML frontmatter between --- markers
        var frontmatter = ExtractFrontmatter(text);
        if (frontmatter == null) return null;

        var body = ExtractBody(text);

        var id = GetFrontmatterValue(frontmatter, "name");
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Normalize: skill name → ID
        var skillId = "builtin_" + id.Trim().ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Parse display names
        string displayNameZh = "", displayNameEn = "";
        var displayNameBlock = GetFrontmatterBlock(frontmatter, "displayName");
        if (displayNameBlock != null)
        {
            displayNameZh = GetNestedValue(displayNameBlock, "zh") ?? id;
            displayNameEn = GetNestedValue(displayNameBlock, "en") ?? id;
        }

        // Parse descriptions
        string descriptionZh = "", descriptionEn = "";
        var descBlock = GetFrontmatterBlock(frontmatter, "description");
        if (descBlock != null)
        {
            descriptionZh = GetNestedValue(descBlock, "zh") ?? "";
            descriptionEn = GetNestedValue(descBlock, "en") ?? "";
        }
        else
        {
            var flatDesc = ParseYamlValue(frontmatter, "description");
            if (flatDesc != null)
            {
                descriptionZh = flatDesc;
                descriptionEn = flatDesc;
            }
        }

        var category = ParseYamlValue(frontmatter, "category") ?? "BuiltIn";
        var kind = ParseYamlValue(frontmatter, "kind") ?? "instructions";
        var tags = ParseYamlList(frontmatter, "tags");

        // Parse steps from body (## headings as step boundaries)
        var steps = ParseSteps(body);

        return new SkillDefinition
        {
            Id = skillId,
            Name = id,
            DisplayNameZh = displayNameZh,
            DisplayNameEn = displayNameEn,
            DescriptionZh = descriptionZh,
            DescriptionEn = descriptionEn,
            Description = string.IsNullOrEmpty(descriptionZh) ? (descriptionEn ?? id) : descriptionZh,
            Category = category,
            Kind = kind,
            Tags = tags,
            Steps = steps,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── Frontmatter extraction ──

    private static string? ExtractFrontmatter(string text)
    {
        var match = Regex.Match(text, @"^---\s*\n(.*?)\n---", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExtractBody(string text)
    {
        var match = Regex.Match(text, @"^---\s*\n.*?\n---\s*\n(.*)", RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);
        if (match.Success) return match.Groups[1].Value.Trim();

        // Fallback: skip first --- block
        var idx = text.IndexOf("---", StringComparison.Ordinal);
        if (idx >= 0)
        {
            idx = text.IndexOf("---", idx + 3, StringComparison.Ordinal);
            if (idx >= 0)
                return text[(idx + 3)..].Trim();
        }
        return text.Trim();
    }

    private static string? GetFrontmatterValue(string frontmatter, string key)
    {
        // Match key: value (single line)
        var match = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
        if (match.Success)
        {
            var val = match.Groups[1].Value.Trim();
            // Strip quotes
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
                val = val[1..^1].Replace("\\\"", "\"");
            // Strip brackets for list values
            if (val.StartsWith('[') && val.EndsWith(']'))
            {
                // Return the raw bracket content for list parsing
                return val;
            }
            return val;
        }
        return null;
    }

    private static string? GetFrontmatterBlock(string frontmatter, string key)
    {
        // Match multi-line block: key:\n  subkey: value\n  subkey2: value2
        var match = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}\s*:\s*\n((?:\s+.*\n?)*)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? GetNestedValue(string block, string key)
    {
        var match = Regex.Match(block, $@"^{Regex.Escape(key)}\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
        if (match.Success)
        {
            var val = match.Groups[1].Value.Trim();
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
                val = val[1..^1];
            return val;
        }
        return null;
    }

    private static string? ParseYamlValue(string frontmatter, string key)
    {
        // First try single line
        var val = GetFrontmatterValue(frontmatter, key);
        if (val != null && !val.StartsWith('[')) return val;

        // Try quoted multi-word value
        var qMatch = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}\s*:\s*""(.+?)""\s*$", RegexOptions.Multiline);
        return qMatch.Success ? qMatch.Groups[1].Value : val;
    }

    private static List<string> ParseYamlList(string frontmatter, string key)
    {
        var raw = GetFrontmatterValue(frontmatter, key);
        if (raw == null) return new List<string>();

        var result = new List<string>();
        // Format: [tag1, tag2, tag3]
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var inner = raw[1..^1];
            foreach (var item in inner.Split(','))
            {
                var t = item.Trim().Trim('"').Trim('\'');
                if (!string.IsNullOrEmpty(t))
                    result.Add(t);
            }
        }
        return result;
    }

    // ── Step parsing ──

    /// <summary>
    /// Parse steps from the Markdown body.
    /// Steps are delimited by ## (level-2 headings).
    /// Each step may contain: description text, code blocks (```), and "预期结果" annotations.
    /// </summary>
    private static List<SkillStep> ParseSteps(string body)
    {
        var steps = new List<SkillStep>();

        // Split by ## headings
        var sections = Regex.Split(body, @"(^|\n)##\s+").ToList();
        // sections[0] is typically the document title (# Title) or preamble — skip it
        // Each subsequent even+odd pair: sections[i] = heading text, sections[i+1] = body text

        // Simplified: iterate split result
        // After splitting by "## ", we get fragments:
        // [0] = everything before first ## (title + preamble)
        // [1] = first step heading text
        // [2] = first step body
        // [3] = second step heading text
        // [4] = second step body, etc.
        for (int i = 1; i + 1 < sections.Count; i += 2)
        {
            var heading = sections[i].Trim();
            var content = i + 1 < sections.Count ? sections[i + 1].Trim() : "";

            // Remove trailing ## (if any) from next heading accidentally included
            var headingEnd = heading.IndexOf('\n');
            if (headingEnd > 0) heading = heading[..headingEnd].Trim();

            if (string.IsNullOrWhiteSpace(heading)) continue;

            // Skip the document title (# Title) — not a step
            if (i == 1 && heading == sections[0].TrimStart('#').Trim())
                continue;

            var step = new SkillStep
            {
                Title = heading,
                Instructions = ExtractInstructions(content),
                Code = ExtractCode(content),
                CodeLanguage = ExtractCodeLanguage(content),
                ExpectedResult = ExtractExpectedResult(content)
            };

            steps.Add(step);
        }

        // Fallback: if no ## headings found, treat whole body as single step
        if (steps.Count == 0 && !string.IsNullOrWhiteSpace(body))
        {
            // Extract first # as title
            var titleMatch = Regex.Match(body, @"^#\s+(.+)$", RegexOptions.Multiline);
            steps.Add(new SkillStep
            {
                Title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "步骤",
                Instructions = ExtractInstructions(body),
                Code = ExtractCode(body),
                CodeLanguage = ExtractCodeLanguage(body),
                ExpectedResult = ExtractExpectedResult(body)
            });
        }

        return steps;
    }

    private static string ExtractInstructions(string content)
    {
        // Remove code blocks and expected result markers
        var text = Regex.Replace(content, @"```[\s\S]*?```", "");
        text = Regex.Replace(text, @"\*\*预期结果\*\*[\s\S]*", "");
        return text.Trim();
    }

    private static string? ExtractCode(string content)
    {
        var match = Regex.Match(content, @"```(\w*)\s*\n([\s\S]*?)```");
        return match.Success ? match.Groups[2].Value.Trim() : null;
    }

    private static string? ExtractCodeLanguage(string content)
    {
        var match = Regex.Match(content, @"```(\w+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractExpectedResult(string content)
    {
        var match = Regex.Match(content, @"\*\*预期结果\*\*:\s*(.+?)(?:\n|$)");
        if (match.Success) return match.Groups[1].Value.Trim();

        var match2 = Regex.Match(content, @"\*\*预期结果\*\*\s*(.+?)(?:\n|$)");
        return match2.Success ? match2.Groups[1].Value.Trim() : null;
    }
}
