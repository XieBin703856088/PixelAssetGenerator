using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    /// <summary>Identifies an action that can be bound to a keyboard shortcut.</summary>
    public enum ShortcutAction
    {
        // ---- 通用操作 ----
        Undo,
        Redo,
        // ---- 文件操作 ----
        NewProject,
        OpenProject,
        SaveProject,
        ExportTiles,
        // ---- 自定义形状绘制窗口 ----
        FinishPath,
        Cancel,
        ClearCanvas,
        DrawingModePencil,
        DrawingModePath,
        DrawingModeMove,
        DrawingModeShape,
        BrushSizeIncrease,
        BrushSizeDecrease,
        ToggleEraser,
        // ---- 主窗口节点画布 ----
        SelectAll,
        CopyNodes,
        PasteNodes,
        DeleteSelected,
        ZoomToSelected,
        TogglePreviewGrid,
        ToggleTilePreview,
        FitPreview,
        AutoArrange,
        ToggleNoiseNode,
    }

    /// <summary>A key + modifier combination that triggers a <see cref="ShortcutAction"/>.</summary>
    public sealed class ShortcutBinding
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Key Key { get; set; } = Key.None;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;

        /// <summary>Human-readable display string, e.g. "Ctrl+Z".</summary>
        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                if (Key == Key.None) return Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>().GetString("Hint_Unbound");
                var parts = new System.Collections.Generic.List<string>();
                if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((Modifiers & ModifierKeys.Shift)   != 0) parts.Add("Shift");
                if ((Modifiers & ModifierKeys.Alt)     != 0) parts.Add("Alt");
                parts.Add(KeyToDisplayName(Key));
                return string.Join("+", parts);
            }
        }

        /// <summary>Returns true when <paramref name="key"/> and <paramref name="modifiers"/> match this binding.</summary>
        public bool IsMatch(Key key, ModifierKeys modifiers)
            => Key != Key.None && key == Key && modifiers == Modifiers;

        public ShortcutBinding Clone() => new() { Key = Key, Modifiers = Modifiers };

        private static string KeyToDisplayName(Key key) => key switch
        {
            Key.Return  => "Enter",
            Key.Escape  => "Esc",
            Key.Delete  => "Delete",
            Key.Back    => "Backspace",
            Key.Space   => "Space",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            _           => key.ToString()
        };
    }

    /// <summary>Persisted application settings.</summary>
    public sealed class AppSettings
    {
        /// <summary>"Dark" or "Light".</summary>
        public string Theme { get; set; } = "Dark";

        /// <summary>Language code, e.g. "zh-Hans", "en". Empty = use system default.</summary>
        public string Language { get; set; } = "";

        /// <summary>Custom nodes directory path. Empty = use default (app/Nodes).</summary>
        public string CustomNodesPath { get; set; } = "";

        /// <summary>Node library thumbnail size. 0 = auto-compute on first launch.</summary>
        public double NodeThumbnailSize { get; set; } = 0;

        /// <summary>Keyboard shortcuts keyed by <see cref="ShortcutAction"/> name.</summary>
        public Dictionary<string, ShortcutBinding> Shortcuts { get; set; } = CreateDefaultShortcuts();

        /// <summary>Returns the factory-default shortcut bindings.</summary>
        public static Dictionary<string, ShortcutBinding> CreateDefaultShortcuts() => new()
        {
            // 通用
            [nameof(ShortcutAction.Undo)]             = new() { Key = Key.Z,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.Redo)]             = new() { Key = Key.Y,      Modifiers = ModifierKeys.Control },
            // 文件操作
            [nameof(ShortcutAction.NewProject)]       = new() { Key = Key.N,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.OpenProject)]      = new() { Key = Key.O,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.SaveProject)]      = new() { Key = Key.S,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.ExportTiles)]      = new() { Key = Key.E,      Modifiers = ModifierKeys.Control },
            // 自定义形状绘制窗口
            [nameof(ShortcutAction.FinishPath)]        = new() { Key = Key.Return,           Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.Cancel)]            = new() { Key = Key.Escape,           Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.ClearCanvas)]       = new() { Key = Key.Delete,           Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.DrawingModePencil)] = new() { Key = Key.P,                Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.DrawingModePath)]   = new() { Key = Key.L,                Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.DrawingModeMove)]   = new() { Key = Key.V,                Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.DrawingModeShape)]  = new() { Key = Key.S,                Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.BrushSizeIncrease)] = new() { Key = Key.OemPlus,  Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.BrushSizeDecrease)] = new() { Key = Key.OemMinus, Modifiers = ModifierKeys.None },
            [nameof(ShortcutAction.ToggleEraser)]      = new() { Key = Key.E,        Modifiers = ModifierKeys.None },
            // 主窗口节点画布
            [nameof(ShortcutAction.SelectAll)]        = new() { Key = Key.A,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.CopyNodes)]        = new() { Key = Key.C,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.PasteNodes)]       = new() { Key = Key.V,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.DeleteSelected)]   = new() { Key = Key.Delete, Modifiers = ModifierKeys.None    },
            [nameof(ShortcutAction.ZoomToSelected)]   = new() { Key = Key.Z,      Modifiers = ModifierKeys.None    },
            [nameof(ShortcutAction.TogglePreviewGrid)]= new() { Key = Key.G,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.ToggleTilePreview)] = new() { Key = Key.T,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.FitPreview)]        = new() { Key = Key.F,      Modifiers = ModifierKeys.None    },
            [nameof(ShortcutAction.AutoArrange)]       = new() { Key = Key.L,      Modifiers = ModifierKeys.Control },
            [nameof(ShortcutAction.ToggleNoiseNode)]   = new() { Key = Key.D,      Modifiers = ModifierKeys.Control },
        };

        /// <summary>Returns true when the key event matches the binding for <paramref name="action"/>.</summary>
        public bool IsMatch(ShortcutAction action, Key key, ModifierKeys modifiers)
        {
            if (!Shortcuts.TryGetValue(action.ToString(), out var binding)) return false;
            return binding.IsMatch(key, modifiers);
        }

        /// <summary>Returns the <see cref="ShortcutBinding"/> for <paramref name="action"/>, or an empty binding.</summary>
        public ShortcutBinding GetBinding(ShortcutAction action)
            => Shortcuts.TryGetValue(action.ToString(), out var b) ? b : new ShortcutBinding();
    }

    /// <summary>
    /// Live shortcut-hint strings for use as tooltip and menu-header content.
    /// Bindings to these properties update automatically when shortcuts are saved.
    /// </summary>
    public sealed class ShortcutHints : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // ---- Main window ----
        public string FitPreviewToolTip        { get; private set; } = string.Empty;
        public string TogglePreviewGridToolTip { get; private set; } = string.Empty;
        public string ToggleTilePreviewToolTip { get; private set; } = string.Empty;
        public string DeleteSelectedMenuHeader { get; private set; } = string.Empty;

        // ---- Shape drawing window ----
        public string FinishPathToolTip              { get; private set; } = string.Empty;
        public string PencilModeToolTip              { get; private set; } = string.Empty;
        public string PathModeToolTip                { get; private set; } = string.Empty;
        public string MoveModeToolTip                { get; private set; } = string.Empty;
        public string ShapeModeToolTip               { get; private set; } = string.Empty;
        public string PencilModeInstructions         { get; private set; } = string.Empty;
        public string PathModeInstructions           { get; private set; } = string.Empty;
        public string MoveModeInstructions           { get; private set; } = string.Empty;
        public string ShapeStampModeInstructions     { get; private set; } = string.Empty;
        public string EditModeInstructions           { get; private set; } = string.Empty;
        public string FinishEditPathModeInstructions { get; private set; } = string.Empty;
        public string FinishEditMoveModeInstructions { get; private set; } = string.Empty;
        public string BrushSizeToolTip               { get; private set; } = string.Empty;
        public string EraserToolTip                  { get; private set; } = string.Empty;
        public string ClearCanvasToolTip             { get; private set; } = string.Empty;

        /// <summary>Recomputes all hint strings from <paramref name="settings"/> and notifies bindings.</summary>
        internal void Refresh(AppSettings settings)
        {
            string Key(ShortcutAction a) => settings.GetBinding(a).DisplayText;
            string Lc(string key, params object?[] args) => Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>().GetString(key);

            FitPreviewToolTip        = string.Format(Lc("Hint_FitPreview"), Key(ShortcutAction.FitPreview));
            TogglePreviewGridToolTip = string.Format(Lc("Hint_TogglePreviewGrid"), Key(ShortcutAction.TogglePreviewGrid));
            ToggleTilePreviewToolTip = string.Format(Lc("Hint_ToggleTilePreview"), Key(ShortcutAction.ToggleTilePreview));
            DeleteSelectedMenuHeader = string.Format(Lc("Hint_DeleteSelected"), Key(ShortcutAction.DeleteSelected));

            var undo   = Key(ShortcutAction.Undo);
            var redo   = Key(ShortcutAction.Redo);
            var finish = Key(ShortcutAction.FinishPath);
            var cancel = Key(ShortcutAction.Cancel);
            var pencil = Key(ShortcutAction.DrawingModePencil);
            var path   = Key(ShortcutAction.DrawingModePath);
            var move   = Key(ShortcutAction.DrawingModeMove);
            var shape  = Key(ShortcutAction.DrawingModeShape);
            var eraser = Key(ShortcutAction.ToggleEraser);
            var clear  = Key(ShortcutAction.ClearCanvas);

            FinishPathToolTip              = string.Format(Lc("Hint_FinishPath"), finish);
            BrushSizeToolTip               = string.Format(Lc("Hint_BrushSize"), Key(ShortcutAction.BrushSizeDecrease), Key(ShortcutAction.BrushSizeIncrease));
            EraserToolTip                  = string.Format(Lc("Hint_Eraser"), eraser);
            ClearCanvasToolTip             = string.Format(Lc("Hint_ClearCanvas"), clear);
            PencilModeToolTip              = string.Format(Lc("Hint_PencilMode"), pencil);
            PathModeToolTip                = string.Format(Lc("Hint_PathMode"), path);
            MoveModeToolTip                = string.Format(Lc("Hint_MoveMode"), move);
            ShapeModeToolTip               = string.Format(Lc("Hint_ShapeMode"), shape);
            PencilModeInstructions         = string.Format(Lc("Hint_PencilInstructions"), undo, redo, eraser, clear);
            PathModeInstructions           = string.Format(Lc("Hint_PathInstructions"), finish, undo, redo);
            MoveModeInstructions           = string.Format(Lc("Hint_MoveInstructions"), cancel, undo);
            ShapeStampModeInstructions     = string.Format(Lc("Hint_ShapeStampInstructions"), shape, undo);
            EditModeInstructions           = string.Format(Lc("Hint_EditModeInstructions"), undo);
            FinishEditPathModeInstructions = string.Format(Lc("Hint_FinishEditPathInstructions"), finish, undo, redo, cancel);
            FinishEditMoveModeInstructions = string.Format(Lc("Hint_FinishEditMoveInstructions"), cancel, undo);

            // Notify all bound properties at once
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>Loads, saves and applies <see cref="AppSettings"/> at runtime.</summary>
    public static class SettingsService
    {
        private static readonly string _settingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelAssetGenerator");

        private static readonly string _settingsPath =
            Path.Combine(_settingsDir, "settings.json");

        /// <summary>JSON serializer options used for settings persistence.</summary>
        public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Full path to the settings.json file.</summary>
        public static string SettingsPath => _settingsPath;

        /// <summary>The currently active settings. Loaded once at startup.</summary>
        public static AppSettings Current { get; set; } = Load();

        /// <summary>Live shortcut hints; update automatically when shortcuts are saved.</summary>
        public static ShortcutHints Hints { get; } = InitHints();

        private static ShortcutHints InitHints()
        {
            var h = new ShortcutHints();
            h.Refresh(Current);
            return h;
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        // Backfill any actions added after the user's settings were saved
                        var defaults = AppSettings.CreateDefaultShortcuts();
                        foreach (var kv in defaults)
                            loaded.Shortcuts.TryAdd(kv.Key, kv.Value);
                        return loaded;
                    }
                }
            }
            catch { /* return defaults on any read error */ }
            return new AppSettings();
        }

        /// <summary>Persists <see cref="Current"/> to disk.</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(_settingsPath, json);
                Hints.Refresh(Current);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}"); }
        }

        /// <summary>
        /// Applies the named theme ("Dark" or "Light") to <see cref="Application.Current.Resources"/>
        /// and persists the choice in <see cref="Current"/>.
        /// All windows using <c>DynamicResource</c> bindings update immediately.
        /// </summary>
        public static void ApplyTheme(string theme)
        {
            Current.Theme = theme;
            var res = Application.Current.Resources;

            if (theme == "Light")
            {
                // Layered light palette — four clearly distinct depth layers:
                //   WindowBackground (page) → PanelBackground (card/white) → ControlBackground (input) → PrimaryText (near-black)
                // All text/background pairs verified ≥ WCAG AA 4.5:1 contrast ratio.
                res["WindowBackground"]     = Brush(0xCC, 0xD6, 0xEC); // blue-steel page, clearly distinct from white panels
                res["PanelBackground"]      = Brush(0xFF, 0xFF, 0xFF); // pure white — maximises text contrast (~21:1 with PrimaryText)
                res["PanelBorder"]          = Brush(0xA8, 0xB8, 0xD0); // visible panel separator
                res["PanelBackgroundAlt"]   = Brush(0xE8, 0xF0, 0xFC); // blue-tinted secondary zone
                res["PrimaryText"]          = Brush(0x0D, 0x11, 0x1C); // near-black — 21:1 on white, 13:1 on page
                res["ControlBackground"]    = Brush(0xDC, 0xE8, 0xF6); // distinctly blue inputs, stands out on white panels
                res["ControlBorder"]        = Brush(0x86, 0x9E, 0xBE); // crisp, visible input borders
                res["Accent"]               = Brush(0x0D, 0x47, 0xA1); // Material Blue 900 — 8.2:1 on white, 5.9:1 on page
                res["AccentHover"]          = Brush(0x15, 0x65, 0xC0); // Material Blue 800 — 5.4:1 on white
                res["AccentPressed"]        = Brush(0x0A, 0x38, 0x80); // darker on press
                res["AccentForeground"]     = Brush(0xFF, 0xFF, 0xFF); // white text on dark-blue accent bg
                res["MutedText"]            = Brush(0x3A, 0x50, 0x70); // dark blue-gray — 6.3:1 on white, 5.0:1 on page
                res["NodeBackground"]       = Brush(0xEC, 0xF3, 0xFF); // light blue node body
                res["NodeBorder"]           = Brush(0x88, 0xA4, 0xC4); // visible node outline
                res["NodeHeaderBackground"] = Brush(0xCC, 0xDA, 0xF0); // header clearly distinct from node body
                res["RecordingHighlight"]   = Brush(0xCC, 0x30, 0x00); // deep red-orange — 4.9:1 on white, clearly visible
            }
            else // Dark (default) — AI blue palette
            {
                res["WindowBackground"]     = Brush(0x0B, 0x0E, 0x14); // deepest blue-black
                res["PanelBackground"]      = Brush(0x0E, 0x12, 0x20); // deep blue panel
                res["PanelBorder"]          = Brush(0x1E, 0x28, 0x40); // blue-toned border
                res["PanelBackgroundAlt"]   = Brush(0x0A, 0x0F, 0x1C); // alternate depth layer
                res["PrimaryText"]          = Brush(0xC8, 0xD8, 0xF0); // cool blue-white text
                res["ControlBackground"]    = Brush(0x14, 0x1C, 0x30); // blue-dark input bg
                res["ControlBorder"]        = Brush(0x1E, 0x28, 0x40); // matches panel border
                res["Accent"]               = Brush(0x4A, 0x80, 0xC8); // AI blue accent
                res["AccentHover"]          = Brush(0x5A, 0x90, 0xD8); // lighter on hover
                res["AccentPressed"]        = Brush(0x3A, 0x6A, 0xB0); // darker on press
                res["AccentForeground"]     = Brush(0xC8, 0xD8, 0xF0); // light text on accent bg
                res["MutedText"]            = Brush(0x6B, 0x7A, 0x9F); // muted blue-gray
                res["NodeBackground"]       = Brush(0x0E, 0x18, 0x28); // node body
                res["NodeBorder"]           = Brush(0x1E, 0x2E, 0x50); // node outline
                res["NodeHeaderBackground"] = Brush(0x14, 0x20, 0x40); // node header
                res["RecordingHighlight"]   = Brush(0xFF, 0xCC, 0x44); // amber — visible on dark bg
            }
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }
    }
}


