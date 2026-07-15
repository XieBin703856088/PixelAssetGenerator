using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelAssetGenerator.Models;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator.Controls
{
    public partial class ConsoleControl : UserControl
    {
        private IConsoleService? _service;
        private readonly ObservableCollection<ConsoleEntry> _filtered = new();
        private bool _suppressAutoScroll;
        private bool _isUpdating;

        public ConsoleControl()
        {
            InitializeComponent();

            MessageList.ItemsSource = _filtered;

            // Try to get the service immediately (DI is initialized by the time MainWindow is shown).
            // The ConsoleTabContent Grid starts collapsed, so the control is created but not loaded
            // until the user switches to the Console tab for the first time.
            ConnectService();

            // Also handle the case where the control is dynamically loaded later
            Loaded += (_, _) =>
            {
                if (_service == null)
                    ConnectService();
            };

            Unloaded += (_, _) =>
            {
                if (_service != null)
                    _service.Entries.CollectionChanged -= OnEntriesChanged;
            };

            // Track user scroll to pause auto-scroll
            MessageList.PreviewMouseWheel += (_, _) =>
            {
                if (!IsAtBottom())
                    _suppressAutoScroll = true;
            };

            // Keyboard shortcuts: Ctrl+A = select all, Ctrl+C = copy selected, Ctrl+Shift+C = copy all
            MessageList.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.A && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                {
                    MessageList.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                {
                    CopySelectedToClipboard();
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
                {
                    CopyAllToClipboard();
                    e.Handled = true;
                }
            };
        }

        private void ConnectService()
        {
            _service = ServiceLocator.TryGetService<IConsoleService>();
            if (_service != null)
            {
                _service.Entries.CollectionChanged += OnEntriesChanged;
                RefreshFilter();
                ScrollToBottom();
                UpdateCount();
            }
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshFilter();

            if (!_suppressAutoScroll && IsLoaded)
            {
                Dispatcher.BeginInvoke(new Action(ScrollToBottom), System.Windows.Threading.DispatcherPriority.Background);
            }

            UpdateCount();
        }

        public void RefreshFilter()
        {
            if (_service == null) return;

            bool showErrors = _service.ShowErrors;
            bool showWarnings = _service.ShowWarnings;
            bool showInfos = _service.ShowInfos;
            bool showDebugs = _service.ShowDebugs;

            _isUpdating = true;
            _filtered.Clear();
            foreach (var entry in _service.Entries)
            {
                if (ShouldShow(entry, showErrors, showWarnings, showInfos, showDebugs))
                    _filtered.Add(entry);
            }
            _isUpdating = false;
        }

        private static bool ShouldShow(ConsoleEntry entry,
            bool showErrors, bool showWarnings, bool showInfos, bool showDebugs)
        {
            return entry.Level switch
            {
                LogLevel.Error => showErrors,
                LogLevel.Warning => showWarnings,
                LogLevel.Info or LogLevel.Success => showInfos,
                LogLevel.Debug => showDebugs,
                _ => false,
            };
        }

        private void FilterToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_service == null || _isUpdating) return;

            if (sender == FilterErrorsBtn) _service.ShowErrors = FilterErrorsBtn.IsChecked ?? true;
            else if (sender == FilterWarningsBtn) _service.ShowWarnings = FilterWarningsBtn.IsChecked ?? true;
            else if (sender == FilterInfosBtn) _service.ShowInfos = FilterInfosBtn.IsChecked ?? true;
            else if (sender == FilterDebugsBtn) _service.ShowDebugs = FilterDebugsBtn.IsChecked ?? true;

            RefreshFilter();
            ScrollToBottom();
            UpdateCount();
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _service?.Clear();
            _filtered.Clear();
            _suppressAutoScroll = false;
            UpdateCount();
        }

        // ── 复制功能 ──

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            CopyAllToClipboard();
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedToClipboard();
        }

        private void CopySelectedToClipboard()
        {
            var selected = MessageList.SelectedItems;
            var text = selected.Count == 0
                ? string.Join(Environment.NewLine, _filtered.Select(e => e.FormattedText))
                : string.Join(Environment.NewLine, selected.Cast<ConsoleEntry>().Select(e => e.FormattedText));
            if (!string.IsNullOrEmpty(text))
                SetClipboardWithRetry(text);
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            CopyAllToClipboard();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            MessageList.SelectAll();
        }

        private void CopyAllToClipboard()
        {
            var text = string.Join(Environment.NewLine, _filtered.Select(e => e.FormattedText));
            if (!string.IsNullOrEmpty(text))
                SetClipboardWithRetry(text);
        }

        /// <summary>剪贴板写入，使用 SetText 替代 SetDataObject 以减少 COM 阻塞。用 Dispatcher 确保在 STA 线程执行。</summary>
        private static void SetClipboardWithRetry(string text, int maxRetries = 5)
        {
            // 用 Application.Current.Dispatcher 切回 UI (STA) 线程，但用 Background 优先级避免阻塞
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        Clipboard.SetText(text, TextDataFormat.UnicodeText);
                        return;
                    }
                    catch (Exception) when (i < maxRetries - 1)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConsoleControl] Clipboard failed after {maxRetries} retries: {ex.Message}");
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateCount()
        {
            if (_service == null) return;
            int total = _service.Entries.Count;
            int shown = _filtered.Count;
            EntryCountText.Text = shown == total
                ? $"{total} 条"
                : $"{shown}/{total} 条";
        }

        private void ScrollToBottom()
        {
            if (_filtered.Count == 0) return;
            if (MessageList.Items.Count > 0)
            {
                MessageList.ScrollIntoView(MessageList.Items[^1]);
            }
            _suppressAutoScroll = false;
        }

        private bool IsAtBottom()
        {
            var sv = FindScrollViewer(MessageList);
            if (sv == null) return true;
            return sv.VerticalOffset >= sv.ExtentHeight - sv.ViewportHeight - 2;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject? dep)
        {
            if (dep == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                if (child is ScrollViewer sv) return sv;
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
