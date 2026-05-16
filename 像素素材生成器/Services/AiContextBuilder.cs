using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services;

// ── Build context ──────────────────────────────────────────────────────────────

/// <summary>
/// Carries all inputs required to build a system prompt for one agent session.
/// </summary>
public sealed record AgentBuildContext(
    IReadOnlyList<NodeLibraryItem> NodeLibraryItems,
    string? Intent = null,
    IReadOnlyList<SkillDoc>? ActiveSkills = null,
    AiPermissionMode PermissionMode = AiPermissionMode.Execute,
    string? CustomConfig = null
);

// ── Builder ────────────────────────────────────────────────────────────────────

/// <summary>
/// Single authoritative source for the agent system prompt.
/// One public method: <see cref="Build"/>.
/// All prompt sections are assembled in a fixed, Claude-style order:
///   Role → Workflow → Tools → Principles → Anti-loop → Node catalog → Skills → Custom config
/// </summary>
public sealed class AiContextBuilder
{
    /// <summary>
    /// Builds the complete system prompt from the given context.
    /// </summary>
    public string Build(AgentBuildContext ctx)
    {
        var sb = new StringBuilder(2048);

        AppendRole(sb);
        AppendWorkflow(sb);
        AppendTools(sb, ctx.PermissionMode);
        AppendPrinciples(sb);
        AppendAntiLoop(sb);
        AppendNodeCatalog(sb, ctx.NodeLibraryItems);

        if (ctx.ActiveSkills is { Count: > 0 })
            AppendSkills(sb, ctx.ActiveSkills);

        if (!string.IsNullOrWhiteSpace(ctx.CustomConfig))
            AppendCustomConfig(sb, ctx.CustomConfig!);

        return sb.ToString();
    }

    // ── Legacy compatibility shims ─────────────────────────────────────────────
    // Kept so existing code that instantiates AiContextBuilder with old constructor
    // arguments still compiles. Callers should migrate to Build(AgentBuildContext).

    public AiContextBuilder() { }

    public AiContextBuilder(
        IReadOnlyList<NodeLibraryItem>? library = null,
        object? graphToolProvider = null,
        object? fewShotSelector = null,
        object? userProfile = null)
    { }

    // Legacy property shims — kept for source compatibility, not used by Build()
    public bool UseCompactPrompt { get; set; }
    public string? CustomConfigCode { get; set; }
    public AiPermissionMode PermissionMode { get; set; } = AiPermissionMode.Execute;
    public void SetCurrentIntent(string? intent) { }

    // ── Private section builders ───────────────────────────────────────────────

    private static void AppendRole(StringBuilder sb)
    {
        sb.AppendLine("# 角色");
        sb.AppendLine("你是像素素材生成器的 AI 代理。你通过调用工具操作节点图来生成无缝纹理、像素图案和程序化材质。");
        sb.AppendLine("你只能通过工具与系统交互。你的知识边界：纹理生成、像素艺术、图案设计、节点图操作。");
        sb.AppendLine("所有回复使用简体中文。");
        sb.AppendLine();
    }

    private static void AppendWorkflow(StringBuilder sb)
    {
        sb.AppendLine("# 工作流程（计划驱动模式）");
        sb.AppendLine("系统已为你生成了一份分步执行计划。你的工作方式：");
        sb.AppendLine();
        sb.AppendLine("1. **读取当前步骤** — 查看注入的【当前任务】，这是你本轮唯一要完成的事");
        sb.AppendLine("2. **执行** — 调用必要的工具完成该步骤，可在同一轮调用多个工具");
        sb.AppendLine("3. **标记完成** — 工具执行完毕后，**必须调用 `update_plan` 并传入 `action=mark_complete`** 来标记当前步骤已完成");
        sb.AppendLine("4. **等待下一步** — 标记后系统会推进到下一步，你无需主动决定下一步是什么");
        sb.AppendLine();
        sb.AppendLine("> ### 📐 每个步骤的三段式操作规范");
        sb.AppendLine("> 一个完整的工作流步骤必须按顺序完成以下三个子操作：");
        sb.AppendLine("> ");
        sb.AppendLine("> **① 创建** — 用 `modify_nodes action=create` 创建该步骤所需的节点");
        sb.AppendLine("> **② 连接** — 用 `modify_connections action=connect` 将新节点连入已有节点图");
        sb.AppendLine("> **③ 调参** — 用 `set_parameter` 设置该步骤节点的主要参数");
        sb.AppendLine("> ");
        sb.AppendLine("> 示例：步骤「创建蜂窝节点并连接材质」的正确执行顺序：");
        sb.AppendLine("> `modify_nodes` create 蜂窝 → `modify_connections` 石板→蜂窝 → `set_parameter` 蜂窝 Size=32");
        sb.AppendLine("> **在同一步骤内优先用多轮调用完成上述三个操作，不要创建完就跳到下一步**。");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ **严格规则**：");
        sb.AppendLine("> - 每轮**只做当前步骤**，不要超前执行其他步骤");
        sb.AppendLine("> - 每轮结束时**必须调用 `update_plan action=mark_complete`**，否则计划无法推进");
        sb.AppendLine("> - 如果当前步骤无法完成，调用 `update_plan action=mark_failed reason=\"原因\"`");
        sb.AppendLine("> - 不要重复执行已完成的步骤");
        sb.AppendLine("> - **创建节点失败时（返回 Node type not found）**，应先调用 `query_info query_type=node_library`");
        sb.AppendLine(">   查看真实可用节点列表，选用名称最接近的替代节点重新尝试，而不是直接标记失败");
        sb.AppendLine();
    }

    private static void AppendTools(StringBuilder sb, AiPermissionMode permissionMode)
    {
        sb.AppendLine("# 工具");
        sb.AppendLine("| 工具 | 说明 |");
        sb.AppendLine("|------|------|");
        sb.AppendLine("| query_info | 查询画布状态（graph_summary）或节点库（node_library） |");
        sb.AppendLine("| modify_nodes | 创建 / 删除 / 移动节点（支持一次创建多个） |");
        sb.AppendLine("| modify_connections | 连接或断开节点 |");
        sb.AppendLine("| set_parameter | 设置节点参数值 |");
        sb.AppendLine("| aesthetic_eval | 评估当前输出图像的视觉质量并给出改进建议 |");
        sb.AppendLine("| use_skill / list_skills | 应用或查询已保存的技能模板 |");
        sb.AppendLine("| create_resource_node / delete_resource_node | 创建或删除自定义节点文件 |");
        sb.AppendLine("| **update_plan** | **标记当前步骤状态（每步必须调用）** action: mark_complete / mark_failed / report_blocker |");
        sb.AppendLine("| get_plan | 查询当前计划进度与步骤列表 |");
        sb.AppendLine();

        if (permissionMode == AiPermissionMode.SkipPermissions)
        {
            sb.AppendLine("> 所有操作均已预授权，可直接调用，无需额外说明。");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> 删除节点或文件等破坏性操作，请在调用工具前用一句话说明原因。");
            sb.AppendLine();
        }
    }

    private static void AppendPrinciples(StringBuilder sb)
    {
        sb.AppendLine("# 五条执行准则");
        sb.AppendLine("1. **完成即推进** — 工具返回 `success: true` 代表操作已完成，直接进入下一步，不要重复查询");
        sb.AppendLine("2. **三步流程** — 每个步骤必须依次完成：`modify_nodes`（创建节点）→ `modify_connections`（连接节点）→ `set_parameter`（设置参数）。不要创建完节点就跳到下一步");
        sb.AppendLine("3. **失败自愈** — 工具失败时分析原因。若是 `Node type not found`，立即调用" +
                       "`query_info query_type=node_library` 查真实节点名后重试；若是其他错误，修改参数或换用备选方案，绝不重试相同调用");
        sb.AppendLine("4. **同步执行** — 意图说明（如有）和工具调用写在同一条回复里；**不要先发一段文字再停下来等待**");
        sb.AppendLine("5. **清晰收尾** — 任务完成后输出中文总结（描述创建了什么、关键参数、效果），然后停止调用工具");
        sb.AppendLine();
    }

    private static void AppendAntiLoop(StringBuilder sb)
    {
        sb.AppendLine("# 防循环约束");
        sb.AppendLine("- 相同的工具 + 相同的参数组合**不得连续出现 2 次**");
        sb.AppendLine("- 若系统注入了循环警告（`[⚠️ 调用重复]` / `[⚠️ A-B-A-B循环]`），必须立即更换策略，不要继续重试");
        sb.AppendLine("- **参数设置饱和规则**：`set_parameter` 等参数类工具若已连续主导 3 轮以上，");
        sb.AppendLine("  必须停止参数调整，用一段文字总结已完成的操作，任务即告结束");
        sb.AppendLine("  （可以在结束语中说明效果是否满意，不要再调用任何工具）");
        sb.AppendLine("- 若连续 3 步没有实质进展，停止工具调用，用文字说明卡住的原因，请求用户帮助");
        sb.AppendLine();
    }

    private static void AppendNodeCatalog(StringBuilder sb, IReadOnlyList<NodeLibraryItem> library)
    {
        if (library == null || library.Count == 0) return;

        sb.AppendLine("# 节点速查");
        sb.AppendLine("以下列出所有可用节点。每个节点的格式为：");
        sb.AppendLine("  **名称** — 简短描述 | 输入→输出端口类型 | 能力标签");
        sb.AppendLine("你需要先用 `query_info query_type=node_library` 查看完整的参数详情。");
        sb.AppendLine();

        var grouped = library
            .GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.Append("## ").Append(group.Key).AppendLine();
            foreach (var item in group.OrderBy(i => i.Name))
            {
                // Node name
                sb.Append("- **").Append(item.Name).Append('*');

                // Description (first 100 chars)
                if (!string.IsNullOrEmpty(item.Description))
                {
                    var desc = item.Description.Length > 100
                        ? item.Description.AsSpan(0, 100).ToString() + "…"
                        : item.Description;
                    sb.Append(" — ").Append(desc);
                }

                // Port summary
                var inTypes = item.InputPorts.Count > 0
                    ? string.Join("/", item.InputPorts)
                    : "无输入";
                var outTypes = item.OutputPorts.Count > 0
                    ? string.Join("/", item.OutputPorts)
                    : "无输出";
                sb.Append(" | [").Append(inTypes).Append("→").Append(outTypes).Append(']');

                // Capability tags
                if (item.AiMetadata?.Capabilities is { Count: > 0 })
                {
                    sb.Append(" | `").Append(string.Join("`, `", item.AiMetadata.Capabilities)).Append('`');
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine("> 💡 **提示**：如果不确定用哪个节点，调用 `query_info query_type=node_library`");
        sb.AppendLine("> 可查看所有节点的完整参数、端口类型和默认值。也可以用自然语言描述需求。");
        sb.AppendLine();
    }

    private static void AppendSkills(StringBuilder sb, IReadOnlyList<SkillDoc> skills)
    {
        sb.AppendLine("# 激活的技能");
        sb.AppendLine("以下技能知识已注入，执行相关任务时请遵循：");
        sb.AppendLine();

        foreach (var skill in skills)
        {
            sb.Append("## ").AppendLine(skill.DisplayNameZh);
            if (!string.IsNullOrEmpty(skill.Description))
            {
                sb.Append("*").Append(skill.Description).AppendLine("*");
                sb.AppendLine();
            }
            sb.AppendLine(skill.Body);
            sb.AppendLine();
        }
    }

    private static void AppendCustomConfig(StringBuilder sb, string config)
    {
        sb.AppendLine("# 用户补充配置");
        sb.AppendLine(config);
        sb.AppendLine();
    }

    // ── Planning prompt ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the system prompt used in the planning phase (Phase 1).
    /// The model is asked to output ONLY a structured Markdown plan with checkboxes.
    /// No tools are provided in this call — the model must not call any.
    /// </summary>
    public string BuildPlanningPrompt(string intent, IReadOnlyList<NodeLibraryItem>? library = null)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("# 角色");
        sb.AppendLine("你是像素素材生成器的 AI 代理，专注于通过节点图生成无缝纹理和像素图案。");
        sb.AppendLine("所有回复使用简体中文。");
        sb.AppendLine();

        sb.AppendLine("# 当前任务");
        sb.AppendLine(intent);
        sb.AppendLine();

        sb.AppendLine("# 你的唯一职责");
        sb.AppendLine("分析任务，输出一份结构化的执行计划。");
        sb.AppendLine("**不要调用任何工具。不要执行任何操作。只输出计划。**");
        sb.AppendLine();

        sb.AppendLine("# 计划格式（严格遵守）");
        sb.AppendLine("你必须输出如下格式，不得添加任何额外内容：");
        sb.AppendLine();
        sb.AppendLine("[plan_start]");
        sb.AppendLine("## <计划标题>");
        sb.AppendLine("- [ ] 步骤一：<具体操作，包含节点 typeName 和参数要点>");
        sb.AppendLine("- [ ] 步骤二：<具体操作>");
        sb.AppendLine("- [ ] 步骤三：<...>");
        sb.AppendLine("[plan_end]");
        sb.AppendLine();
        sb.AppendLine("要求：");
        sb.AppendLine("- 输出必须以 [plan_start] 开头，以 [plan_end] 结尾，这两个标签不可省略");
        sb.AppendLine("- 步骤数量 3-8 个，每步聚焦一个原子操作（创建节点 / 连接节点 / 设置参数 / 评估效果）");
        sb.AppendLine("- 步骤描述具体，写明节点 typeName、连接关系或参数名");
        sb.AppendLine("- 不要在 [plan_start]/[plan_end] 之外输出任何内容（无问候语，无解释，无工具调用）");
        sb.AppendLine();

        if (library is { Count: > 0 })
        {
            sb.AppendLine("# 可用节点（按分类）");
            var grouped = library
                .GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                sb.Append("## ").Append(group.Key).AppendLine();
                foreach (var item in group.OrderBy(i => i.Name))
                {
                    sb.Append("- **").Append(item.Name).Append('*');
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        var desc = item.Description.Length > 80
                            ? item.Description.AsSpan(0, 80).ToString() + "…"
                            : item.Description;
                        sb.Append(" ").Append(desc);
                    }
                    if (item.AiMetadata?.Capabilities is { Count: > 0 })
                        sb.Append(" | `").Append(string.Join("`, `", item.AiMetadata.Capabilities)).Append('`');
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}