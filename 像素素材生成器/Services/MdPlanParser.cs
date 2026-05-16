using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>Parses MD task plans from AI responses, including layered (multi-phase) plans.</summary>
public static partial class MdPlanParser
{
    /// <summary>Extracts a task plan from AI response text (legacy flat format).</summary>
    public static MdTaskPlan? ExtractPlan(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = PlanBlockRegex().Match(text);
        if (!match.Success) return null;

        var plan = new MdTaskPlan { RawMarkdown = match.Value };
        var lines = match.Value.Split('\n');

        foreach (var line in lines)
        {
            // Title: ## heading
            if (line.StartsWith("## ") && string.IsNullOrEmpty(plan.Title))
            {
                plan.Title = line[3..].Trim();
                continue;
            }

            // Checkbox items
            var cbMatch = CheckboxRegex().Match(line);
            if (cbMatch.Success)
            {
                var isDone = cbMatch.Groups[1].Value == "x";
                var desc = cbMatch.Groups[2].Value.Trim();
                plan.Steps.Add(new MdTaskStep
                {
                    Description = desc,
                    Status = isDone ? "completed" : "pending"
                });
                continue;
            }

            // Numbered items
            var numMatch = NumberedRegex().Match(line);
            if (numMatch.Success)
            {
                plan.Steps.Add(new MdTaskStep
                {
                    Description = numMatch.Groups[1].Value.Trim(),
                    Status = "pending"
                });
                continue;
            }
        }

        return plan.Steps.Count > 0 ? plan : null;
    }

    /// <summary>
    /// Extracts a layered (multi-phase) task plan from AI response text.
    /// Detects phase headings like "### 阶段一：构图" or "### Phase 1: Composition"
    /// and groups steps by phase.
    /// </summary>
    public static LayeredPlan? ExtractLayeredPlan(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = PlanBlockRegex().Match(text);
        if (!match.Success) return null;

        var plan = new LayeredPlan { RawMarkdown = match.Value };
        var lines = match.Value.Split('\n');

        CreationPhase currentPhase = CreationPhase.General;

        foreach (var line in lines)
        {
            // Phase heading detection: "### 阶段一：构图" or "## 构图阶段" or "### Phase 1: Composition"
            var phaseMatch = PhaseHeadingRegex().Match(line);
            if (phaseMatch.Success)
            {
                currentPhase = ParsePhaseFromHeading(phaseMatch.Groups[1].Value);
                continue;
            }

            // Title: ## heading (first non-phase heading)
            if (line.StartsWith("## ") && string.IsNullOrEmpty(plan.Title))
            {
                // Check it's not a phase heading
                if (!PhaseHeadingRegex().IsMatch(line))
                    plan.Title = line[3..].Trim();
                continue;
            }

            // Checkbox items
            var cbMatch = CheckboxRegex().Match(line);
            if (cbMatch.Success)
            {
                var isDone = cbMatch.Groups[1].Value == "x";
                var desc = cbMatch.Groups[2].Value.Trim();
                plan.Steps.Add(new LayeredTaskStep
                {
                    Description = desc,
                    Status = isDone ? "completed" : "pending",
                    Phase = currentPhase
                });
                continue;
            }

            // Numbered items
            var numMatch = NumberedRegex().Match(line);
            if (numMatch.Success)
            {
                plan.Steps.Add(new LayeredTaskStep
                {
                    Description = numMatch.Groups[1].Value.Trim(),
                    Status = "pending",
                    Phase = currentPhase
                });
                continue;
            }
        }

        return plan.Steps.Count > 0 ? plan : null;
    }

    /// <summary>
    /// 提取 [plan_start]...[plan_end] 之间的纯 Markdown 内容（去掉标签行）。
    /// 如果找不到标签，则返回原始文本。
    /// </summary>
    public static string ExtractRawMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var match = PlanBlockRegex().Match(text);
        if (!match.Success) return text;

        var raw = match.Value;
        // 去掉首行 [plan_start] 和末行 [plan_end]
        var lines = raw.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim() == "[plan_start]")
            lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "[plan_end]")
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim();
    }

    /// <summary>将阶段标题文本解析为 CreationPhase 枚举</summary>
    private static CreationPhase ParsePhaseFromHeading(string headingText)
    {
        var lower = headingText.ToLowerInvariant().Trim();

        // 中文关键词
        if (lower.Contains("构图") || lower.Contains("布局") || lower.Contains("composition") || lower.Contains("布局"))
            return CreationPhase.Composition;
        if (lower.Contains("物件") || lower.Contains("对象") || lower.Contains("物体") || lower.Contains("元素")
            || lower.Contains("object") || lower.Contains("shape") || lower.Contains("图案"))
            return CreationPhase.Object;
        if (lower.Contains("材质") || lower.Contains("纹理") || lower.Contains("质感") || lower.Contains("颜色")
            || lower.Contains("material") || lower.Contains("texture") || lower.Contains("color"))
            return CreationPhase.Material;
        if (lower.Contains("调整") || lower.Contains("效果") || lower.Contains("光影") || lower.Contains("修正")
            || lower.Contains("adjust") || lower.Contains("effect") || lower.Contains("light") || lower.Contains("final"))
            return CreationPhase.Adjustment;

        // 数字索引
        if (lower.Contains("一") || lower.Contains("1") || lower.Contains("一") || lower.StartsWith("1"))
            return CreationPhase.Composition;
        if (lower.Contains("二") || lower.Contains("2") || lower.StartsWith("2"))
            return CreationPhase.Object;
        if (lower.Contains("三") || lower.Contains("3") || lower.StartsWith("3"))
            return CreationPhase.Material;
        if (lower.Contains("四") || lower.Contains("4") || lower.StartsWith("4"))
            return CreationPhase.Adjustment;

        return CreationPhase.General;
    }

    [GeneratedRegex(@"\[plan_start\][\s\S]*?\[plan_end\]", RegexOptions.Multiline)]
    private static partial Regex PlanBlockRegex();

    [GeneratedRegex(@"- \[([ x])\] (.+)")]
    private static partial Regex CheckboxRegex();

    [GeneratedRegex(@"^\d+\.\s+(.+)", RegexOptions.Multiline)]
    private static partial Regex NumberedRegex();

    /// <summary>匹配阶段标题行：### 阶段一：构图、## Phase 1: Composition、### 构图阶段</summary>
    [GeneratedRegex(@"^#{2,3}\s*(?:阶段|Phase|Stage|步骤)\s*[一二三四五六1-6]*[：:．.]*\s*(.+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PhaseHeadingRegex();
}
