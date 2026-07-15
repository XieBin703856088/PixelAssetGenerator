using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

/// <summary>
/// 计划生命周期管理器。
/// 负责创建计划、推进步骤、停滞检测、状态更新。
/// 所有修改通过事件通知 UI 和服务层。
/// </summary>
public sealed class PlanManager
{
    private ActivePlan? _activePlan;

    /// <summary>当前激活计划（可能为 null）</summary>
    public ActivePlan? ActivePlan => _activePlan;

    /// <summary>是否有正在执行的计划</summary>
    public bool HasActivePlan => _activePlan != null && _activePlan.Status == PlanStatus.Executing;

    /// <summary>计划是否为完成状态</summary>
    public bool IsPlanCompleted => _activePlan?.Status == PlanStatus.Completed;

    /// <summary>自动保存计划文件的路径（设置后每次计划状态变更都会保存）</summary>
    public string? AutoSavePath { get; set; }

    /// <summary>MD格式计划文件的路径。设置后会同步保存MD版本。</summary>
    public string? MdPlanFilePath { get; set; }

    /// <summary>前置查询发现的上下文摘要（注入到MD计划文件中）</summary>
    public string? ResearchContext { get; set; }

    // ── 事件 ──

    public event Action<ActivePlan>? OnPlanCreated;
    public event Action<ActivePlan>? OnPlanUpdated;
    public event Action<PlanStep?>? OnCurrentStepChanged;
    public event Action<string, string>? OnPhaseChanged;  // (oldPhase, newPhase)
    public event Action<int, int, string>? OnProgressChanged; // (completed, total, desc)
    public event Action<ActivePlan>? OnPlanCompleted;
    public event Action<string>? OnPlanAborted; // 中止原因

    // ── 停滞阈值（已禁用自动推进，仅用于BuildStagnationInjection） ──

    private void TriggerUpdated()
    {
        if (_activePlan == null) return;
        OnPlanUpdated?.Invoke(_activePlan);
        TryAutoSave();
    }

    private void TryAutoSave()
    {
        if (!string.IsNullOrEmpty(AutoSavePath))
        {
            try { SavePlan(AutoSavePath); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlanManager] AutoSave failed: {ex.Message}");
            }
        }
        // 同步保存MD文件
        try { SavePlanAsMarkdown(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlanManager] Md auto-save failed: {ex.Message}");
        }
    }
    public ActivePlan CreateFromLayeredPlan(LayeredPlan layeredPlan)
    {
        var plan = new ActivePlan
        {
            Title = layeredPlan.Title,
            RawMarkdown = layeredPlan.RawMarkdown,
            Status = PlanStatus.Draft
        };

        for (int i = 0; i < layeredPlan.Steps.Count; i++)
        {
            var ls = layeredPlan.Steps[i];
            plan.Steps.Add(new PlanStep
            {
                Index = i,
                Description = ls.Description,
                Status = ls.Status is "completed" or "failed" or "skipped"
                    ? ParseStepStatus(ls.Status) : StepStatus.Pending,
                Phase = (PlanCreationPhase)(int)ls.Phase,
                ToolCallIds = new List<string>(ls.ToolCallIds)
            });
        }

        _activePlan = plan;
        OnPlanCreated?.Invoke(plan);
        TryAutoSave();
        return plan;
    }

    /// <summary>从原始 Markdown 文本解析创建 ActivePlan</summary>
    public ActivePlan? CreateFromMarkdown(string rawMarkdown)
    {
        var layered = MdPlanParser.ExtractLayeredPlan(rawMarkdown);
        if (layered == null) return null;
        return CreateFromLayeredPlan(layered);
    }

    /// <summary>直接设置 ActivePlan（用于从会话恢复）</summary>
    public void SetActivePlan(ActivePlan plan)
    {
        _activePlan = plan;
        OnPlanCreated?.Invoke(plan);
    }

    /// <summary>开始执行计划：标记 Draft → Executing，设置第一步为 InProgress</summary>
    public void StartExecution()
    {
        if (_activePlan == null) return;
        _activePlan.Status = PlanStatus.Executing;
        _activePlan.StagnationCount = 0;

        if (_activePlan.Steps.Count > 0)
        {
            _activePlan.CurrentStepIndex = 0;
            _activePlan.Steps[0].Status = StepStatus.InProgress;
        }

        TriggerUpdated();
        OnCurrentStepChanged?.Invoke(_activePlan.CurrentStep);
        OnProgressChanged?.Invoke(_activePlan.CompletedCount, _activePlan.Steps.Count, "开始执行");
    }

    /// <summary>推进到下一步。标记当前步骤完成，找到下一个 Pending 步骤。</summary>
    /// <returns>(canContinue, nextStep) — canContinue=false 表示所有步骤已完成</returns>
    public (bool CanContinue, PlanStep? NextStep) AdvanceToNextStep()
    {
        if (_activePlan == null) return (false, null);

        // 标记当前步骤为完成
        if (_activePlan.CurrentStep != null)
        {
            _activePlan.CurrentStep.Status = StepStatus.Completed;
        }

        _activePlan.StagnationCount = 0;
        string oldPhase = _activePlan.CurrentPhaseName;

        // 找下一个 Pending 步骤
        int nextIndex = -1;
        for (int i = _activePlan.CurrentStepIndex + 1; i < _activePlan.Steps.Count; i++)
        {
            if (_activePlan.Steps[i].Status == StepStatus.Pending)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex >= 0)
        {
            _activePlan.CurrentStepIndex = nextIndex;
            _activePlan.Steps[nextIndex].Status = StepStatus.InProgress;
            TriggerUpdated();
            OnCurrentStepChanged?.Invoke(_activePlan.CurrentStep);

            string newPhase = _activePlan.CurrentPhaseName;
            if (oldPhase != newPhase)
                OnPhaseChanged?.Invoke(oldPhase, newPhase);

            OnProgressChanged?.Invoke(
                _activePlan.CompletedCount, _activePlan.Steps.Count,
                $"步骤 {nextIndex + 1}/{_activePlan.Steps.Count}");
            return (true, _activePlan.CurrentStep);
        }

        // 没有更多步骤 — 完成
        _activePlan.Status = PlanStatus.Completed;
        _activePlan.CompletedAt = DateTime.Now;
        _activePlan.CurrentStepIndex = -1;
        TriggerUpdated();
        OnCurrentStepChanged?.Invoke(null);
        OnProgressChanged?.Invoke(_activePlan.Steps.Count, _activePlan.Steps.Count, "全部完成");
        OnPlanCompleted?.Invoke(_activePlan);
        return (false, null);
    }

    /// <summary>标记当前步骤失败，自动推进到下一步。</summary>
    public (bool CanContinue, PlanStep? NextStep) FailCurrentStep(string reason)
    {
        if (_activePlan?.CurrentStep == null) return (false, null);

        _activePlan.CurrentStep.Status = StepStatus.Failed;
        _activePlan.CurrentStep.FailureReason = reason;
        _activePlan.StagnationCount = 0;

        TriggerUpdated();
        return AdvanceToNextStep();
    }

    /// <summary>记录一次停滞。仅记录计数供参考，不再自动推进。</summary>
    /// <returns>总是返回 0（不再做停滞检测）</returns>
    public int RecordStagnation()
    {
        if (_activePlan == null) return 0;
        _activePlan.StagnationCount++;
        TriggerUpdated();
        return 0;
    }

    /// <summary>更新某一步骤的状态（通过工具调用）</summary>
    public PlanStep? UpdateStepStatus(int stepIndex, StepStatus status, string? reason = null)
    {
        if (_activePlan == null || stepIndex < 0 || stepIndex >= _activePlan.Steps.Count)
            return null;

        var step = _activePlan.Steps[stepIndex];
        step.Status = status;
        if (reason != null)
            step.FailureReason = reason;

        TriggerUpdated();
        return step;
    }

    /// <summary>中止整个计划</summary>
    public void AbortPlan(string reason)
    {
        if (_activePlan == null) return;
        _activePlan.Status = PlanStatus.Aborted;
        _activePlan.StagnationCount = 0;

        if (_activePlan.CurrentStep != null)
            _activePlan.CurrentStep.Status = StepStatus.Skipped;

        TriggerUpdated();
        OnPlanAborted?.Invoke(reason);
    }

    /// <summary>重置计划</summary>
    public void Clear()
    {
        _activePlan = null;
    }

    // ── 注入提示构建 ──

    /// <summary>构建当前步骤的执行上下文提示（注入到对话历史中）</summary>
    public string? BuildStepInjection()
    {
        if (_activePlan?.CurrentStep == null) return null;

        var step = _activePlan.CurrentStep;
        return $"【当前任务】{step.Description}\n" +
               $"【当前阶段】{ActivePlan.PhaseDisplayName(step.Phase)}\n" +
               $"【进度】{_activePlan.CompletedCount}/{_activePlan.Steps.Count}\n" +
               $"【指令】\n" +
               $"1. 仅执行当前这一步，不要跳步骤。\n" +
               $"2. 如果本步骤包含**创建节点**，必须先用 `query_info query_type=node_detail param=\"节点类型名\"` 查询节点参数后，再创建。\n" +
               $"3. 完成后必须调用 `update_plan action=mark_complete` 标记完成。\n" +
               $"4. 如果创建节点时返回 `Node type not found`，先查节点库找到正确名称再重试。";
    }

    /// <summary>构建停滞提示</summary>
    public string? BuildStagnationInjection(int level)
    {
        if (_activePlan?.CurrentStep == null) return null;

        return level switch
        {
            1 => $"【提示】当前步骤执行遇到困难：" +
                  $"\"{_activePlan.CurrentStep.Description}\"\n" +
                  "如果确定无法完成，请调用 update_plan action=mark_failed 并提供原因，系统会处理后续步骤。",
            2 => $"【强制提示】当前步骤卡住较久。请立即调用 update_plan action=mark_failed " +
                  $"reason=\"...\"，不要继续尝试。",
            _ => null
        };
    }

    /// <summary>构建总结提示</summary>
    public string BuildSummaryInjection()
    {
        return "所有步骤已执行完毕。请总结成果，包括生成的纹理类型、使用的节点、关键参数。\n" +
               "**注意：不要调用任何工具。只输出文本总结。**";
    }

    // ── 持久化 ──

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    public void SavePlan(string filePath)
    {
        if (_activePlan == null) return;
        var json = JsonSerializer.Serialize(_activePlan, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 将当前计划生成为可读的MD格式文件（包含步骤、进度、节点信息）。
    /// 每次调用都会重新生成完整的MD文件，反映最新状态。
    /// </summary>
    public string BuildPlanMarkdown()
    {
        if (_activePlan == null) return "# 无活动计划\n";

        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(_activePlan.Title);
        sb.AppendLine();

        // 元数据
        sb.AppendLine("## 元数据");
        sb.AppendLine($"- **计划ID**: {_activePlan.Id}");
        sb.AppendLine($"- **状态**: {StatusDisplayName(_activePlan.Status)}");
        sb.AppendLine($"- **创建时间**: {_activePlan.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **进度**: {_activePlan.CompletedCount}/{_activePlan.Steps.Count} 步完成 ({_activePlan.Progress * 100:F0}%)");
        sb.AppendLine();

        // 前置研究摘要
        if (!string.IsNullOrEmpty(ResearchContext))
        {
            sb.AppendLine("## 前置调研");
            sb.AppendLine(ResearchContext);
            sb.AppendLine();
        }

        // 步骤列表
        sb.AppendLine("## 执行步骤");
        sb.AppendLine();

        foreach (var step in _activePlan.Steps)
        {
            string icon = step.Status switch
            {
                StepStatus.Completed => "- [x]",
                StepStatus.Failed => "- [x]",
                StepStatus.Skipped => "- [-]",
                StepStatus.InProgress => "- [→]",
                _ => "- [ ]"
            };
            string statusTag = step.Status switch
            {
                StepStatus.Completed => " ✅ 已完成",
                StepStatus.InProgress => " 🔄 执行中",
                StepStatus.Failed => $" ❌ 失败: {step.FailureReason ?? "未知原因"}",
                StepStatus.Skipped => " ⏭️ 已跳过",
                _ => ""
            };
            string phase = ActivePlan.PhaseDisplayName(step.Phase);
            sb.AppendLine($"{icon} **步骤 {step.Index + 1}** (阶段: {phase}){statusTag}");
            sb.AppendLine($"  {step.Description}");
            sb.AppendLine();
        }

        // 原始Markdown计划（如果存在，使用缩进代码块避免三反引号冲突）
        if (!string.IsNullOrEmpty(_activePlan.RawMarkdown))
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 原始AI计划");
            sb.AppendLine();
            foreach (var line in _activePlan.RawMarkdown.Split('\n'))
                sb.Append("    ").AppendLine(line);
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*最后更新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    /// <summary>
    /// 保存MD格式计划文件并同步更新JSON。
    /// </summary>
    public void SavePlanAsMarkdown()
    {
        if (_activePlan == null || string.IsNullOrEmpty(MdPlanFilePath)) return;
        try
        {
            var md = BuildPlanMarkdown();
            File.WriteAllText(MdPlanFilePath, md);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlanManager] SavePlanAsMarkdown failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成最终的执行报告MD文件。
    /// </summary>
    public string BuildReportMarkdown()
    {
        if (_activePlan == null) return "# 无计划报告\n";

        var sb = new StringBuilder();
        sb.AppendLine($"# 执行报告: {_activePlan.Title}");
        sb.AppendLine();
        sb.AppendLine("## 执行摘要");
        sb.AppendLine();
        sb.AppendLine($"| 项目 | 值 |");
        sb.AppendLine($"|------|-----|");
        sb.AppendLine($"| 计划ID | {_activePlan.Id} |");
        sb.AppendLine($"| 状态 | {StatusDisplayName(_activePlan.Status)} |");
        sb.AppendLine($"| 创建时间 | {_activePlan.CreatedAt:yyyy-MM-dd HH:mm:ss} |");
        sb.AppendLine($"| 完成时间 | {(_activePlan.CompletedAt.HasValue ? _activePlan.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "未完成")} |");
        sb.AppendLine($"| 总步骤 | {_activePlan.Steps.Count} |");
        sb.AppendLine($"| 完成 | {_activePlan.CompletedCount} |");
        sb.AppendLine($"| 失败 | {_activePlan.Steps.Count(s => s.Status == StepStatus.Failed)} |");
        sb.AppendLine($"| 跳过 | {_activePlan.Steps.Count(s => s.Status == StepStatus.Skipped)} |");
        sb.AppendLine($"| 进度 | {_activePlan.Progress * 100:F0}% |");
        sb.AppendLine();

        // 前置调研
        if (!string.IsNullOrEmpty(ResearchContext))
        {
            sb.AppendLine("## 前置调研");
            sb.AppendLine(ResearchContext);
            sb.AppendLine();
        }

        // 步骤详情
        sb.AppendLine("## 步骤详情");
        sb.AppendLine();
        foreach (var step in _activePlan.Steps)
        {
            string statusEmoji = step.Status switch
            {
                StepStatus.Completed => "✅",
                StepStatus.Failed => "❌",
                StepStatus.Skipped => "⏭️",
                StepStatus.InProgress => "🔄",
                _ => "⏳"
            };
            string phase = ActivePlan.PhaseDisplayName(step.Phase);
            sb.AppendLine($"### {statusEmoji} 步骤 {step.Index + 1}: {step.Description}");
            sb.AppendLine($"- **阶段**: {phase}");
            sb.AppendLine($"- **状态**: {StepStatusDisplayName(step.Status)}");
            if (step.FailureReason != null)
                sb.AppendLine($"- **失败原因**: {step.FailureReason}");
            if (step.ToolCallIds.Count > 0)
                sb.AppendLine($"- **工具调用**: {step.ToolCallIds.Count} 次");
            sb.AppendLine();
        }

        // 原始Markdown
        if (!string.IsNullOrEmpty(_activePlan.RawMarkdown))
        {
            sb.AppendLine("## 原始AI计划");
            sb.AppendLine();
            foreach (var line in _activePlan.RawMarkdown.Split('\n'))
                sb.Append("    ").AppendLine(line);
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*报告生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

        return sb.ToString();
    }

    public static ActivePlan? LoadPlan(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ActivePlan>(json, _jsonOptions);
    }

    // ── 帮助方法 ──

    private static string StatusDisplayName(PlanStatus status) => status switch
    {
        PlanStatus.Draft => "草稿",
        PlanStatus.Executing => "执行中",
        PlanStatus.Completed => "已完成",
        PlanStatus.Aborted => "已中止",
        PlanStatus.Stalled => "卡住",
        _ => "未知"
    };

    private static string StepStatusDisplayName(StepStatus status) => status switch
    {
        StepStatus.Pending => "等待中",
        StepStatus.InProgress => "执行中",
        StepStatus.Completed => "已完成",
        StepStatus.Failed => "失败",
        StepStatus.Skipped => "已跳过",
        _ => "未知"
    };

    private static StepStatus ParseStepStatus(string status) => status switch
    {
        "completed" => StepStatus.Completed,
        "failed" => StepStatus.Failed,
        "skipped" => StepStatus.Skipped,
        "in_progress" => StepStatus.InProgress,
        _ => StepStatus.Pending
    };
}
