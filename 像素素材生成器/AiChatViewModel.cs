using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using PixelAssetGenerator.Services.Localization;

namespace PixelAssetGenerator;

/// <summary>
/// 工具调用的单步状态，显示在对话气泡中。
/// 类似 Claude Code 的 tool call 展示：工具名 + spinner/图标 + 参数摘要 + 结果。
/// </summary>
public sealed class ToolCallStepViewModel : INotifyPropertyChanged
{
    public string ToolName { get; }
    public string ArgumentsPreview { get; }
    public string CallId { get; }

    private string _status = "pending"; // pending, running, success, error, cancelled
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(IsSpinning)); }
    }

    private string _result = "";
    public string Result
    {
        get => _result;
        set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResultPreview)); }
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    // 耗时跟踪
    private DateTime _startedAt;
    public DateTime StartedAt
    {
        get => _startedAt;
        set { _startedAt = value; OnPropertyChanged(); }
    }

    public string StatusIcon => _status switch
    {
        "pending" => "⏳",
        "running" => "⟳",
        "success" => "✓",
        "error" => "✗",
        "cancelled" => "—",
        _ => "?"
    };

    public string StatusColor => _status switch
    {
        "pending" => "#888",
        "running" => "#E6B450",
        "success" => "#4CAF50",
        "error" => "#E07070",
        "cancelled" => "#888",
        _ => "#888"
    };

    public bool IsSpinning => _status == "running" || _status == "pending";

    public string ResultPreview
    {
        get
        {
            if (string.IsNullOrEmpty(_result)) return "";
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(_result);
                var root = doc.RootElement;

                // 根元素是数组（如 node_library 返回的列表）→ 显示条目数
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                    return $"{root.GetArrayLength()} items";

                // 根元素是对象 → 尝试解析 success/error
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return Truncate(_result, 80);

                var success = root.TryGetProperty("success", out var s) ? s.GetBoolean() : false;
                if (success)
                {
                    if (root.TryGetProperty("message", out var msg))
                        return Truncate(msg.GetString() ?? "Done", 80);
                    if (root.TryGetProperty("node_id", out var nid))
                        return $"Node #{nid.GetInt32()}";
                    if (root.TryGetProperty("count", out var cnt))
                        return $"{cnt.GetInt32()} items";
                    return "Done";
                }
                if (root.TryGetProperty("error", out var err))
                    return $"Error: {Truncate(err.GetString() ?? "?", 80)}";
                return Truncate(_result, 80);
            }
            catch
            {
                return Truncate(_result, 80);
            }
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public ToolCallStepViewModel(string toolName, string argsPreview, string callId)
    {
        ToolName = toolName;
        ArgumentsPreview = argsPreview;
        CallId = callId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 代表一组工具调用的消息气泡，包含多个 ToolCallStepViewModel。
/// 显示在对话中，如同一轮的多个并行工具调用。
/// </summary>
public sealed class ToolCallGroupViewModel : INotifyPropertyChanged
{
    private readonly ILocalizationService _loc;

    public string Role => "tool_call_group";

    /// <summary>本轮所有工具调用</summary>
    public ObservableCollection<ToolCallStepViewModel> Steps { get; } = new();

    /// <summary>总体状态：running / success / error</summary>
    private string _groupStatus = "running";
    public string GroupStatus
    {
        get => _groupStatus;
        set { _groupStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); OnPropertyChanged(nameof(StatusColor)); }
    }

    public string StatusIcon => _groupStatus switch
    {
        "success" => "✓",
        "error" => "✗",
        "running" => "⟳",
        _ => "?"
    };

    public string StatusColor => _groupStatus switch
    {
        "success" => "#4CAF50",
        "error" => "#E07070",
        "running" => "#E6B450",
        _ => "#888"
    };

    public string Summary
    {
        get
        {
            int total = Steps.Count;
            int done = 0, err = 0;
            foreach (var s in Steps)
            {
                if (s.Status == "success") done++;
                else if (s.Status == "error") err++;
            }
            if (_groupStatus == "running")
                return $"Running: {done + err + 1}/{total}";
            return err > 0 ? $"{done} done, {err} failed" : $"{done}/{total} completed";
        }
    }

    public ToolCallGroupViewModel()
    {
        _loc = Services.ServiceLocator.TryGetService<ILocalizationService>()
            ?? Services.Localization.LocalizationService.Instance;
        GroupStatus = "running";
    }

    /// <summary>添加一个工具步骤并返回</summary>
    public ToolCallStepViewModel AddStep(string toolName, string argsPreview, string callId)
    {
        var step = new ToolCallStepViewModel(toolName, argsPreview, callId);
        Steps.Add(step);
        OnPropertyChanged(nameof(Summary));
        return step;
    }

    /// <summary>检查组内所有步骤是否已结束，更新组状态</summary>
    public void CheckAndUpdateGroupStatus()
    {
        bool allDone = true;
        bool anyError = false;
        foreach (var s in Steps)
        {
            if (s.Status == "running" || s.Status == "pending")
            {
                allDone = false;
                break;
            }
            if (s.Status == "error") anyError = true;
        }
        if (allDone)
            GroupStatus = anyError ? "error" : "success";
        OnPropertyChanged(nameof(Summary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 单个聊天消息的 ViewModel。支持流式追加、限速刷新、推理折叠。
/// </summary>
public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Set once at app startup to enable DI for ChatMessageViewModel without constructor injection.
    /// All ChatMessageViewModel instances share this reference.
    /// </summary>
    internal static ILocalizationService? LocService;

    public string Role { get; }
    // 使用 StringBuilder 存储内容，避免 += 拼接时的 O(n²) 内存分配
    private readonly StringBuilder _contentBuilder;
    private bool _isCollapsed;
    private bool _isStreaming;

    // 缓存已计算的 CollapsedSummary，避免每次 Content 变更时重新全文扫描
    private string? _cachedCollapsedSummary;

    // --- 限速刷新：token写入buffer，timer批量flush到UI ---
    private readonly StringBuilder _pendingBuffer = new();
    private DispatcherTimer? _flushTimer;

    // --- 推理自动折叠：流式完成后 2 秒自动折叠 ---
    private DispatcherTimer? _autoCollapseTimer;

    public string Content
    {
        get => _contentBuilder.ToString();
        set
        {
            _contentBuilder.Clear();
            _contentBuilder.Append(value);
            _cachedCollapsedSummary = null;
            OnPropertyChanged();
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming == value) return;
            _isStreaming = value;
            _cachedCollapsedSummary = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CollapsedSummary));
            OnPropertyChanged(nameof(ReasoningStatusText));

            // 推理消息流式完成后 1 秒自动折叠
            if (!value && IsReasoning)
            {
                // 先展开显示完成的内容
                IsCollapsed = false;
                ScheduleAutoCollapse();
            }
            else if (value && IsReasoning)
            {
                // 开始流式时取消待执行自动折叠
                CancelAutoCollapse();
            }
        }
    }

    /// <summary>1 秒后自动折叠推理消息（仅在流式完成且用户未手动操作时）</summary>
    private void ScheduleAutoCollapse()
    {
        CancelAutoCollapse();
        _autoCollapseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _autoCollapseTimer.Tick += (_, _) =>
        {
            _autoCollapseTimer?.Stop();
            _autoCollapseTimer = null;
            IsCollapsed = true;
        };
        _autoCollapseTimer.Start();
    }

    /// <summary>取消待执行的自动折叠</summary>
    private void CancelAutoCollapse()
    {
        _autoCollapseTimer?.Stop();
        _autoCollapseTimer = null;
    }

    public string? ToolCallSummary { get; set; }
    public bool IsReasoning { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed == value) return;
            _isCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleGlyph));
            OnPropertyChanged(nameof(ShowContent));
            OnPropertyChanged(nameof(ReasoningStatusText));
        }
    }

    private ILocalizationService GetLoc() => LocService ?? Services.Localization.LocalizationService.Instance;

    public string ToggleGlyph => IsCollapsed ? "▸" : "▾";
    public bool ShowContent => !IsReasoning || !IsCollapsed;
    public string DisplayRole => IsReasoning
        ? GetLoc().GetString("AI_ThinkingProcess")
        : Role;

    public string CollapsedSummary
    {
        get
        {
            if (!IsReasoning) return string.Empty;
            if (_cachedCollapsedSummary != null) return _cachedCollapsedSummary;

            if (_contentBuilder.Length == 0)
            {
                _cachedCollapsedSummary = IsStreaming
                    ? GetLoc().GetString("AI_GeneratingThoughts")
                    : GetLoc().GetString("AI_ClickToViewThoughts");
                return _cachedCollapsedSummary;
            }

            const int scanLimit = 128;
            const int maxLength = 84;
            var sb = new StringBuilder(Math.Min(scanLimit + 4, maxLength + 4));
            int len = _contentBuilder.Length;
            for (int i = 0; i < Math.Min(len, scanLimit); i++)
            {
                char c = _contentBuilder[i];
                if (c == '\r' || c == '\n') sb.Append(' ');
                else sb.Append(c);
            }
            var normalized = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _cachedCollapsedSummary = IsStreaming
                    ? GetLoc().GetString("AI_GeneratingThoughts")
                    : GetLoc().GetString("AI_ClickToViewThoughts");
                return _cachedCollapsedSummary;
            }
            _cachedCollapsedSummary = normalized.Length > maxLength
                ? normalized[..maxLength] + "…"
                : normalized;
            return _cachedCollapsedSummary;
        }
    }

    public string ReasoningStatusText => !IsReasoning
        ? string.Empty
        : IsStreaming ? GetLoc().GetString("AI_Thinking")
        : IsCollapsed ? GetLoc().GetString("AI_ClickExpand")
        : GetLoc().GetString("AI_ClickCollapse");

    public ChatMessageViewModel(string role, string content, bool isReasoning = false)
    {
        Role = role;
        _contentBuilder = new StringBuilder(content);
        IsReasoning = isReasoning;
        _isCollapsed = false; // 推理内容默认展开显示
    }

    /// <summary>
    /// 流式期间：仅写入内部缓冲，不触发PropertyChanged。
    /// 必须从UI线程调用（Dispatcher保证）。
    /// </summary>
    public void AppendBuffered(string delta)
    {
        _pendingBuffer.Append(delta);
    }

    /// <summary>
    /// 启动限速刷新Timer。intervalMs控制刷新频率（默认120ms以避免高频UI更新卡死）。
    /// 必须从UI线程调用。
    /// 使用 Background 优先级：高于 Normal 事件处理，避免完成事件被无限延迟
    /// </summary>
    public void StartFlushTimer(int intervalMs = 120)
    {
        if (_flushTimer != null) return;
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _flushTimer.Tick += (_, _) => FlushToContent();
        _flushTimer.Start();
    }

    /// <summary>
    /// 停止Timer并将缓冲区剩余内容全部flush到Content。
    /// 必须从UI线程调用。
    /// </summary>
    public void StopFlushTimer()
    {
        _flushTimer?.Stop();
        _flushTimer = null;
        FlushToContent();
    }

    /// <summary>
    /// 将待刷新缓冲追加到Content，触发一次PropertyChanged。
    /// 必须从UI线程调用。
    /// 优化：直接 Append 到 StringBuilder，无需分配整段历史字符串
    /// </summary>
    public void FlushToContent()
    {
        if (_pendingBuffer.Length == 0) return;

        _contentBuilder.Append(_pendingBuffer);
        _pendingBuffer.Clear();
        _cachedCollapsedSummary = null;

        OnPropertyChanged(nameof(Content));
    }

    /// <summary>原有Append保留以兼容非流式场景（如工具调用注释等）。</summary>
    public void Append(string delta)
    {
        _contentBuilder.Append(delta);
        _cachedCollapsedSummary = null;
        OnPropertyChanged(nameof(Content));
    }

    public void ToggleCollapsed()
    {
        if (!IsReasoning) return;
        // 用户手动切换时取消自动折叠，尊重用户意图
        CancelAutoCollapse();
        IsCollapsed = !IsCollapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


public sealed class AiChatViewModel : INotifyPropertyChanged
{
    private const int MaxVisibleMessages = 48;
    private const int TargetVisibleMessages = 36;

    private readonly ILocalizationService _loc;

    public AiChatViewModel() : this(Services.ServiceLocator.TryGetService<ILocalizationService>()
        ?? Services.Localization.LocalizationService.Instance) { }

    public AiChatViewModel(ILocalizationService localizationService)
    {
        _loc = localizationService;
        // Share the localization service with ChatMessageViewModel instances
        ChatMessageViewModel.LocService = localizationService;
    }

    public ObservableCollection<object> Messages { get; } = new();

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); }
    }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        set { _isProcessing = value; OnPropertyChanged(); }
    }

    private string? _statusText;
    public string StatusText
    {
        get => _statusText ?? _loc.GetString("AI_Ready");
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string? _liveStatusText;
    public string LiveStatusText
    {
        get => _liveStatusText ?? _loc.GetString("AI_Standby");
        set { _liveStatusText = value; OnPropertyChanged(); }
    }

    public void AddSystemMessage(string text)
    {
        AddMessage(new ChatMessageViewModel("system", text));
    }

    /// <summary>
    /// 添加或替换最后一条系统状态消息。避免过多旧状态堆积在聊天框中。
    /// </summary>
    public void AddOrUpdateStatusMessage(string text)
    {
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i] is ChatMessageViewModel msg && msg.Role == "system")
            {
                msg.Content = text;
                return;
            }
        }
        AddMessage(new ChatMessageViewModel("system", text));
    }

    public void AddUserMessage(string text)
    {
        AddMessage(new ChatMessageViewModel("user", text));
    }

    public ChatMessageViewModel AddAssistantMessage(string text)
    {
        var msg = new ChatMessageViewModel("AI", text) { IsStreaming = true };
        AddMessage(msg);
        return msg;
    }

    public ChatMessageViewModel AddReasoningMessage(string text)
    {
        var msg = new ChatMessageViewModel("thinking", text, isReasoning: true) { IsStreaming = true };
        AddMessage(msg);
        return msg;
    }

    public void AddErrorMessage(string text)
    {
        AddMessage(new ChatMessageViewModel("error", text));
    }

    /// <summary>
    /// 添加一组工具调用的状态气泡（类似 Claude Code 的 tool call display）。
    /// </summary>
    public ToolCallGroupViewModel AddToolCallGroup()
    {
        var group = new ToolCallGroupViewModel();
        AddMessage(group);
        return group;
    }

    public ChatMessageViewModel? LastAssistantMessage
    {
        get
        {
            for (int i = Messages.Count - 1; i >= 0; i--)
                if (Messages[i] is ChatMessageViewModel msg && msg.Role == "AI")
                    return msg;
            return null;
        }
    }

    public ChatMessageViewModel? LastReasoningMessage
    {
        get
        {
            for (int i = Messages.Count - 1; i >= 0; i--)
                if (Messages[i] is ChatMessageViewModel msg && msg.Role == "thinking")
                    return msg;
            return null;
        }
    }

    /// <summary>最后一组工具调用气泡（当前轮次）</summary>
    public ToolCallGroupViewModel? LastToolCallGroup
    {
        get
        {
            for (int i = Messages.Count - 1; i >= 0; i--)
                if (Messages[i] is ToolCallGroupViewModel group)
                    return group;
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void AddMessage(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Messages.Add(message);
        TrimVisibleMessages();
    }

    private void TrimVisibleMessages()
    {
        if (Messages.Count <= MaxVisibleMessages)
        {
            return;
        }

        var removeCount = Messages.Count - TargetVisibleMessages;
        while (removeCount > 0 && Messages.Count > 0)
        {
            if (Messages[0] is ChatMessageViewModel msg && msg.IsStreaming)
            {
                break;
            }

            Messages.RemoveAt(0);
            removeCount--;
        }
    }
}
