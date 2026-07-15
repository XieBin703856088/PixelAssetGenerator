using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Utilities;

namespace PixelAssetGenerator.Services;

public sealed class PixelAgentService
{
    private readonly AiService _aiService;
    private readonly AiToolService _toolService;
    private readonly AiContextBuilder _contextBuilder;
    private readonly ConversationHistoryManager _historyManager;
    private readonly AiSettings _settings;
    private readonly PlanManager _planManager;
    private AiPermissionMode _permissionMode;
    private IReadOnlyList<NodeLibraryItem> _nodeLibrary = Array.Empty<NodeLibraryItem>();
    // 跟踪每步已调用的工具类型，判断是否有新进展
    private readonly HashSet<string> _stepToolCalls = new(StringComparer.OrdinalIgnoreCase);
    private int _lastStepIndex = -1;
    private int _stepRoundCount; // 当前步骤的连续轮次数

    /// <summary>可在每次发送前动态切换权限模式。</summary>
    public AiPermissionMode PermissionMode
    {
        get => _permissionMode;
        set => _permissionMode = value;
    }

    private IReadOnlyList<SkillDoc>? _activeSkills;
    public void SetActiveSkills(IReadOnlyList<SkillDoc>? skills) => _activeSkills = skills;
    public void SetNodeLibrary(IReadOnlyList<NodeLibraryItem>? library) => _nodeLibrary = library ?? Array.Empty<NodeLibraryItem>();

    public bool IsProcessing { get; private set; }

    public event Action<string>? OnTextDelta;
    public event Action<string>? OnReasoningDelta;
    public event Action<string>? OnStatusMessage;
    public event Action<string>? OnLiveStatus;
    public event Action? OnDone;
    public event Action<string>? OnError;
    public event Action? OnStreamRoundEnd;
    public event Action<string, string>? OnToolResult;
    public event Action<(string CallId, string ToolName, string ArgsPreview)[]>? OnToolGroupStarted;
    public event Action<string, string, string>? OnToolStepChanged;
    public event Func<string, string, Task<bool>>? OnConfirmRequired;
    public event Action<string, string>? OnPlanDetected;
    public event Action<string>? OnPhaseChanged;

    public PixelAgentService(
        AiService ai,
        AiToolService tool,
        AiContextBuilder ctx,
        ConversationHistoryManager hist,
        AiSettings settings,
        PlanManager? planManager = null,
        AiPermissionMode permissionMode = AiPermissionMode.Execute,
        object? unused1 = null)
    {
        _aiService = ai;
        _toolService = tool;
        _contextBuilder = ctx;
        _historyManager = hist;
        _settings = settings;
        _planManager = planManager ?? new PlanManager();
        _permissionMode = permissionMode;
    }

    public IReadOnlyList<ChatMessage> GetHistory() => _historyManager.Messages;

    private static string PlansDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PixelAssetGenerator", "Plans");

    /// <summary>
    /// 项目目录下的 Plans 文件夹（用于放置用户可见的MD计划文件）。
    /// </summary>
    private static string ProjectPlansDirectory =>
        System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Plans");

    private CancellationTokenSource? _cts;
    public void CancelStreaming() => _cts?.Cancel();

    public async Task SendMessageAsync(
        string msg,
        IReadOnlyList<AiFileAttachment>? attachments = null,
        CancellationToken ct = default)
    {
        if (IsProcessing) return;
        IsProcessing = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = _cts.Token;

        var sessionTag = Guid.NewGuid().ToString("N")[..6];
        int stepCount = 0;
        const int MaxSteps = 40;
        string executionPrompt = string.Empty;

        void Log(string cat, string detail)
        {
            var line = $"[AI:{sessionTag}][{cat}][step={stepCount}] {detail}";
            Debug.WriteLine(line);
            OnStatusMessage?.Invoke(line);
        }

        try
        {
            Log("START", $"用户消息: {msg.Truncate(80)}");
            _historyManager.AddUserMessage(msg);

            // ── 阶段 0: 前置查询（Research） ──
            // AI 先查询项目架构文档和节点库，了解可用工具后再生成计划
            Log("RESEARCH", "开始前置查询...");
            OnPhaseChanged?.Invoke("Researching...");

            var researchPrompt = BuildResearchPrompt(msg);
            // 研究阶段提供工具定义（核心工具集即可），让AI能真正查询节点详情
            // 注意：传空列表而不是null，否则API不会提供工具
            var researchTools = _toolService.GetCoreToolSchemas();
            var researchRequest = new AiChatRequest(
                _settings,
                researchPrompt,
                _historyManager.ToAiMessages(),
                researchTools);

            var researchBuilder = new StringBuilder();
            var researchToolCalls = new List<AiToolCallData>();

            await foreach (var ev in _aiService.ChatStreamAsync(researchRequest, ct))
            {
                switch (ev)
                {
                    case AiTextDelta td:
                        researchBuilder.Append(td.Delta);
                        OnTextDelta?.Invoke(td.Delta);
                        break;
                    case AiReasoningDelta rd:
                        OnReasoningDelta?.Invoke(rd.Delta);
                        break;
                    case AiToolCallBatch batch:
                        researchToolCalls.AddRange(batch.Calls);
                        break;
                    case AiError err:
                        Log("ERROR", err.Message);
                        OnError?.Invoke(err.Message);
                        goto endLoop;
                    case AiStatus st:
                        OnLiveStatus?.Invoke(st.Message);
                        break;
                }
            }
            OnStreamRoundEnd?.Invoke();

            // 执行研究阶段的工具调用并记录结果
            if (researchToolCalls.Count > 0)
            {
                Log("RESEARCH", $"执行 {researchToolCalls.Count} 个研究工具调用");
                OnToolGroupStarted?.Invoke(researchToolCalls.Select(c => (c.Id, c.FunctionName, PreviewArgs(c.FunctionName, c.Arguments))).ToArray());
                var researchResults = await ExecuteToolsAsync(researchToolCalls, ct);
                foreach (var tc in researchToolCalls)
                {
                    if (researchResults.TryGetValue(tc.Id, out var res))
                        _historyManager.AddToolResult(tc.Id, tc.FunctionName, Sanitize(tc.Arguments), res);
                }
            }

            var researchText = researchBuilder.ToString();
            Log("RESEARCH", $"前置查询完成: {researchText.Length} 字符");
            _historyManager.AddAssistantMessage(researchText, researchToolCalls.Count > 0
                ? researchToolCalls.Select(t => new ChatToolCall { Id = t.Id, FunctionName = t.FunctionName, Arguments = t.Arguments }).ToList()
                : null, null);

            // 将研究摘要保存到 PlanManager（后续写入MD文件）
            _planManager.ResearchContext = researchText;

            // ── 阶段 1: 生成计划 ──
            Log("PLAN", "开始生成计划...");
            OnPhaseChanged?.Invoke("Planning...");

            var planRequest = new AiChatRequest(
                _settings,
                _contextBuilder.BuildPlanningPrompt(msg, _nodeLibrary),
                _historyManager.ToAiMessages(),
                null);

            var planTextBuilder = new StringBuilder();
            await foreach (var ev in _aiService.ChatStreamAsync(planRequest, ct))
            {
                switch (ev)
                {
                    case AiTextDelta td:
                        planTextBuilder.Append(td.Delta);
                        OnTextDelta?.Invoke(td.Delta);
                        break;
                    case AiReasoningDelta rd:
                        OnReasoningDelta?.Invoke(rd.Delta);
                        break;
                    case AiError err:
                        Log("ERROR", err.Message);
                        OnError?.Invoke(err.Message);
                        goto endLoop;
                    case AiStatus st:
                        OnLiveStatus?.Invoke(st.Message);
                        break;
                }
            }
            OnStreamRoundEnd?.Invoke();

            var planText = planTextBuilder.ToString();
            Log("PLAN", $"计划文本 {planText.Length} 字符");
            _historyManager.AddAssistantMessage(planText, null, null);

            var activePlan = _planManager.CreateFromMarkdown(planText);
            if (activePlan == null || activePlan.Steps.Count == 0)
            {
                Log("WARN", "计划解析失败或步骤为空，退回自由模式执行");
                await ExecuteFreeFormAsync(msg, sessionTag, stepCount, MaxSteps, ct);
                goto endLoop;
            }

            Log("PLAN", $"计划已解析: \"{activePlan.Title}\" — {activePlan.Steps.Count} 步");
            OnPlanDetected?.Invoke(activePlan.Title, planText);

            // 设置计划文件自动保存路径（同时保存JSON和MD版本）
            try
            {
                System.IO.Directory.CreateDirectory(PlansDirectory);
                System.IO.Directory.CreateDirectory(ProjectPlansDirectory);
                var planFileName = $"plan_{DateTime.Now:yyyyMMdd_HHmmss}_{sessionTag}";
                _planManager.AutoSavePath = System.IO.Path.Combine(PlansDirectory, planFileName + ".json");
                _planManager.MdPlanFilePath = System.IO.Path.Combine(ProjectPlansDirectory, planFileName + ".md");
                Log("PLAN", $"计划MD文件路径: {_planManager.MdPlanFilePath}");
            }
            catch (Exception ex)
            {
                Log("PLAN", $"设置计划持久化失败: {ex.Message}");
            }

            // 首次保存MD计划文件
            try { _planManager.SavePlanAsMarkdown(); }
            catch (Exception ex) { Log("PLAN", $"MD初始保存失败: {ex.Message}"); }

            _planManager.StartExecution();
            OnPhaseChanged?.Invoke("Executing...");

            executionPrompt = _contextBuilder.Build(new AgentBuildContext(
                NodeLibraryItems: _nodeLibrary,
                Intent: msg,
                ActiveSkills: _activeSkills,
                PermissionMode: _permissionMode));

            var toolSchemas = _toolService.GetToolSchemas();

            while (!ct.IsCancellationRequested && stepCount < MaxSteps && _planManager.HasActivePlan)
            {
                var plan = _planManager.ActivePlan;
                if (plan == null) break;
                stepCount++;
                var currentStep = plan.CurrentStep;
                if (currentStep == null) break;

                // 如果步骤变了，重置工具调用跟踪
                if (currentStep.Index != _lastStepIndex)
                {
                    _stepToolCalls.Clear();
                    _lastStepIndex = currentStep.Index;
                    _stepRoundCount = 0;
                }
                _stepRoundCount++;

                Log("STEP", $"执行步骤 {currentStep.Index + 1}: {currentStep.Description.Truncate(80)}");
                OnPhaseChanged?.Invoke($"步骤 {currentStep.Index + 1}/{plan.Steps.Count}");

                var stepInjection = _planManager.BuildStepInjection();
                var msgs = new List<AiMessage>(_historyManager.ToAiMessages());
                if (!string.IsNullOrEmpty(stepInjection))
                    msgs.Add(new AiMessage("user", stepInjection, null));

                var stepRequest = new AiChatRequest(_settings, executionPrompt, msgs, toolSchemas);
                var contentBuilder = new StringBuilder();
                var toolCalls = new List<AiToolCallData>();

                var sw = Stopwatch.StartNew();
                await foreach (var ev in _aiService.ChatStreamAsync(stepRequest, ct))
                {
                    switch (ev)
                    {
                        case AiTextDelta td:
                            contentBuilder.Append(td.Delta);
                            OnTextDelta?.Invoke(td.Delta);
                            break;
                        case AiReasoningDelta rd:
                            OnReasoningDelta?.Invoke(rd.Delta);
                            break;
                        case AiToolCallBatch batch:
                            toolCalls.AddRange(batch.Calls);
                            break;
                        case AiError err:
                            Log("ERROR", err.Message);
                            OnError?.Invoke(err.Message);
                            goto endLoop;
                        case AiStatus st:
                            OnLiveStatus?.Invoke(st.Message);
                            break;
                    }
                }
                sw.Stop();
                OnStreamRoundEnd?.Invoke();

                var content = contentBuilder.Length > 0 ? contentBuilder.ToString() : null;
                var hasTools = toolCalls.Count > 0;
                Log("RESPONSE", $"耗时={sw.ElapsedMilliseconds}ms tools={hasTools}");

                var chatToolCalls = hasTools
                    ? toolCalls.Select(t => new ChatToolCall { Id = t.Id, FunctionName = t.FunctionName, Arguments = t.Arguments }).ToList()
                    : null;
                _historyManager.AddAssistantMessage(content, chatToolCalls, null);

                // AI 本轮没有工具调用 — 看作"步骤已完成"的信号，推进到下一步
                if (!hasTools)
                {
                    if (_planManager.IsPlanCompleted) break;
                    var (canContinue, _) = _planManager.AdvanceToNextStep();
                    if (!canContinue) break;
                    continue;
                }

                Log("TOOLS", $"{toolCalls.Count} 个: {string.Join(", ", toolCalls.Select(c => c.FunctionName))}");
                OnToolGroupStarted?.Invoke(toolCalls.Select(c => (c.Id, c.FunctionName, PreviewArgs(c.FunctionName, c.Arguments))).ToArray());

                // 记录执行前的步骤索引，用于检测 AI 是否调用了 update_plan mark_complete
                var stepIndexBefore = _planManager.ActivePlan?.CurrentStepIndex ?? -1;

                var execResults = await ExecuteToolsAsync(toolCalls, ct);
                foreach (var tc in toolCalls)
                {
                    if (!execResults.TryGetValue(tc.Id, out var res)) continue;
                    _historyManager.AddToolResult(tc.Id, tc.FunctionName, Sanitize(tc.Arguments), res);
                    Log("RESULT", $"{tc.FunctionName}: {(IsSuccess(res) ? "OK" : "FAIL")}");
                    // 记录成功的**变更型**工具类型（query_info 等只读操作不算进展）
                    if (IsSuccess(res) && tc.FunctionName is not ("query_info" or "get_plan" or "list_skills" or "list_recipes"))
                        _stepToolCalls.Add(tc.FunctionName);
                }

                if (_planManager.IsPlanCompleted)
                {
                    Log("DONE", "计划全部完成");
                    break;
                }

                // 检查是否有 Node type not found 或端口错误，注入修复提示并留在同一步
                bool anyNodeTypeError = execResults.Values.Any(r => r.Contains("Node type not found"));
                bool anyPortError = execResults.Values.Any(r => r.Contains("port index out of range"));
                if (anyNodeTypeError)
                {
                    Log("RETRY", "节点类型不存在，注入 node_library 查询提示让 AI 重试");
                    _historyManager.AddUserMessage(
                        "【系统提示】创建节点失败：指定的节点类型不存在。请先调用 `query_info query_type=node_library` " +
                        "查看当前可用的节点类型列表，然后从中选择最接近的节点类型重新创建。");
                    OnStreamRoundEnd?.Invoke();
                    continue;
                }
                if (anyPortError)
                {
                    Log("RETRY", "连接端口索引错误，注入 node_detail 查询提示让 AI 重试");
                    _historyManager.AddUserMessage(
                        "【系统提示】连接失败：目标节点的输入端口索引不正确。请先调用 `query_info query_type=node_detail param=\"节点类型名\"` " +
                        "查看目标节点的具体输入端口名称和索引，然后使用正确的端口索引重新连接。");
                    OnStreamRoundEnd?.Invoke();
                    continue;
                }

                // 若 AI 调用了 update_plan（步骤索引已变），已由 PlanToolProvider 推进，跳过下面的自动推进
                var stepIndexAfter = _planManager.ActivePlan?.CurrentStepIndex ?? -1;
                if (stepIndexAfter != stepIndexBefore)
                {
                    // 步骤已由 update_plan 推进，继续循环
                    continue;
                }

                // 判断是否继续留在同一步还是推进：
                // 1. 无实质变更操作（_stepToolCalls 为空，全是 query_info）
                //    给 AI 1 轮缓冲：第一步通常是 query_info 看画布，立即跳过会导致永远没有实质性操作
                // 2. 当前步骤已完成的轮次 >= 3，强制推进
                // 3. 有实质进展但不足→留在同一步继续
                const int maxRoundsPerStep = 3;
                if (_stepToolCalls.Count == 0)
                {
                    if (_stepRoundCount < 2)
                    {
                        // 第一轮是只读调用（query_info 等），给第二轮机会执行实质性操作
                        Log("STAY", $"步骤 {stepIndexBefore + 1} 轮次 {_stepRoundCount}/2（仅只读调用），留在同一步让 AI 执行实际操作");
                        _historyManager.AddUserMessage(
                            "【系统提示】画布状态已查看。请在当前步骤继续执行实质性操作（创建节点、连接节点、设置参数等），不要仅停留在查询。");
                        OnStreamRoundEnd?.Invoke();
                        continue;
                    }
                    // 连续两轮只有只读调用 → 无实质进展，推进
                    Log("ADVANCE", $"步骤 {stepIndexBefore + 1} 连续 {_stepRoundCount} 轮仅有只读调用，无实质进展，推进到下一步");
                }
                else if (_stepRoundCount < maxRoundsPerStep)
                {
                    Log("STAY", $"步骤 {stepIndexBefore + 1} 轮次 {_stepRoundCount}/{maxRoundsPerStep}，留在同一步继续");
                    // 注入提示，引导 AI 在当前步骤完成剩余操作
                    if (_stepRoundCount == 1 && _stepToolCalls.Contains("modify_nodes"))
                    {
                        _historyManager.AddUserMessage(
                            "【系统提示】节点已创建。如果需要连接节点，请在当前步骤继续调用 modify_connections 完成连接。");
                    }
                    OnStreamRoundEnd?.Invoke();
                    continue;
                }
                else
                {
                    Log("ADVANCE", $"步骤 {stepIndexBefore + 1} 已完成 {_stepRoundCount} 轮，自动推进到下一步");
                }
                var (canAdvance, _) = _planManager.AdvanceToNextStep();
                if (!canAdvance)
                {
                    Log("DONE", "所有步骤已推进完毕");
                    break;
                }
            }

            if (_planManager.IsPlanCompleted && !ct.IsCancellationRequested)
            {
                Log("SUMMARY", "计划完成，生成总结");
                OnPhaseChanged?.Invoke("Summarizing...");

                // 使用专用summary提示，不含工具定义，确保AI不会调用工具
                var summaryPrompt = "# 角色\n" +
                    "你是像素素材生成器的 AI 代理。所有回复使用简体中文。\n\n" +
                    "# 当前任务\n" +
                    "计划已执行完毕，请总结成果。\n\n" +
                    "# 约束\n" +
                    "- **不要调用任何工具**\n" +
                    "- 只输出文本总结\n" +
                    "- 不要提及工具、不要输出JSON、不要输出代码块\n\n";

                var summaryMsgs = new List<AiMessage>(_historyManager.ToAiMessages())
                {
                    new AiMessage("user", _planManager.BuildSummaryInjection(), null)
                };
                var summaryReq = new AiChatRequest(_settings, summaryPrompt, summaryMsgs, null);
                var summaryText = new StringBuilder();

                // 总结阶段使用超时 CancellationTokenSource，避免 API 无响应导致永久卡死
                using var summaryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                summaryCts.CancelAfter(TimeSpan.FromSeconds(30));
                var summaryCt = summaryCts.Token;

                try
                {
                    await foreach (var ev in _aiService.ChatStreamAsync(summaryReq, summaryCt))
                    {
                        switch (ev)
                        {
                            case AiTextDelta td:
                                summaryText.Append(td.Delta);
                                OnTextDelta?.Invoke(td.Delta);
                                break;
                            case AiReasoningDelta rd:
                                OnReasoningDelta?.Invoke(rd.Delta);
                                break;
                            case AiStatus st:
                                OnLiveStatus?.Invoke(st.Message);
                                break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log("SUMMARY", "总结生成超时（30s），跳过");
                    summaryText.Append("（总结生成超时，请查看上述执行结果）");
                }
                OnStreamRoundEnd?.Invoke();
                _historyManager.AddAssistantMessage(summaryText.ToString(), null, null);
            }

            endLoop:
            if (stepCount >= MaxSteps)
                OnStatusMessage?.Invoke($"已达最大步数 ({MaxSteps})，自动结束");

            // 确保计划状态为Completed（如果是自动推进结束的）
            if (_planManager.HasActivePlan && !_planManager.IsPlanCompleted)
            {
                // 标记所有剩余步骤为跳过，计划为已结束
                var plan = _planManager.ActivePlan;
                if (plan != null)
                {
                    plan.Status = Models.PlanStatus.Completed;
                    plan.CompletedAt = DateTime.Now;
                    foreach (var step in plan.Steps)
                    {
                        if (step.Status is Models.StepStatus.Pending or Models.StepStatus.InProgress)
                            step.Status = Models.StepStatus.Skipped;
                    }
                }
            }

            // 生成最终执行报告MD文件（始终生成，无论计划状态如何）
            try { GenerateFinalReport(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AI][REPORT] 报告生成失败: {ex.Message}"); }

            OnPhaseChanged?.Invoke("Done");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke($"错误：{ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            OnDone?.Invoke();
        }
    }

    private async Task ExecuteFreeFormAsync(
        string originalMsg,
        string sessionTag,
        int initialStepCount,
        int maxSteps,
        CancellationToken ct)
    {
        var systemPrompt = _contextBuilder.Build(new AgentBuildContext(
            NodeLibraryItems: _nodeLibrary,
            Intent: originalMsg,
            ActiveSkills: _activeSkills,
            PermissionMode: _permissionMode));

        var toolSchemas = _toolService.GetToolSchemas();
        int stepCount = initialStepCount;

        void Log(string cat, string detail) =>
            OnStatusMessage?.Invoke($"[AI:{sessionTag}][{cat}][step={stepCount}] {detail}");

        while (!ct.IsCancellationRequested && stepCount < maxSteps)
        {
            stepCount++;
            var request = new AiChatRequest(_settings, systemPrompt, _historyManager.ToAiMessages(), toolSchemas);
            var contentBuilder = new StringBuilder();
            var toolCalls = new List<AiToolCallData>();

            await foreach (var ev in _aiService.ChatStreamAsync(request, ct))
            {
                switch (ev)
                {
                    case AiTextDelta td:
                        contentBuilder.Append(td.Delta);
                        OnTextDelta?.Invoke(td.Delta);
                        break;
                    case AiReasoningDelta rd:
                        OnReasoningDelta?.Invoke(rd.Delta);
                        break;
                    case AiToolCallBatch batch:
                        toolCalls.AddRange(batch.Calls);
                        break;
                    case AiError err:
                        Log("ERROR", err.Message);
                        OnError?.Invoke(err.Message);
                        return;
                    case AiStatus st:
                        OnLiveStatus?.Invoke(st.Message);
                        break;
                }
            }
            OnStreamRoundEnd?.Invoke();

            var content = contentBuilder.Length > 0 ? contentBuilder.ToString() : null;
            var hasTools = toolCalls.Count > 0;
            var chatToolCalls = hasTools
                ? toolCalls.Select(t => new ChatToolCall { Id = t.Id, FunctionName = t.FunctionName, Arguments = t.Arguments }).ToList()
                : null;
            _historyManager.AddAssistantMessage(content, chatToolCalls, null);

            if (!hasTools) break;

            Log("TOOLS", $"{toolCalls.Count} 个: {string.Join(", ", toolCalls.Select(c => c.FunctionName))}");
            OnToolGroupStarted?.Invoke(toolCalls.Select(c => (c.Id, c.FunctionName, PreviewArgs(c.FunctionName, c.Arguments))).ToArray());

            var execResults = await ExecuteToolsAsync(toolCalls, ct);
            foreach (var tc in toolCalls)
            {
                if (!execResults.TryGetValue(tc.Id, out var res)) continue;
                _historyManager.AddToolResult(tc.Id, tc.FunctionName, Sanitize(tc.Arguments), res);
                Log("RESULT", $"{tc.FunctionName}: {(IsSuccess(res) ? "OK" : "FAIL")}");
            }
        }
    }

    private async Task<Dictionary<string, string>> ExecuteToolsAsync(
        List<AiToolCallData> toolCalls,
        CancellationToken ct)
    {
        var results = new Dictionary<string, string>();
        var permitted = new List<AiToolCallData>();

        foreach (var tc in toolCalls)
        {
            // SkipPermissions 模式：跳过所有确认，直接放行
            if (_permissionMode == AiPermissionMode.SkipPermissions)
            {
                permitted.Add(tc);
                continue;
            }

            if (_sensitiveTools.TryGetValue(tc.FunctionName, out var rule))
            {
                try
                {
                    var el = JsonSerializer.Deserialize<JsonElement>(tc.Arguments);
                    if (rule.predicate == null || rule.predicate(el))
                    {
                        var desc = rule.describe(el);
                        bool ok = true;
                        if (OnConfirmRequired != null)
                            ok = await OnConfirmRequired.Invoke(tc.FunctionName, desc);
                        if (!ok)
                        {
                            results[tc.Id] = "{\"success\":false,\"error\":\"用户拒绝了此操作\"}";
                            OnToolStepChanged?.Invoke(tc.Id, "error", results[tc.Id]);
                            continue;
                        }
                    }
                }
                catch { }
            }
            permitted.Add(tc);
        }

        var pending = new Dictionary<string, string>();
        var tasks = permitted.Select(tc => Task.Run(async () =>
        {
            OnToolStepChanged?.Invoke(tc.Id, "running", "");
            try
            {
                var r = await _toolService.ExecuteToolAsync(tc.FunctionName, tc.Arguments, ct);
                lock (pending) { pending[tc.Id] = r; }
                OnToolStepChanged?.Invoke(tc.Id, IsSuccess(r) ? "success" : "error", r);
                OnToolResult?.Invoke(tc.FunctionName, r);
            }
            catch (Exception ex)
            {
                var e = $"{{\"success\":false,\"error\":\"{ex.GetType().Name}: {ex.Message}\"}}";
                lock (pending) { pending[tc.Id] = e; }
                OnToolStepChanged?.Invoke(tc.Id, "error", e);
                OnToolResult?.Invoke(tc.FunctionName, e);
            }
        }, ct)).ToList();

        await Task.WhenAll(tasks);
        foreach (var kv in pending) results[kv.Key] = kv.Value;
        return results;
    }

    private static readonly Dictionary<string, (Func<JsonElement, bool>? predicate, Func<JsonElement, string> describe)>
        _sensitiveTools = new()
        {
            ["modify_nodes"] = (
                a => a.TryGetProperty("action", out var x) && x.GetString() == "delete",
                _ => "删除节点"),
            ["modify_connections"] = (
                a => a.TryGetProperty("action", out var x) && x.GetString() == "disconnect",
                _ => "断开连接"),
        };

    public static void RegisterSensitiveTool(
        string name,
        Func<JsonElement, bool>? predicate,
        Func<JsonElement, string> describe)
        => _sensitiveTools[name] = (predicate, describe);

    public void ClearHistory() => _historyManager.Clear();
    public void SaveSession(string id, string title = "New Chat", string? planPath = null)
        => _historyManager.SaveSession(id, title, planPath);
    public bool LoadSessionById(string id) => _historyManager.LoadSessionById(id);
    public string? GetSessionPlanPath(string id) => _historyManager.GetSessionPlanPath(id);
    public void DeleteSession(string id) => _historyManager.DeleteSession(id);
    public IReadOnlyList<ChatSessionSummary> GetAllSessionSummaries()
        => _historyManager.GetAllSessionSummaries();

    private static bool IsSuccess(string r) => AiHelpers.IsSuccess(r);
    private static string Sanitize(string? j) => AiHelpers.Sanitize(j);
    private static string PreviewArgs(string name, string args) => AiHelpers.PreviewArgs(name, args);

    /// <summary>
    /// 构建前置查询阶段的提示词。
    /// 引导AI先调用工具查询节点库详情，了解节点真实参数后再输出调研报告。
    /// </summary>
    private string BuildResearchPrompt(string userIntent)
    {
        var sb = new System.Text.StringBuilder(1024);

        sb.AppendLine("# 角色");
        sb.AppendLine("你是像素素材生成器的 AI 代理。用户提出了一项需求，你需要先调研再执行。");
        sb.AppendLine("所有回复使用简体中文。");
        sb.AppendLine();

        sb.AppendLine("# 用户需求");
        sb.AppendLine(userIntent);
        sb.AppendLine();

        sb.AppendLine("# 前置调研（本阶段唯一任务）");
        sb.AppendLine("请按以下顺序操作：");
        sb.AppendLine();
        sb.AppendLine("1. **查询节点库** — 调用 `query_info query_type=node_library` 查看有哪些可用节点类型");
        sb.AppendLine("2. **查节点详情** — 对可能需要用到的节点，调用 `query_info query_type=node_detail param=\"节点名\"`");
        sb.AppendLine("   查看其端口类型、参数名、可选值、默认值。必须查清楚以下信息：");
        sb.AppendLine("   - 节点有哪些参数，参数名**确切叫什么**（如不是 patternScale 而是 Size 或 CellSize）");
        sb.AppendLine("   - Choice类型参数有哪些选项可选");
        sb.AppendLine("   - 端口索引和类型（确保连接时不会配错）");
        sb.AppendLine("3. **输出调研报告** — 基于以上查询结果，输出结构化的调研报告");
        sb.AppendLine();
        sb.AppendLine("**注意**：");
        sb.AppendLine("- 本阶段**可以调用工具**查询信息，但不要创建/连接/修改节点");
        sb.AppendLine("- 调研报告应包括：");
        sb.AppendLine("  - 需求分析摘要");
        sb.AppendLine("  - 计划使用的节点类型（typeName）及确切参数名");
        sb.AppendLine("  - 预期的节点图结构（节点间的连接关系和端口索引）");
        sb.AppendLine("  - 每个参数的具体设定值（基于查询到的真实参数名）");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// 生成最终执行报告并保存为MD文件。
    /// </summary>
    private void GenerateFinalReport()
    {
        try
        {
            var reportMd = _planManager.BuildReportMarkdown();
            System.IO.Directory.CreateDirectory(PlansDirectory);
            System.IO.Directory.CreateDirectory(ProjectPlansDirectory);
            var reportPath = System.IO.Path.Combine(ProjectPlansDirectory,
                $"report_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(reportPath, reportMd);
            Debug.WriteLine($"[AI][REPORT] 执行报告已保存: {reportPath}");

            // 同时更新MD计划文件为最终版本
            _planManager.SavePlanAsMarkdown();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AI][REPORT] 生成执行报告失败: {ex.Message}");
        }
    }

}
