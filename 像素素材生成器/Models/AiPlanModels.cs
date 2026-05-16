using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelAssetGenerator.Models;

/// <summary>计划整体状态</summary>
public enum PlanStatus
{
    Draft,      // AI 刚生成计划，尚未开始执行
    Executing,  // 正在按计划执行
    Completed,  // 所有步骤完成
    Aborted,    // 用户中止或 AI 报告无法完成
    Stalled     // AI 卡住，需要用户介入
}

/// <summary>单个步骤状态</summary>
public enum StepStatus
{
    Pending,     // 等待执行
    InProgress,  // 正在执行
    Completed,   // 已完成
    Failed,      // 失败
    Skipped      // 跳过
}

/// <summary>创作阶段：构图 → 物件 → 材质 → 调整</summary>
public enum PlanCreationPhase
{
    General,
    Composition,    // 构图
    Object,         // 物件
    Material,       // 材质
    Adjustment      // 调整
}

/// <summary>计划执行模式</summary>
public enum PlanExecutionMode
{
    PlanOnly,         // 只输出计划，不调用工具
    FreeForm,         // 自由模式（当前默认行为）
    PlanThenExecute,  // 生成计划 → 确认 → 按步骤执行
    AutoExecute       // 生成计划 → 自动执行
}

/// <summary>计划中的单个步骤</summary>
public sealed class PlanStep
{
    public int Index { get; set; }
    public string Description { get; set; } = "";
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public PlanCreationPhase Phase { get; set; } = PlanCreationPhase.General;
    public string? FailureReason { get; set; }
    public List<string> ToolCallIds { get; set; } = new();
}

/// <summary>激活中的计划</summary>
public sealed class ActivePlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public List<PlanStep> Steps { get; set; } = new();
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public int CurrentStepIndex { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public int StagnationCount { get; set; }
    public string RawMarkdown { get; set; } = "";

    /// <summary>当前步骤（可能为 null）</summary>
    public PlanStep? CurrentStep =>
        CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count
            ? Steps[CurrentStepIndex] : null;

    /// <summary>整体进度 (0.0 ~ 1.0)</summary>
    public double Progress
    {
        get
        {
            if (Steps.Count == 0) return 0;
            return (double)Steps.Count(s => s.Status
                is StepStatus.Completed or StepStatus.Failed or StepStatus.Skipped) / Steps.Count;
        }
    }

    /// <summary>已完成步骤数</summary>
    public int CompletedCount => Steps.Count(s => s.Status == StepStatus.Completed);

    /// <summary>当前阶段名称</summary>
    public string CurrentPhaseName
    {
        get
        {
            var step = CurrentStep;
            return step != null ? PhaseDisplayName(step.Phase) : "";
        }
    }

    /// <summary>按阶段分组的步骤</summary>
    public Dictionary<PlanCreationPhase, List<PlanStep>> StepsByPhase
    {
        get
        {
            var dict = new Dictionary<PlanCreationPhase, List<PlanStep>>();
            foreach (var step in Steps)
            {
                if (!dict.ContainsKey(step.Phase))
                    dict[step.Phase] = new List<PlanStep>();
                dict[step.Phase].Add(step);
            }
            return dict;
        }
    }

    public static string PhaseDisplayName(PlanCreationPhase phase) => phase switch
    {
        PlanCreationPhase.Composition => "构图",
        PlanCreationPhase.Object => "物件",
        PlanCreationPhase.Material => "材质",
        PlanCreationPhase.Adjustment => "调整",
        _ => "通用"
    };
}
