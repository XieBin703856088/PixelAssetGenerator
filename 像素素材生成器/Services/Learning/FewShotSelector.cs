using System.Text;

namespace PixelAssetGenerator.Services.Learning;

/// <summary>
/// Selects relevant past success cases from ExperienceDb and formats them
/// as few-shot examples for the AI system prompt.
/// </summary>
public sealed class FewShotSelector
{
    private readonly ExperienceDb _db;

    public FewShotSelector(ExperienceDb db)
    {
        _db = db;
    }

    /// <summary>
    /// Build a few-shot example block for the given user intent.
    /// Returns empty string if no relevant examples found.
    /// </summary>
    public string BuildExamplesBlock(string? userIntent, int maxExamples = 2)
    {
        if (string.IsNullOrWhiteSpace(userIntent))
            return "";

        var examples = _db.GetPositiveExamples(userIntent, maxExamples);
        if (examples.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 参考：过去做过的类似任务（供参考，不强制照搬）");

        foreach (var ex in examples.Take(maxExamples))
        {
            if (ex.Steps.Count == 0) continue;

            sb.AppendLine($"- 用户意图: \"{ex.UserIntent}\"");
            sb.Append("  操作: ");
            var stepDescriptions = ex.Steps.Select(s => $"{s.ToolName}({TruncateArgs(s.Arguments, 60)})");
            sb.AppendLine(string.Join(" → ", stepDescriptions));
            sb.AppendLine($"  结果: {(ex.Score > 0.5 ? "成功" : "部分有效")}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Format success rate info for capability awareness.</summary>
    public string BuildCapabilityBlock(string? userIntent)
    {
        if (string.IsNullOrWhiteSpace(userIntent))
            return "";

        var rate = _db.GetSuccessRate(userIntent);
        if (rate >= 0.7)
            return ""; // confident, no need to mention

        if (rate < 0.3)
            return $"\n注意：\"{userIntent}\" 类型的操作以往成功率较低({rate:P0})，建议谨慎或让用户手动操作关键步骤。\n";

        return $"\n注意：\"{userIntent}\" 类型的操作以往成功率约 {rate:P0}，仅供参考。\n";
    }

    private static string TruncateArgs(string args, int maxLen)
    {
        return args.Length <= maxLen ? args : args[..maxLen] + "...";
    }
}
