using System.IO;
using System.Text.Json;

namespace PixelAssetGenerator.Services.Learning;

/// <summary>
/// JSON file-based experience store. Records AI operations, feedback,
/// and capability metrics. All data is local-only, no external dependencies.
/// </summary>
public sealed class ExperienceDb
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private List<OperationRecord> _records = new();

    public ExperienceDb()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "像素素材生成器", "learning");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "experience.json");
        Load();
    }

    public IReadOnlyList<OperationRecord> GetAll() => _records.AsReadOnly();

    public void Add(OperationRecord record)
    {
        _records.Add(record);
        Save();
    }

    /// <summary>Find records by normalized intent, ordered by score descending.</summary>
    public List<OperationRecord> QueryByIntent(string normalizedIntent, int topK = 3)
    {
        if (string.IsNullOrWhiteSpace(normalizedIntent))
            return new List<OperationRecord>();

        var intent = normalizedIntent.Trim().ToLowerInvariant();

        return _records
            .Where(r => r.NormalizedIntent.Contains(intent, StringComparison.OrdinalIgnoreCase)
                     || r.UserIntent.Contains(intent, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Timestamp)
            .Take(topK)
            .ToList();
    }

    /// <summary>Get records with positive scores for few-shot examples.</summary>
    public List<OperationRecord> GetPositiveExamples(string normalizedIntent, int count = 2)
    {
        var byIntent = QueryByIntent(normalizedIntent, count * 2)
            .Where(r => r.Score > 0 && r.Steps.Count > 0)
            .OrderByDescending(r => r.Score)
            .Take(count)
            .ToList();

        if (byIntent.Count >= count)
            return byIntent;

        // Fall back to recent high-score records from any intent
        var recent = _records
            .Where(r => r.Score > 0 && r.Steps.Count > 0)
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Timestamp)
            .Take(count - byIntent.Count)
            .ToList();

        byIntent.AddRange(recent);
        return byIntent;
    }

    /// <summary>Returns success rate for a given intent category.</summary>
    public double GetSuccessRate(string normalizedIntent)
    {
        var relevant = _records
            .Where(r => r.NormalizedIntent.Contains(normalizedIntent, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevant.Count == 0) return 0.5; // unknown = 50%
        return (double)relevant.Count(r => r.Score > 0) / relevant.Count;
    }

    /// <summary>Recent parse-failure rate (last 20 records).</summary>
    public double GetRecentParseFailureRate()
    {
        var recent = _records.TakeLast(20).ToList();
        if (recent.Count == 0) return 0;
        return (double)recent.Count(r => r.Outcome == "failed") / recent.Count;
    }

    /// <summary>Recent max-round-hit rate.</summary>
    public double GetMaxRoundHitRate()
    {
        var recent = _records.TakeLast(20).ToList();
        if (recent.Count == 0) return 0;
        return (double)recent.Count(r => r.Outcome == "max_rounds") / recent.Count;
    }

    /// <summary>All distinct intents with their success rates.</summary>
    public Dictionary<string, double> GetIntentSuccessRates()
    {
        var groups = _records
            .GroupBy(r => r.NormalizedIntent)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        var result = new Dictionary<string, double>();
        foreach (var group in groups)
        {
            var total = group.Count();
            var positive = group.Count(r => r.Score > 0);
            result[group.Key] = total > 0 ? (double)positive / total : 0.5;
        }
        return result;
    }

    /// <summary>Remove records older than the given threshold.</summary>
    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        _records.RemoveAll(r => r.Timestamp < cutoff && r.Score <= 0);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            _records = JsonSerializer.Deserialize<List<OperationRecord>>(json) ?? new();
        }
        catch
        {
            _records = new();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_records, _jsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        try
        {
            File.Delete(_filePath);
        }
        catch { /* best-effort: temp file may already be the primary file */ }
        File.Move(tempPath, _filePath);
    }
}
