namespace PixelAssetGenerator.Services.Learning;

public sealed class OperationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserIntent { get; set; } = "";
    public string NormalizedIntent { get; set; } = "";
    public string Outcome { get; set; } = "success"; // success / partial / failed
    public double Score { get; set; }
    public List<ToolCallStep> Steps { get; set; } = new();
}

public sealed class ToolCallStep
{
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Result { get; set; } = "";
}

public sealed class UserPreferences
{
    public Dictionary<string, object> DefaultParameters { get; set; } = new();
    public Dictionary<string, string> StylePreferences { get; set; } = new();
    public string LayoutDirection { get; set; } = "top-down";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
