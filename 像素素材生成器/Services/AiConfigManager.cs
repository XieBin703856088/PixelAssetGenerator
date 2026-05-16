using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelAssetGenerator.Services;

/// <summary>A named AI configuration preset.</summary>
public sealed class AiConfigProfile
{
    /// <summary>Display name for this profile.</summary>
    public string Name { get; set; } = "默认配置";

    /// <summary>The AI settings for this profile.</summary>
    public AiSettings Settings { get; set; } = new();
}

/// <summary>Manages AI configuration as a separate file independent of main app settings.</summary>
public sealed class AiConfigManager
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelAssetGenerator");

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "ai-settings.json");

    private static readonly string OldSettingsPath =
        Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AiConfigManager Current { get; } = new();

    /// <summary>All saved configuration profiles.</summary>
    public List<AiConfigProfile> Profiles { get; set; } = new();

    /// <summary>Index of the currently active profile in <see cref="Profiles"/>.</summary>
    public int ActiveProfileIndex { get; set; } = 0;

    /// <summary>Gets the currently active settings.</summary>
    public AiSettings Settings => ActiveProfile?.Settings ?? _fallback;

    private AiConfigProfile? ActiveProfile =>
        Profiles.Count > 0 && ActiveProfileIndex >= 0 && ActiveProfileIndex < Profiles.Count
            ? Profiles[ActiveProfileIndex]
            : null;

    private readonly AiSettings _fallback = new();

    /// <summary>Event raised when the profile list changes (added/removed/renamed).</summary>
    public event Action? ProfilesChanged;

    private AiConfigManager() => Load();

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);

                // Try new format first (with Profiles array)
                try
                {
                    var container = JsonSerializer.Deserialize<AiConfigContainer>(json, JsonOptions);
                    if (container?.Profiles != null && container.Profiles.Count > 0)
                    {
                        foreach (var p in container.Profiles)
                            p.Settings?.Normalize();
                        Profiles = container.Profiles;
                        ActiveProfileIndex = Math.Clamp(container.ActiveProfileIndex, 0, Profiles.Count - 1);
                        return;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AiConfigManager] New format deserialize failed, falling back: {ex.Message}"); }

                // Old format: single AiSettings object
                var single = JsonSerializer.Deserialize<AiSettings>(json, JsonOptions);
                if (single != null)
                {
                    single.Normalize();
                    Profiles = new List<AiConfigProfile>
                    {
                        new() { Name = "默认配置", Settings = single }
                    };
                    ActiveProfileIndex = 0;
                    return;
                }
            }

            // One-time migration: copy Ai section from old settings.json
            TryMigrateFromOldSettings();
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"AiConfigManager.Load failed: {ex.Message}"); }

        if (Profiles.Count == 0)
        {
            Profiles = new List<AiConfigProfile>
            {
                new() { Name = "默认配置", Settings = new AiSettings() }
            };
            ActiveProfileIndex = 0;
        }
        Profiles[ActiveProfileIndex].Settings.Normalize();
    }

    private void TryMigrateFromOldSettings()
    {
        try
        {
            if (!File.Exists(OldSettingsPath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(OldSettingsPath));
            if (!doc.RootElement.TryGetProperty("Ai", out var aiElement)) return;

            var oldAi = JsonSerializer.Deserialize<AiSettings>(aiElement.GetRawText(), JsonOptions);
            if (oldAi != null)
            {
                oldAi.Normalize();
                Profiles = new List<AiConfigProfile>
                {
                    new() { Name = "默认配置", Settings = oldAi }
                };
                ActiveProfileIndex = 0;
                Save(); // persist to new file
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"AiConfigManager.TryMigrateFromOldSettings failed: {ex.Message}"); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            foreach (var p in Profiles)
                p.Settings?.Normalize();

            var container = new AiConfigContainer
            {
                Profiles = Profiles,
                ActiveProfileIndex = ActiveProfileIndex
            };
            var json = JsonSerializer.Serialize(container, JsonOptions);
            File.WriteAllText(ConfigPath, json);
            ProfilesChanged?.Invoke();
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"AiConfigManager.Save failed: {ex.Message}"); }
    }

    /// <summary>Switches to a different profile by index.</summary>
    public void SwitchProfile(int index)
    {
        if (index >= 0 && index < Profiles.Count)
        {
            ActiveProfileIndex = index;
        }
    }

    /// <summary>Adds a new profile with default settings.</summary>
    public void AddProfile(string name)
    {
        var profile = new AiConfigProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"配置 {Profiles.Count + 1}" : name,
            Settings = new AiSettings()
        };
        Profiles.Add(profile);
        ActiveProfileIndex = Profiles.Count - 1;
        Save();
    }

    /// <summary>Duplicates the current profile as a new one.</summary>
    public void DuplicateCurrentProfile(string newName)
    {
        var current = ActiveProfile;
        if (current == null) return;

        var json = JsonSerializer.Serialize(current.Settings, JsonOptions);
        var cloned = JsonSerializer.Deserialize<AiSettings>(json, JsonOptions) ?? new AiSettings();
        cloned.Normalize();

        Profiles.Add(new AiConfigProfile
        {
            Name = string.IsNullOrWhiteSpace(newName) ? $"{current.Name} (副本)" : newName,
            Settings = cloned
        });
        ActiveProfileIndex = Profiles.Count - 1;
        Save();
    }

    /// <summary>Renames a profile.</summary>
    public void RenameProfile(int index, string newName)
    {
        if (index >= 0 && index < Profiles.Count && !string.IsNullOrWhiteSpace(newName))
        {
            Profiles[index].Name = newName.Trim();
            Save();
        }
    }

    /// <summary>Deletes a profile. Cannot delete the last profile.</summary>
    public void DeleteProfile(int index)
    {
        if (Profiles.Count <= 1) return;
        if (index < 0 || index >= Profiles.Count) return;

        Profiles.RemoveAt(index);
        if (ActiveProfileIndex >= Profiles.Count)
            ActiveProfileIndex = Profiles.Count - 1;
        Save();
    }

    /// <summary>Gets all profile names.</summary>
    public List<string> GetProfileNames() => Profiles.Select(p => p.Name).ToList();
}

/// <summary>Container for multi-profile AI configuration.</summary>
internal sealed class AiConfigContainer
{
    public List<AiConfigProfile> Profiles { get; set; } = new();
    public int ActiveProfileIndex { get; set; } = 0;
}
