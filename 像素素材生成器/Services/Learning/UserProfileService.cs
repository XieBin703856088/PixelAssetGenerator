using System.IO;
using System.Text.Json;

namespace PixelAssetGenerator.Services.Learning;

/// <summary>
/// Tracks user preferences over time: default parameter values, style tendencies,
/// and layout habits. Persisted as JSON in app data folder.
/// </summary>
public sealed class UserProfileService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private UserPreferences _profile = new();

    public event Action<UserPreferences>? OnPreferencesChanged;

    public UserProfileService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "像素素材生成器", "learning");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "user_profile.json");
        Load();
    }

    public UserPreferences Current => _profile;

    /// <summary>Record a parameter default preference.</summary>
    public void SetDefaultParameter(string name, object value)
    {
        _profile.DefaultParameters[name] = value;
        _profile.LastUpdated = DateTime.UtcNow;
        Save();
        OnPreferencesChanged?.Invoke(_profile);
    }

    /// <summary>Get preferred default for a parameter, or null.</summary>
    public object? GetDefaultParameter(string name)
    {
        return _profile.DefaultParameters.TryGetValue(name, out var val) ? val : null;
    }

    /// <summary>Record a style preference (e.g., noise type).</summary>
    public void SetStylePreference(string key, string value)
    {
        _profile.StylePreferences[key] = value;
        _profile.LastUpdated = DateTime.UtcNow;
        Save();
    }

    /// <summary>Update layout direction preference.</summary>
    public void SetLayoutDirection(string direction)
    {
        _profile.LayoutDirection = direction;
        _profile.LastUpdated = DateTime.UtcNow;
        Save();
    }

    /// <summary>Fill unspecified parameters from user preferences.</summary>
    public Dictionary<string, object> ApplyDefaults(Dictionary<string, object> parameters)
    {
        var result = new Dictionary<string, object>(parameters);
        foreach (var (key, val) in _profile.DefaultParameters)
        {
            if (!result.ContainsKey(key))
                result[key] = val;
        }
        return result;
    }

    /// <summary>Build a compact summary string for system prompt injection.</summary>
    public string ToPreferenceSummary()
    {
        var parts = new List<string>();

        if (_profile.DefaultParameters.Count > 0)
        {
            var defaults = string.Join(", ",
                _profile.DefaultParameters.Select(kv => $"{kv.Key}={kv.Value}"));
            parts.Add($"常用参数默认值: {defaults}");
        }

        if (_profile.StylePreferences.Count > 0)
        {
            var styles = string.Join(", ",
                _profile.StylePreferences.Select(kv => $"{kv.Key}={kv.Value}"));
            parts.Add($"风格倾向: {styles}");
        }

        parts.Add($"布局方向: {(_profile.LayoutDirection == "top-down" ? "从上到下" : "从左到右")}");

        return parts.Count > 0 ? string.Join("; ", parts) : "";
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            _profile = JsonSerializer.Deserialize<UserPreferences>(json) ?? new();
        }
        catch
        {
            _profile = new();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_profile, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
