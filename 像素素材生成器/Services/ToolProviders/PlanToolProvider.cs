using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services.ToolProviders;

/// <summary>
/// 计划管理工具提供者。
/// 提供 update_plan 和 get_plan 两个工具，让 AI 可以更新和查询计划状态。
/// </summary>
public sealed class PlanToolProvider : IToolProvider
{
    private readonly PlanManager _planManager;

    public string ProviderName => "plan";

    public PlanToolProvider(PlanManager planManager)
    {
        _planManager = planManager;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return new ToolDefinition(
            "update_plan",
            "更新当前计划的步骤状态。每完成一个原子操作后必须调用此工具标记步骤完成，系统会自动推进到下一步。卡住时用 mark_failed 跳过当前步骤。",
            JsonSerializer.Deserialize<JsonElement>("""
                {
                  "type": "object",
                  "properties": {
                    "action": {
                      "type": "string",
                      "enum": ["mark_complete", "mark_failed", "report_blocker"],
                      "description": "mark_complete=当前步骤已完成, mark_failed=当前步骤失败请跳过, report_blocker=遇到阻塞但不想标记失败"
                    },
                    "reason": {
                      "type": "string",
                      "description": "失败或阻塞的原因说明（mark_failed/report_blocker 时必填）"
                    },
                    "note": {
                      "type": "string",
                      "description": "当前步骤的备注信息或执行细节说明（可选）"
                    }
                  },
                  "required": ["action"]
                }
                """)
        );

        yield return new ToolDefinition(
            "get_plan",
            "获取当前计划的完整状态：标题、进度、当前步骤描述、所有步骤列表。开始执行新操作前建议先查询此工具确认当前任务。",
            JsonSerializer.Deserialize<JsonElement>("""
                {
                  "type": "object",
                  "properties": {},
                  "required": []
                }
                """)
        );
    }

    public Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "update_plan" => Task.FromResult(ExecuteUpdatePlan(arguments)),
            "get_plan" => Task.FromResult(ExecuteGetPlan()),
            _ => Task.FromResult(
                new ToolResult($"{{\"success\":false,\"error\":\"Unknown plan tool: {toolName}\"}}", true)
                { IsUnhandled = true })
        };
    }

    private ToolResult ExecuteUpdatePlan(JsonElement args)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() : "";

        return action switch
        {
            "mark_complete" => MarkComplete(args),
            "mark_failed" => MarkFailed(args),
            "report_blocker" => ReportBlocker(args),
            _ => new ToolResult(
                $"{{\"success\":false,\"error\":\"Unknown update_plan action: {Escape(action ?? "")}\"}}",
                true)
        };
    }

    private ToolResult MarkComplete(JsonElement args)
    {
        var plan = _planManager.ActivePlan;
        if (plan == null)
            return new ToolResult("{\"success\":false,\"error\":\"No active plan\"}", true);

        var step = plan.CurrentStep;
        if (step == null)
        {
            // 所有步骤已完成
            return new ToolResult("{\"success\":true,\"message\":\"All steps completed\",\"allDone\":true}");
        }

        // 记录备注
        if (args.TryGetProperty("note", out var noteEl))
        {
            step.ToolCallIds.Add($"[note] {noteEl.GetString()}");
        }

        var (canContinue, nextStep) = _planManager.AdvanceToNextStep();

        var result = new
        {
            success = true,
            completedStep = step.Description,
            completedIndex = step.Index,
            hasNext = canContinue,
            nextStepDescription = nextStep?.Description ?? "",
            progress = plan.Progress,
            completedCount = plan.CompletedCount,
            totalSteps = plan.Steps.Count,
            currentPhase = plan.CurrentPhaseName,
            allDone = !canContinue
        };

        return new ToolResult(JsonSerializer.Serialize(result));
    }

    private ToolResult MarkFailed(JsonElement args)
    {
        var plan = _planManager.ActivePlan;
        if (plan == null)
            return new ToolResult("{\"success\":false,\"error\":\"No active plan\"}", true);

        var reason = args.TryGetProperty("reason", out var r) ? r.GetString() : "unspecified";
        var step = plan.CurrentStep;

        if (step == null)
        {
            return new ToolResult("{\"success\":true,\"message\":\"No current step to fail\"}");
        }

        var (canContinue, nextStep) = _planManager.FailCurrentStep(reason ?? "unspecified");

        var result = new
        {
            success = true,
            failedStep = step.Description,
            failedReason = reason,
            hasNext = canContinue,
            nextStepDescription = nextStep?.Description ?? "",
            progress = plan.Progress,
            currentPhase = plan.CurrentPhaseName,
            allDone = !canContinue
        };

        return new ToolResult(JsonSerializer.Serialize(result));
    }

    private ToolResult ReportBlocker(JsonElement args)
    {
        var plan = _planManager.ActivePlan;
        if (plan == null)
            return new ToolResult("{\"success\":false,\"error\":\"No active plan\"}", true);

        var reason = args.TryGetProperty("reason", out var r) ? r.GetString() : "unspecified blocker";
        var note = args.TryGetProperty("note", out var n) ? n.GetString() : "";

        var step = plan.CurrentStep;
        if (step != null)
        {
            step.ToolCallIds.Add($"[blocker: {reason}] {note}");
        }

        // 记录一次停滞
        _planManager.RecordStagnation();

        var result = new
        {
            success = true,
            message = $"Blocker recorded: {reason}",
            suggestion = "Try a different approach or use mark_failed to skip this step."
        };

        return new ToolResult(JsonSerializer.Serialize(result));
    }

    private ToolResult ExecuteGetPlan()
    {
        var plan = _planManager.ActivePlan;
        if (plan == null)
        {
            return new ToolResult("{\"success\":true,\"hasPlan\":false,\"message\":\"No active plan\"}");
        }

        var steps = plan.Steps.Select(s => new
        {
            index = s.Index,
            description = s.Description,
            status = s.Status.ToString().ToLowerInvariant(),
            phase = ActivePlan.PhaseDisplayName(s.Phase)
        }).ToList();

        var result = new
        {
            success = true,
            hasPlan = true,
            title = plan.Title,
            status = plan.Status.ToString().ToLowerInvariant(),
            currentStepIndex = plan.CurrentStepIndex,
            currentStepDescription = plan.CurrentStep?.Description ?? "",
            currentPhase = plan.CurrentPhaseName,
            progress = plan.Progress,
            completedCount = plan.CompletedCount,
            totalSteps = plan.Steps.Count,
            stagnationCount = plan.StagnationCount,
            steps
        };

        return new ToolResult(JsonSerializer.Serialize(result));
    }

    private static string Escape(string s) => AiHelpers.Escape(s);
}
