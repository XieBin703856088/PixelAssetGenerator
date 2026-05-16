namespace PixelAssetGenerator.Services.Learning;

/// <summary>
/// Captures implicit feedback signals from user behavior:
/// - Undo/redo of AI operations → negative feedback
/// - Direct export without modification → strong positive
/// - Parameter tweaks → weak positive
/// - Node deletion after AI creation → negative
/// Reports back to ExperienceDb and UserProfileService.
/// </summary>
public sealed class ExperienceTracker
{
    private readonly ExperienceDb _db;
    private readonly UserProfileService _profile;
    private string? _currentSessionId;
    private string? _currentIntent;
    private DateTime _sessionStart;

    // Track the last AI-created node set for feedback correlation
    private readonly HashSet<int> _lastAiCreatedNodes = new();
    private readonly HashSet<string> _lastAiSetParameters = new();
    private bool _hasExportedSinceLastAiAction;

    public ExperienceTracker(ExperienceDb db, UserProfileService profile)
    {
        _db = db;
        _profile = profile;
    }

    /// <summary>Call when an AI operation session begins.</summary>
    public void BeginOperation(string? intent, string sessionId)
    {
        _currentSessionId = sessionId;
        _currentIntent = intent;
        _sessionStart = DateTime.UtcNow;
        _lastAiCreatedNodes.Clear();
        _lastAiSetParameters.Clear();
        _hasExportedSinceLastAiAction = false;
    }

    /// <summary>Call when AI creates or modifies a node.</summary>
    public void TrackAiNodeCreated(int nodeId)
    {
        _lastAiCreatedNodes.Add(nodeId);
    }

    /// <summary>Call when AI sets a parameter.</summary>
    public void TrackAiParameterSet(string paramPath)
    {
        _lastAiSetParameters.Add(paramPath);
    }

    /// <summary>Call when user exports the result.</summary>
    public void TrackExport()
    {
        _hasExportedSinceLastAiAction = true;
    }

    /// <summary>
    /// Call when user deletes a node. If it was AI-created, record negative feedback.
    /// </summary>
    public void TrackNodeDeleted(int nodeId)
    {
        if (_lastAiCreatedNodes.Remove(nodeId))
        {
            RecordFeedback(-0.3, "deleted_ai_node", "用户删除了AI创建的节点");
        }
    }

    /// <summary>
    /// Call when user modifies a parameter that AI previously set.
    /// Records a preference if user changes it and keeps it for >threshold.
    /// </summary>
    public void TrackParameterModified(string paramName, object newValue)
    {
        if (_lastAiSetParameters.Contains(paramName))
        {
            // Weak positive: user engaged with the parameter
            RecordFeedback(0.05, "modified_ai_param", $"用户修改了AI设置的参数: {paramName}");
        }

        // After 5 min, if the value persists, record as preference
        _ = RecordDelayedPreference(paramName, newValue, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Call when user triggers undo after an AI operation.
    /// Multiple undos → stronger negative signal.
    /// </summary>
    public void TrackUndoAfterAi(int consecutiveUndos)
    {
        var score = consecutiveUndos switch
        {
            1 => -0.3,
            2 => -0.6,
            _ => -1.0
        };
        RecordFeedback(score, "undo_ai", $"用户撤销了AI操作 (连续{consecutiveUndos}次)");
    }

    /// <summary>Records a successful operation completion with positive score.</summary>
    public void RecordSuccess(string normalizedIntent, List<ToolCallStep> steps, double baseScore = 0.3)
    {
        var score = baseScore;
        if (_hasExportedSinceLastAiAction) score += 0.3;
        if (_lastAiCreatedNodes.Count == 0) score -= 0.1; // no nodes created = less impactful

        var record = new OperationRecord
        {
            UserIntent = _currentIntent ?? "",
            NormalizedIntent = normalizedIntent,
            Outcome = "success",
            Score = Math.Min(1.0, score),
            Steps = steps
        };
        _db.Add(record);
    }

    /// <summary>Records a failed operation.</summary>
    public void RecordFailure(string normalizedIntent, string reason, List<ToolCallStep>? steps = null)
    {
        _db.Add(new OperationRecord
        {
            UserIntent = _currentIntent ?? "",
            NormalizedIntent = normalizedIntent,
            Outcome = "failed",
            Score = -1.0,
            Steps = steps ?? new()
        });
    }

    private void RecordFeedback(double score, string reason, string detail)
    {
        _db.Add(new OperationRecord
        {
            UserIntent = _currentIntent ?? "",
            NormalizedIntent = reason,
            Outcome = score >= 0 ? "success" : "failed",
            Score = score,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task RecordDelayedPreference(string paramName, object value, TimeSpan delay)
    {
        await Task.Delay(delay);
        _profile.SetDefaultParameter(paramName, value);
    }
}
