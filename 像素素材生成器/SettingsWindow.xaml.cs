using PixelAssetGenerator.Services.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LocSvc = PixelAssetGenerator.Services.Localization.LocalizationService;

namespace PixelAssetGenerator
{
    public partial class SettingsWindow : Window
    {
        private sealed class ShortcutItem : INotifyPropertyChanged
        {
            public ShortcutAction Action { get; init; }
            private string _actionName = string.Empty;
            public string ActionName
            {
                get => _actionName;
                set { _actionName = value; Notify(); }
            }

            private ShortcutBinding _binding = new();
            public ShortcutBinding Binding
            {
                get => _binding;
                set { _binding = value; Notify(nameof(BindingText)); }
            }

            public string BindingText => Binding.DisplayText;

            private bool _isRecording;
            public bool IsRecording
            {
                get => _isRecording;
                set { _isRecording = value; Notify(nameof(IsRecording)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            internal void Notify([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class LanguageItem
        {
            public string Code { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public override string ToString() => DisplayName;
        }

        private readonly string _originalTheme;
        private readonly string _originalLanguage;
        private readonly string _originalNodesPath;
        private readonly string _defaultNodesPath;
        private string _currentNodesPath;
        private readonly Dictionary<string, ShortcutBinding> _originalShortcuts;
        private readonly List<ShortcutItem> _items;
        private ShortcutItem? _recordingItem;

        public SettingsWindow()
        {
            InitializeComponent();

            _originalTheme = SettingsService.Current.Theme;
            _originalLanguage = LocSvc.Instance.CurrentCulture;
            _defaultNodesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes");
            _originalNodesPath = SettingsService.Current.CustomNodesPath;
            _currentNodesPath = string.IsNullOrEmpty(_originalNodesPath) ? _defaultNodesPath : _originalNodesPath;

            NodesPathText.Text = _currentNodesPath;
            CachePathText.Text = Core.Nodes.Sources.ResourceNodeInstance.GetScriptCacheDir();

            // Deep-copy original shortcuts so Cancel can revert them
            _originalShortcuts = new Dictionary<string, ShortcutBinding>();
            foreach (var kv in SettingsService.Current.Shortcuts)
                _originalShortcuts[kv.Key] = kv.Value.Clone();

            _items = new List<ShortcutItem>
            {
                MakeItem(ShortcutAction.Undo,              "Shortcut_Undo"),
                MakeItem(ShortcutAction.Redo,              "Shortcut_Redo"),
                // 文件操作
                MakeItem(ShortcutAction.NewProject,        "Shortcut_NewProject"),
                MakeItem(ShortcutAction.OpenProject,       "Shortcut_OpenProject"),
                MakeItem(ShortcutAction.SaveProject,       "Shortcut_SaveProject"),
                MakeItem(ShortcutAction.ExportTiles,       "Shortcut_ExportTiles"),
                // 绘制窗口
                MakeItem(ShortcutAction.DrawingModePencil, "Shortcut_DrawingModePencil"),
                MakeItem(ShortcutAction.DrawingModePath,   "Shortcut_DrawingModePath"),
                MakeItem(ShortcutAction.DrawingModeMove,   "Shortcut_DrawingModeMove"),
                MakeItem(ShortcutAction.DrawingModeShape,  "Shortcut_DrawingModeShape"),
                MakeItem(ShortcutAction.BrushSizeIncrease, "Shortcut_BrushSizeIncrease"),
                MakeItem(ShortcutAction.BrushSizeDecrease, "Shortcut_BrushSizeDecrease"),
                MakeItem(ShortcutAction.ToggleEraser,      "Shortcut_ToggleEraser"),
                MakeItem(ShortcutAction.FinishPath,        "Shortcut_FinishPath"),
                MakeItem(ShortcutAction.Cancel,            "Shortcut_Cancel"),
                MakeItem(ShortcutAction.ClearCanvas,       "Shortcut_ClearCanvas"),
                // 节点画布
                MakeItem(ShortcutAction.SelectAll,         "Shortcut_SelectAll"),
                MakeItem(ShortcutAction.CopyNodes,         "Shortcut_CopyNodes"),
                MakeItem(ShortcutAction.PasteNodes,        "Shortcut_PasteNodes"),
                MakeItem(ShortcutAction.DeleteSelected,    "Shortcut_DeleteSelected"),
                MakeItem(ShortcutAction.ZoomToSelected,    "Shortcut_ZoomToSelected"),
                MakeItem(ShortcutAction.TogglePreviewGrid, "Shortcut_TogglePreviewGrid"),
                MakeItem(ShortcutAction.ToggleTilePreview, "Shortcut_ToggleTilePreview"),
                MakeItem(ShortcutAction.FitPreview,        "Shortcut_FitPreview"),
                MakeItem(ShortcutAction.AutoArrange,       "Shortcut_AutoArrange"),
                MakeItem(ShortcutAction.ToggleNoiseNode,   "Shortcut_ToggleNoiseNode"),
            };

            ShortcutList.ItemsSource = _items;
            ApplyThemeCardSelection(SettingsService.Current.Theme);
            PopulateLanguageCombo();
        }

        private static string Loc(string key) => LocSvc.Instance.GetString(key);

        private ShortcutItem MakeItem(ShortcutAction action, string locKey) => new()
        {
            Action = action,
            ActionName = Loc(locKey),
            Binding = SettingsService.Current.GetBinding(action).Clone()
        };

        // ─────────────────────────────────────────────────────────────────
        // 导航切换
        // ─────────────────────────────────────────────────────────────────

        private void SelectNav(string page)
        {
            bool isGeneral = page == "General";

            // 左侧导航高亮
            var accentBrush = (SolidColorBrush?)TryFindResource("Accent")
                              ?? new SolidColorBrush(Colors.DodgerBlue);
            var accentFg = (SolidColorBrush?)TryFindResource("AccentForeground")
                           ?? new SolidColorBrush(Colors.White);
            var textBrush = (SolidColorBrush?)TryFindResource("PrimaryText")
                            ?? new SolidColorBrush(Colors.LightGray);

            NavGeneral.Background = isGeneral ? accentBrush : new SolidColorBrush(Colors.Transparent);
            if (NavGeneral.Child is TextBlock tg)
            {
                tg.Foreground = isGeneral ? accentFg : textBrush;
                tg.FontWeight = isGeneral ? FontWeights.SemiBold : FontWeights.Normal;
            }

            NavShortcuts.Background = isGeneral ? new SolidColorBrush(Colors.Transparent) : accentBrush;
            if (NavShortcuts.Child is TextBlock ts)
            {
                ts.Foreground = isGeneral ? textBrush : accentFg;
                ts.FontWeight = isGeneral ? FontWeights.Normal : FontWeights.SemiBold;
            }

            // 右侧面板切换
            GeneralPanel.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
            ShortcutsPanel.Visibility = isGeneral ? Visibility.Collapsed : Visibility.Visible;

            // 顶栏重置快捷键按钮
            ResetShortcutsButton.Visibility = isGeneral ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NavGeneral_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => SelectNav("General");

        private void NavShortcuts_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => SelectNav("Shortcuts");

        // ─────────────────────────────────────────────────────────────────
        // 语言
        // ─────────────────────────────────────────────────────────────────

        private void PopulateLanguageCombo()
        {
            var cultures = LocSvc.Instance.GetAvailableCultures();
            var items = cultures.Select(c => new LanguageItem { Code = c.Item1, DisplayName = c.Item2 }).ToList();

            LanguageCombo.ItemsSource = items;

            var current = LocSvc.Instance.CurrentCulture;
            var sel = items.FirstOrDefault(i => i.Code == current);
            if (sel != null) LanguageCombo.SelectedItem = sel;
        }

        private void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem is LanguageItem item && item.Code != LocSvc.Instance.CurrentCulture)
            {
                LocSvc.Instance.SetCulture(item.Code);
                RefreshLocalizedText();
            }
        }

        private void OpenLangFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Languages");

            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); }
                catch { }
            }

            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 节点 & 缓存路径
        // ─────────────────────────────────────────────────────────────────

        private void BrowseNodes_Click(object sender, RoutedEventArgs e)
        {
            var folder = _currentNodesPath;
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); }
                catch { }
            }

            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }

        private void ResetNodesPath_Click(object sender, RoutedEventArgs e)
        {
            _currentNodesPath = _defaultNodesPath;
            NodesPathText.Text = _currentNodesPath;
        }

        private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Core.Nodes.Sources.ResourceNodeInstance.GetScriptCacheDir();
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); }
                catch { }
            }

            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }

        private void RefreshLocalizedText()
        {
            foreach (var shortcutItem in _items)
            {
                var locKey = shortcutItem.Action switch
                {
                    ShortcutAction.Undo => "Shortcut_Undo",
                    ShortcutAction.Redo => "Shortcut_Redo",
                    ShortcutAction.NewProject => "Shortcut_NewProject",
                    ShortcutAction.OpenProject => "Shortcut_OpenProject",
                    ShortcutAction.SaveProject => "Shortcut_SaveProject",
                    ShortcutAction.ExportTiles => "Shortcut_ExportTiles",
                    ShortcutAction.DrawingModePencil => "Shortcut_DrawingModePencil",
                    ShortcutAction.DrawingModePath => "Shortcut_DrawingModePath",
                    ShortcutAction.DrawingModeMove => "Shortcut_DrawingModeMove",
                    ShortcutAction.DrawingModeShape => "Shortcut_DrawingModeShape",
                    ShortcutAction.BrushSizeIncrease => "Shortcut_BrushSizeIncrease",
                    ShortcutAction.BrushSizeDecrease => "Shortcut_BrushSizeDecrease",
                    ShortcutAction.ToggleEraser => "Shortcut_ToggleEraser",
                    ShortcutAction.FinishPath => "Shortcut_FinishPath",
                    ShortcutAction.Cancel => "Shortcut_Cancel",
                    ShortcutAction.ClearCanvas => "Shortcut_ClearCanvas",
                    ShortcutAction.SelectAll => "Shortcut_SelectAll",
                    ShortcutAction.CopyNodes => "Shortcut_CopyNodes",
                    ShortcutAction.PasteNodes => "Shortcut_PasteNodes",
                    ShortcutAction.DeleteSelected => "Shortcut_DeleteSelected",
                    ShortcutAction.ZoomToSelected => "Shortcut_ZoomToSelected",
                    ShortcutAction.TogglePreviewGrid => "Shortcut_TogglePreviewGrid",
                    ShortcutAction.ToggleTilePreview => "Shortcut_ToggleTilePreview",
                    ShortcutAction.FitPreview => "Shortcut_FitPreview",
                    ShortcutAction.AutoArrange => "Shortcut_AutoArrange",
                    ShortcutAction.ToggleNoiseNode => "Shortcut_ToggleNoiseNode",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(locKey))
                    shortcutItem.ActionName = Loc(locKey);
            }

            foreach (var shortcutItem in _items)
            {
                shortcutItem.Notify(nameof(ShortcutItem.ActionName));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 主题
        // ─────────────────────────────────────────────────────────────────

        private void DarkThemeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => SelectTheme("Dark");

        private void LightThemeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => SelectTheme("Light");

        private void SelectTheme(string theme)
        {
            SettingsService.ApplyTheme(theme);
            ApplyThemeCardSelection(theme);
        }

        private void ApplyThemeCardSelection(string theme)
        {
            var accentBrush = (SolidColorBrush?)TryFindResource("Accent")
                              ?? new SolidColorBrush(Colors.DodgerBlue);
            var borderBrush = (SolidColorBrush?)TryFindResource("ControlBorder")
                              ?? new SolidColorBrush(Colors.Gray);

            bool isDark = theme == "Dark";
            DarkThemeCard.BorderBrush = isDark ? accentBrush : borderBrush;
            LightThemeCard.BorderBrush = !isDark ? accentBrush : borderBrush;
            DarkThemeCard.Opacity = isDark ? 1.0 : 0.5;
            LightThemeCard.Opacity = !isDark ? 1.0 : 0.5;
        }

        // ─────────────────────────────────────────────────────────────────
        // 快捷键
        // ─────────────────────────────────────────────────────────────────

        private void EditShortcut_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not ShortcutItem item) return;

            if (item.IsRecording)
            {
                CancelRecording();
            }
            else
            {
                CancelRecording();
                _recordingItem = item;
                item.IsRecording = true;
                Focus();
            }
        }

        private void CancelRecording()
        {
            if (_recordingItem == null) return;
            _recordingItem.IsRecording = false;
            _recordingItem = null;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (_recordingItem != null)
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;

                if (key is Key.LeftCtrl or Key.RightCtrl
                          or Key.LeftShift or Key.RightShift
                          or Key.LeftAlt or Key.RightAlt
                          or Key.LWin or Key.RWin)
                {
                    base.OnPreviewKeyDown(e);
                    return;
                }

                if (key == Key.Escape)
                {
                    CancelRecording();
                }
                else if (key is Key.Delete or Key.Back)
                {
                    _recordingItem.Binding = new ShortcutBinding { Key = Key.None };
                    CancelRecording();
                }
                else
                {
                    _recordingItem.Binding = new ShortcutBinding
                    {
                        Key = key,
                        Modifiers = Keyboard.Modifiers
                    };
                    CancelRecording();
                }

                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        // ─────────────────────────────────────────────────────────────────
        // 底部按钮
        // ─────────────────────────────────────────────────────────────────

        private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
        {
            CancelRecording();
            var defaults = AppSettings.CreateDefaultShortcuts();
            foreach (var item in _items)
            {
                if (defaults.TryGetValue(item.Action.ToString(), out var def))
                    item.Binding = def.Clone();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            CancelRecording();

            if (LanguageCombo.SelectedItem is LanguageItem langItem)
                SettingsService.Current.Language = langItem.Code;

            foreach (var item in _items)
                SettingsService.Current.Shortcuts[item.Action.ToString()] = item.Binding.Clone();

            var savedPath = _currentNodesPath.Equals(_defaultNodesPath, StringComparison.OrdinalIgnoreCase)
                ? ""
                : _currentNodesPath;
            SettingsService.Current.CustomNodesPath = savedPath;

            SettingsService.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelRecording();

            if (SettingsService.Current.Theme != _originalTheme)
                SettingsService.ApplyTheme(_originalTheme);

            if (LocSvc.Instance.CurrentCulture != _originalLanguage)
                LocSvc.Instance.SetCulture(_originalLanguage);

            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult != true)
            {
                if (SettingsService.Current.Theme != _originalTheme)
                    SettingsService.ApplyTheme(_originalTheme);
                if (LocSvc.Instance.CurrentCulture != _originalLanguage)
                    LocSvc.Instance.SetCulture(_originalLanguage);
            }

            base.OnClosing(e);
        }
    }
}
