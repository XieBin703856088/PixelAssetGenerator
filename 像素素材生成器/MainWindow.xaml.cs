using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using PixelAssetGenerator.Models;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using PixelAssetGenerator.Core;
using PixelAssetGenerator.Core.Gpu;
using PixelAssetGenerator.Core.Nodes.Sources;
using PixelAssetGenerator.Services;
using PixelAssetGenerator.Services.ToolProviders;
using ExportFormat = PixelAssetGenerator.Services.ExportService.ExportFormat;

namespace PixelAssetGenerator
{

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>Singleton instance for XAML bindings (e.g. NodeThumbnailSize).</summary>
        public static MainWindow? Instance { get; private set; }

        private static Services.Localization.ILocalizationService Loc
            => Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>();

        private const string GrassCustomFlowerPortName = "Custom Flower";
        private readonly TileGenerator _generator = new();
        private readonly NodeGraphEvaluator _graphEvaluator = new();
        private GraphEvaluationService _graphEvalService = null!;
        private NodeGraphController _nodeGraphController = null!;
        private readonly UndoRedoService _undoRedoService = new();
        private BitmapSource? _lastGenerated;
        private bool _isInitialized;
        private double _zoomFactor = 1d;
        private double _fitScale = 1d;
        private double _tiledPreviewContentSize;
        private Point _dragStartPoint;
        private bool _nineSliceMode = false;
        private bool _nineSliceStackMode = true;
        private readonly DispatcherTimer _previewDebounceTimer;
        private readonly DispatcherTimer _fpsTimer;
        private readonly DispatcherTimer _statsIdleTimer;
        private bool _statsActive;

        /// <summary>Animation playback engine. Created on first use.</summary>
        private Services.AnimationPlaybackService? _animationService;

        /// <summary>Particle system coordinator. Created on first use.</summary>
        private Services.ParticleEvaluationService? _particleEvalService;

        /// <summary>Current node library thumbnail size (both width and height in the same proportion).</summary>
        public double NodeThumbnailSize
        {
            get => _nodeThumbnailSize;
            set
            {
                // Quantize to integer: slider sub-pixel changes are ignored
                var rounded = Math.Round(value);
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (rounded == _nodeThumbnailSize) return;
                _nodeThumbnailSize = rounded;
                OnPropertyChanged(nameof(NodeThumbnailSize));
                // Defer thumbnail regeneration until the mouse button is released
                // (see NodeThumbnailSlider_DragCompleted).
            }
        }
        private double _nodeThumbnailSize = 64;

        private DispatcherTimer? _thumbnailDebounceTimer;
        /// <summary>
        /// Debounced thumbnail regeneration using a single DispatcherTimer.
        /// When the slider is dragged rapidly, the timer is reset on each change;
        /// only the final position after 300ms of inactivity triggers regeneration.
        /// </summary>
        private void ScheduleThumbnailRegeneration()
        {
            _thumbnailDebounceTimer ??= CreateThumbnailDebounceTimer();

            // Reset timer on each call (debounce)
            _thumbnailDebounceTimer.Stop();
            _thumbnailDebounceTimer.Start();
        }

        private DispatcherTimer CreateThumbnailDebounceTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try
                {
                    await RefreshNodeLibraryThumbnailsAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail] Refresh failed: {ex.Message}");
                }
            };
            return timer;
        }

        /// <summary>
        /// Safely invokes an action on the Dispatcher, logging exceptions in Debug builds instead of catching silently.
        /// </summary>
        private async Task SafeDispatcherInvokeAsync(Action callback, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            try
            {
                await Dispatcher.InvokeAsync(callback, priority);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafeDispatcherInvoke] Failed: {ex.Message}");
            }
        }

        private void InvalidateNodeLibraryThumbnails()
        {
            ScheduleThumbnailRegeneration();
        }

        /// <summary>
        /// Thumbnail slider drag completed — triggers actual thumbnail regeneration.
        /// During dragging, only the display size updates; heavy work is deferred to here.
        /// </summary>
        private void NodeThumbnailSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            ScheduleThumbnailRegeneration();
        }

        // Called when the UI language changes.
        private void OnCultureChanged()
        {
            // Reload node registry so prototypes pick up new locale for port/parameter names
            Core.GraphNodeRegistry.Reload();

            // Refresh code-generated UI elements that aren't in XAML
            InitializeNodeLibrary();
            Title = Loc.GetString("AppTitle");
            if (NodeLibraryView != null) ApplyNodeLibraryFilter();

            // Refresh node parameter display names on all existing nodes (full rebuild)
            RefreshNodeParameterDisplayNames();
            // Force thumbnail cache to rebuild with new locale text
            _forceThumbnailRefresh = true;
            ScheduleThumbnailRegeneration();
            // Reselect current node to refresh parameter panel
            try
            {
                var cur = SelectedNode;
                if (cur != null)
                {
                    SelectedNode = null;
                    SelectedNode = cur;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnCultureChanged] Failed to re-select node: {ex.Message}");
            }
            RequestPreviewRefresh(false);
        }

        private void RefreshNodeParameterDisplayNames()
        {
            var loc = Loc;
            foreach (var node in Nodes)
            {
                // Refresh node title from library
                var libItem = NodeLibrary.FirstOrDefault(n => n.TypeName == node.TypeName || n.Name == node.Title);
                if (libItem != null)
                    node.Title = libItem.Name;

                // Refresh port names from prototype
                var prototype = Core.GraphNodeRegistry.Create(node.RegistryKey);
                if (prototype == null) continue;

                // Resource-based nodes cache a single instance; refresh their locale now
                if (prototype is Core.Nodes.Sources.ResourceNodeInstance rni)
                    rni.RefreshLocale();

                for (int i = 0; i < prototype.InputPorts.Count && i < node.InputPorts.Count; i++)
                    node.InputPorts[i].Name = prototype.InputPorts[i].Name;
                for (int i = 0; i < prototype.OutputPorts.Count && i < node.OutputPorts.Count; i++)
                    node.OutputPorts[i].Name = prototype.OutputPorts[i].Name;

                // Completely rebuild all parameters from prototype to get fresh locale strings
                var oldValues = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var p in node.Parameters)
                {
                    oldValues[p.Name] = p.Kind switch
                    {
                        NodeParameterKind.Seed or NodeParameterKind.Integer => p.IntValue,
                        NodeParameterKind.Boolean => p.BoolValue,
                        NodeParameterKind.Choice => (object)(p.SelectedChoice ?? ""),
                        NodeParameterKind.Color => p.ColorValue,
                        NodeParameterKind.Text => (object)(p.TextValue ?? ""),
                        NodeParameterKind.PointList => (object)p.PointListValue.ToList(),
                        _ => p.NumberValue
                    };
                }

                node.Parameters.Clear();
                if (prototype.Parameters != null)
                {
                    foreach (var def in prototype.Parameters)
                    {
                        var pvm = NodeParameterDefinition.CreateViewModelFromDef(def);
                        if (oldValues.TryGetValue(pvm.Name, out var saved))
                        {
                            switch (pvm.Kind)
                            {
                                case NodeParameterKind.Seed:
                                case NodeParameterKind.Integer:
                                    pvm.IntValue = saved is int iv ? iv : (int)(double)saved;
                                    break;
                                case NodeParameterKind.Boolean:
                                    pvm.BoolValue = saved is bool bv && bv;
                                    break;
                                case NodeParameterKind.Choice:
                                    pvm.SelectedChoice = saved as string ?? "";
                                    break;
                                case NodeParameterKind.Color:
                                    pvm.ColorValue = saved is System.Windows.Media.Color cv ? cv : System.Windows.Media.Colors.White;
                                    break;
                                case NodeParameterKind.Text:
                                    pvm.TextValue = saved as string ?? "";
                                    break;
                                case NodeParameterKind.PointList:
                                    if (saved is System.Collections.Generic.List<System.Windows.Point> pl)
                                    {
                                        pvm.PointListValue.Clear();
                                        foreach (var pt in pl) pvm.PointListValue.Add(pt);
                                    }
                                    break;
                                default:
                                    pvm.NumberValue = saved is double dv ? dv : 0.0;
                                    break;
                            }
                        }
                        pvm.PropertyChanged += NodeParameter_PropertyChanged;
                        node.Parameters.Add(pvm);
                    }
                }
                // Force refresh all Choice ComboBox display by cycling SelectedChoice
                foreach (var p in node.Parameters)
                {
                    if (p.Kind == NodeParameterKind.Choice)
                        p.ForceRefreshChoiceDisplay();
                }
            }
        }


        private bool _forceThumbnailRefresh;
        private bool _pendingShowStatus;
        // Timestamp (Stopwatch ticks) of the last time a preview render was actually fired.
        private long _lastPreviewFireTimestamp;
        // Maximum rate at which interactive (slider drag) preview renders are triggered.
        private const int PreviewThrottleMs = 50;
        private CancellationTokenSource? _previewCts;
        private int _previewGeneration;
        private CancellationTokenSource? _nodePreviewCts;
        private int _nodePreviewGeneration;
        // Debounce timer for node preview updates — merges rapid batch calls (e.g. AI tool calls) into one render
        private DispatcherTimer? _nodePreviewDebounceTimer;
        private const int NodePreviewDebounceMs = 300;
        // Whether to overlay a per-pixel grid on the real-time preview.
        private bool _showPreviewPixelGrid = false;
        private double _nodeCanvasScale = 1d;
        private const double NodeCanvasMinScale = 0.2d;
        private const double NodeCanvasMaxScale = 3.0d;
        private double _nodeCanvasOffsetX = 0d;
        private double _nodeCanvasOffsetY = 0d;
        private double _nodeCanvasWidth = 1600d;
        private double _nodeCanvasHeight = 1200d;
        private bool _isNodePanning;
        private Point _nodePanStart;
        private double _nodePanHorizontalOffset;
        private double _nodePanVerticalOffset;
        private NodeViewModel? _draggingNode;
        private Point _nodeDragStart;
        private double _nodeDragOriginX;
        private double _nodeDragOriginY;
        private Point _nodeDragStartHost;
        private double _nodeDragStartScale = 1d;
        private List<(NodeViewModel Node, double OriginX, double OriginY)>? _draggingNodes;
        private int _lastNodeDragUpdateTick;
        private const int NodeDragThrottleMs = 16;
        private int _frameCount;
        private int _nodeCanvasFps;
        private long _lastRenderTimestamp;
        private double _frameRenderMs;
        private double _lastGenerationLatencyMs;
        private long _lastGenerationTimestamp;
        private long _renderAccumulatorStart;
        private int _renderAccumulatorCount;
        private int _framesSinceLastFpsUpdate;
        private long _lastFpsUpdateTimestamp;
        private NodeConnectionViewModel? _activeConnection;
        private NodeConnectionViewModel? _selectedConnection;
        private NodeViewModel? _snappedNode;
        private int _snappedPortIndex = -1;
        private NodeViewModel? _activeInputNode;
        private int _activeInputPortIndex = -1;
        private bool _isMarqueeSelecting;
        private Point _marqueeStartViewport;
        private Point _marqueeStartHost;
        private Point _marqueeStartContent;

        private bool _isSyncing = false;
        private NodeViewModel? _selectedNode;
        private PreviewMode _previewMode = PreviewMode.Still;

        // Still mode frame slider: overrides AnimationTime when set (0=first frame, 1=last frame)
        // -1 means disabled (no frame override).
        private float _stillFrameNormalizedTime = -1f;
        private int _stillFrameCount = 1;
        private bool _stillFrameSliderActive;

        // Still mode comparison: when true, split-preview shows selected vs terminal
        private bool _comparisonModeEnabled;
        private BitmapSource? _comparisonTerminalBitmap;

        // If true we paused node GPU previews due to an interactive canvas operation
        private bool _nodePreviewsPausedByInteraction;
        // Internal clipboard for node copy/paste
        private ProjectFileService.ProjectData? _nodeClipboard;

        private NodeLibraryCategory? _selectedNodeCategory;
        private Point _lastNodeCanvasRightClick;

        public ObservableCollection<NodeViewModel> Nodes { get; } = new();

        private readonly Dictionary<NodeViewModel, TileProperties?> _nodeTilePropertiesMap = new();
        private readonly Dictionary<NodeViewModel, PropertyChangedEventHandler> _nodeTilePropertiesHandlers = new();

        public ObservableCollection<NodeConnectionViewModel> NodeConnections { get; } = new();
        public ICollectionView? NodeCanvasView { get; private set; }
        public ICollectionView? NodeConnectionsView { get; private set; }
        public ObservableCollection<NodeLibraryItem> NodeLibrary { get; } = new();
        public ObservableCollection<NodeLibraryEntry> NodeLibraryDisplay { get; } = new();
        public ObservableCollection<NodeLibraryCategory> NodeLibraryCategories { get; } = new();
        public ICollectionView NodeLibraryView { get; private set; } = null!;
        public ObservableCollection<TemplateFileInfo> TemplateFiles { get; } = new();
        public ObservableCollection<VariationPreviewItem> VariationPreviews { get; } = new();

        // AI integration
        public AiChatViewModel AiChat { get; } = new();
        private AiService? _aiService;
        private AiToolService? _aiToolService;
        private PixelAgentService? _pixelAgentService;
        private PlanManager? _planManager;
        private string? _currentPlanFilePath;
        private bool _aiHistorySidebarOpen;

        // ── 会话管理 ──
        private string? _currentSessionId;
        private int _userMessageCount;           // 用于自动命名
        private DispatcherTimer? _thinkingDotsTimer;
        private DispatcherTimer? _aiLiveStatusTimer;
        private int _thinkingDotCount;
        private DateTime _aiLiveStatusStartedAt;

        // 流式输出限速渲染：后台token写队列，UI timer批量flush避免每token刷新
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _aiTextBuffer = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _aiReasoningBuffer = new();
        private DispatcherTimer? _aiRenderTimer;

        private static readonly Color[] FlowerColorCandidates =
        {
            Color.FromRgb(255, 99, 132),
            Color.FromRgb(255, 159, 64),
            Color.FromRgb(255, 205, 86),
            Color.FromRgb(166, 226, 46),
            Color.FromRgb(75, 192, 192),
            Color.FromRgb(54, 162, 235),
            Color.FromRgb(153, 102, 255),
            Color.FromRgb(255, 120, 203),
            Color.FromRgb(255, 140, 105),
            Color.FromRgb(255, 180, 162),
            Color.FromRgb(247, 255, 174),
            Color.FromRgb(197, 255, 189)
        };

        private void OpenShapeDrawingWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not NodeParameterViewModel param)
                return;

            // Create the shape drawing window using the currently selected tile size so
            // the logical canvas matches the project's tile resolution.
            var canvasSize = GetSelectedTileSize();
            var window = new ShapeDrawingWindow(param.PointListValue, canvasSize) { Owner = this };
            if (window.ShowDialog() == true && window.ResultPoints != null)
            {
                param.PointListValue.Clear();
                foreach (var pt in window.ResultPoints)
                    param.PointListValue.Add(pt);
                // Trigger property changed to refresh preview
                param.PointListValue = new System.Collections.ObjectModel.ObservableCollection<System.Windows.Point>(param.PointListValue);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow { Owner = this };
            dlg.ShowDialog();
        }

        // ─── File Attachments for AI Chat ──────────────────────────────
        private sealed record AttachmentItem(string FileName, string MediaType, byte[] Data);

        private readonly List<AttachmentItem> _aiAttachments = new();

        private void AiAttachFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files|*.*",
                Multiselect = true,
                Title = Loc.GetString("MW_SelectFileToUpload")
            };
            if (dlg.ShowDialog(this) != true) return;

            foreach (var filePath in dlg.FileNames)
            {
                try
                {
                    var data = File.ReadAllBytes(filePath);
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    var mediaType = ext switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".bmp" => "image/bmp",
                        ".webp" => "image/webp",
                        _ => "application/octet-stream"
                    };
                    _aiAttachments.Add(new AttachmentItem(Path.GetFileName(filePath), mediaType, data));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiAttachFile] Failed to load attachment '{filePath}': {ex.Message}");
                }
            }

            UpdateAttachmentUI();
        }

        private void AiRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement fe && fe.Tag is AttachmentItem item)
            {
                _aiAttachments.Remove(item);
                UpdateAttachmentUI();
            }
        }

        private void UpdateAttachmentUI()
        {
            AiAttachmentList.ItemsSource = null;
            if (_aiAttachments.Count > 0)
            {
                AiAttachmentList.ItemsSource = _aiAttachments.ToList();
                AiAttachmentList.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                AiAttachmentList.Visibility = System.Windows.Visibility.Collapsed;
            }

            UpdateAiSendButtonState();
        }

        // ─── AI Integration Handlers ────────────────────────────────────

        private void EnsureAiConnected()
        {
            if (_pixelAgentService != null) return; // already connected

            var settings = AiConfigManager.Current.Settings;
            settings.Normalize();
            if (settings.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                var dlg = new AiSettingsWindow { Owner = this };
                if (dlg.ShowDialog() != true) return;
                settings = AiConfigManager.Current.Settings;
                settings.Normalize();
                if (settings.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey)) return;
            }

            DoConnectAi(settings);
        }

        private void UpdateAiSendButtonState()
        {
            if (AiSendButton == null)
                return;

            var hasInput = !string.IsNullOrWhiteSpace(AiInputBox?.Text) || _aiAttachments.Count > 0;
            AiSendButton.IsEnabled = !AiChat.IsProcessing && hasInput;

            if (AiStopButton != null)
                AiStopButton.Visibility = AiChat.IsProcessing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetAiThinkingIndicator()
        {
            if (AiThinkingDots == null)
            {
                return;
            }

            _thinkingDotsTimer?.Stop();
            AiThinkingDots.Visibility = Visibility.Collapsed;
            AiThinkingDots.Text = string.Empty;
        }

        private void SetAiLiveStatus(string status)
        {
            var startedAt = DateTime.Now;
            _aiLiveStatusStartedAt = startedAt;
            AiChat.LiveStatusText = status;

            _aiLiveStatusTimer?.Stop();
            _aiLiveStatusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            {
                var seconds = Math.Max(0, (DateTime.Now - _aiLiveStatusStartedAt).TotalSeconds);
                AiChat.LiveStatusText = $"{status} · {seconds:0.0}s";
            }, Dispatcher);
            _aiLiveStatusTimer.Start();
        }

        private void ResetAiLiveStatus(string status = "Standby")
        {
            _aiLiveStatusTimer?.Stop();
            _aiLiveStatusTimer = null;
            AiChat.LiveStatusText = status;
        }

        private void FinalizeAiStreamingMessages()
        {
            // 先把队列中所有剩余token立即flush到消息的 _pendingBuffer
            FlushAiBuffers();

            // 强制把消息内部 _pendingBuffer 也 flush 到 _contentBuilder
            // （消息的 _flushTimer 是 Background 优先级，可能在 Normal 优先级回调之后才执行）
            var assistant = AiChat.LastAssistantMessage;
            var reasoning = AiChat.LastReasoningMessage;
            assistant?.FlushToContent();
            reasoning?.FlushToContent();

            if (assistant != null)
            {
                assistant.StopFlushTimer();
                assistant.IsStreaming = false;

                // 移除没有任何可见文字内容的空 AI 气泡（仅有Tools调用时 content 为空）
                if (string.IsNullOrWhiteSpace(assistant.Content))
                    AiChat.Messages.Remove(assistant);
            }

            if (reasoning != null)
            {
                reasoning.StopFlushTimer();
                reasoning.IsStreaming = false;

                // 同样清理空的推理气泡
                if (string.IsNullOrWhiteSpace(reasoning.Content))
                    AiChat.Messages.Remove(reasoning);
            }

            // 清理任何残留的空消息（Tools调用轮次间产生的Blank气泡）
            for (int i = AiChat.Messages.Count - 1; i >= 0; i--)
            {
                if (AiChat.Messages[i] is ChatMessageViewModel msg &&
                    msg.Role is "AI" or "Think" && !msg.IsStreaming &&
                    string.IsNullOrWhiteSpace(msg.Content))
                {
                    AiChat.Messages.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 每80ms由 _aiRenderTimer 触发：从队列消费token并追加到当前消息，每轮最多一次UI通知。
        /// </summary>
        private void AiRenderTimer_Tick(object? sender, EventArgs e)
        {
            FlushAiBuffers();
        }

        private void FlushAiBuffers()
        {
            // --- 处理推理token ---
            if (!_aiReasoningBuffer.IsEmpty)
            {
                if (AiThinkingDots != null)
                {
                    AiThinkingDots.Visibility = Visibility.Visible;
                    if (!(_thinkingDotsTimer?.IsEnabled ?? false))
                    {
                        _thinkingDotCount = 0;
                        AiThinkingDots.Text = "Thinking...";
                        _thinkingDotsTimer?.Start();
                    }
                }
                AiChat.StatusText = "AI is thinking...";

                var reasoning = AiChat.LastReasoningMessage;
                if (reasoning == null || !reasoning.IsStreaming)
                {
                    reasoning = AiChat.AddReasoningMessage(string.Empty);
                    reasoning.IsStreaming = true;
                    reasoning.StartFlushTimer(80);
                }

                while (_aiReasoningBuffer.TryDequeue(out var token))
                    reasoning.AppendBuffered(token);

                // 不直接Flush：由消息自身的DispatcherTimer控制节奏
            }

            // --- 处理回复token ---
            if (!_aiTextBuffer.IsEmpty)
            {
                ResetAiThinkingIndicator();
                AiChat.StatusText = "AI is replying";

                var reasoning = AiChat.LastReasoningMessage;
                if (reasoning != null && reasoning.IsStreaming)
                    reasoning.IsStreaming = false;

                var assistant = AiChat.LastAssistantMessage;
                if (assistant == null || !assistant.IsStreaming)
                {
                    assistant = AiChat.AddAssistantMessage(string.Empty);
                    assistant.IsStreaming = true;
                    assistant.StartFlushTimer(80);
                }

                while (_aiTextBuffer.TryDequeue(out var token))
                    assistant.AppendBuffered(token);

                // 回复期间温和地滚动（不每token滚，每80ms最多一次）
                AiChatScrollViewer?.ScrollToBottom();
            }
        }

        private void SyncAiCombosFromSettings()
        {
            // Sync reasoning effort combo from saved settings
            var savedEffort = AiConfigManager.Current.Settings.ReasoningEffort;
            for (int i = 0; i < AiReasoningCombo.Items.Count; i++)
            {
                if (AiReasoningCombo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                    item.Tag?.ToString() == savedEffort)
                {
                    AiReasoningCombo.SelectedIndex = i;
                    break;
                }
            }

            // Sync model switch combo
            SyncModelSwitchCombo();
        }

        private bool _isSyncingModelSwitch;

        private void SyncModelSwitchCombo()
        {
            if (_isSyncingModelSwitch) return;
            _isSyncingModelSwitch = true;
            try
            {
                var settings = AiConfigManager.Current.Settings;
                settings.Normalize();

                AiModelSwitchCombo.Items.Clear();
                foreach (var m in settings.Models)
                    AiModelSwitchCombo.Items.Add(m);

                // Select current model
                for (int i = 0; i < AiModelSwitchCombo.Items.Count; i++)
                {
                    if (AiModelSwitchCombo.Items[i] is string s && s == settings.Model)
                    {
                        AiModelSwitchCombo.SelectedIndex = i;
                        return;
                    }
                }
                // Fallback: add current model if not in list
                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    if (!settings.Models.Contains(settings.Model))
                        settings.Models.Add(settings.Model);
                    AiModelSwitchCombo.Items.Add(settings.Model);
                    AiModelSwitchCombo.SelectedIndex = AiModelSwitchCombo.Items.Count - 1;
                }
            }
            finally
            {
                _isSyncingModelSwitch = false;
            }
        }

        private void AiModelSwitchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingModelSwitch) return;
            if (AiModelSwitchCombo.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
            {
                var settings = AiConfigManager.Current.Settings;
                if (settings.Model != model)
                {
                    settings.Model = model;
                    // Ensure the model is in the Models list
                    if (!settings.Models.Contains(model))
                        settings.Models.Insert(0, model);
                    settings.ActiveModelIndex = settings.Models.IndexOf(model);
                    AiConfigManager.Current.Save();
                }
            }
        }

        private void DoConnectAi(AiSettings settings)
        {
            settings.Normalize();
            AiChat.Messages.Clear();
            AiPlanViewer.Plan = null;
            SyncAiCombosFromSettings();
            _aiService?.Dispose();
            _aiService = new AiService();

            // Core services
            var skillService = new SkillService();
            var dynamicNodeService = new DynamicNodeService();
            var historyManager = new ConversationHistoryManager();

            // Learning system
            var experienceDb = new Services.Learning.ExperienceDb();
            var userProfile = new Services.Learning.UserProfileService();
            var experienceTracker = new Services.Learning.ExperienceTracker(experienceDb, userProfile);
            var fewShotSelector = new Services.Learning.FewShotSelector(experienceDb);

            // Tool service
            _aiToolService = new AiToolService();

            // 画布操作提供者（节点/连接/参数修改 + 画布查询）
            var graphProvider = new GraphToolProvider(
                _nodeGraphController,
                Nodes,
                NodeConnections,
                CreateNodeFromLibraryItem,
                Dispatcher,
                NodeLibrary.ToList())
            {
                CurrentTileSize = GetSelectedTileSize()
            };
            _aiToolService.RegisterProvider(graphProvider);

            // 美学评估提供者（从 GraphToolProvider 分离）
            var aestheticProvider = new AestheticToolProvider
            {
                GetPreviewBitmap = () => _lastGenerated
            };
            _aiToolService.RegisterProvider(aestheticProvider);
            var skillToolProvider = new SkillToolProvider(skillService);
            skillToolProvider.OnUseSkill = async (skillId, skillName, serializedGraph, x, y) =>
            {
                try
                {
                    return ImportSkillGraph(serializedGraph, x, y);
                }
                catch { return false; }
            };
            _aiToolService.RegisterProvider(skillToolProvider);
            _aiToolService.RegisterProvider(new DynamicNodeToolProvider(dynamicNodeService));

            // Recipe/Effect generation provider
            var recipeProvider = new RecipeToolProvider(skillService, () =>
            {
                if (_lastGenerated == null) return null;
                try
                {
                    int w = _lastGenerated.PixelWidth;
                    int h = _lastGenerated.PixelHeight;
                    var buf = new Core.PixelBuffer(w, h);
                    var stride = w * 4;
                    var pixels = new byte[stride * h];
                    _lastGenerated.CopyPixels(pixels, stride, 0);
                    var dst = buf.AsSpan();
                    for (int i = 0; i < dst.Length && i < pixels.Length; i++)
                        dst[i] = pixels[i] / 255f;
                    return buf;
                }
                catch { return null; }
            });
            recipeProvider.OnApplyRecipe = (recipeJson, x, y) =>
            {
                bool applied = false;
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var recipe = System.Text.Json.JsonSerializer.Deserialize<Models.EffectRecipe>(recipeJson);
                        if (recipe != null)
                        {
                            // 包装创建函数：从类型名查找 NodeLibraryItem
                            NodeViewModel? CreateNodeFromType(string typeName, double nx, double ny)
                            {
                                var libItem = NodeLibrary.FirstOrDefault(i =>
                                    string.Equals(i.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(i.Name, typeName, StringComparison.OrdinalIgnoreCase));
                                if (libItem == null)
                                {
                                    // 尝试部分匹配
                                    libItem = NodeLibrary.FirstOrDefault(i =>
                                        i.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase) ||
                                        typeName.Contains(i.Name, StringComparison.OrdinalIgnoreCase));
                                }
                                if (libItem == null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Recipe] Node type not found: {typeName}");
                                    return null;
                                }
                                return CreateNodeFromLibraryItem(libItem, nx, ny);
                            }

                            applied = recipe.ApplyToController(
                                _nodeGraphController, Nodes, NodeConnections,
                                CreateNodeFromType, x, y);
                            UpdateNodeCanvasExtent();
                            NodeConnectionsView?.Refresh();
                            RequestPreviewRefresh(false);
                            System.Diagnostics.Debug.WriteLine($"[RecipeToolProvider] Applied recipe '{recipe.Name}' with {recipe.Nodes.Count} nodes");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RecipeToolProvider] Apply failed: {ex.Message}");
                    }
                });
                return applied;
            };
            _aiToolService.RegisterProvider(recipeProvider);

            // Initialize NodeResourceRegistry with file source
            var defaultNodesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nodes");
            var customPath = SettingsService.Current.CustomNodesPath;
            var nodesDir = string.IsNullOrEmpty(customPath) ? defaultNodesDir : customPath;
            var fileNodeSource = new FileNodeSource(nodesDir);
            var registry = NodeResourceRegistry.Instance;
            registry.AddSource(new BuiltInNodeSource());
            registry.AddSource(fileNodeSource);
            registry.AddSource(new DynamicNodeSource(dynamicNodeService));
            // Register fallback so Core's GraphNodeRegistry can resolve nodes from Services without a reverse dependency
            Core.GraphNodeRegistry.NodeFactoryFallback = typeName => registry.Create(typeName);
            _aiToolService.RegisterProvider(new ResourceNodeToolProvider(registry, fileNodeSource, dynamicNodeService));

            // 注册计划管理工具提供者（仅提供 get_plan，无 update_plan）
            var planManager = new PlanManager();
            _planManager = planManager;
            _aiToolService.RegisterProvider(new PlanToolProvider(planManager));

            _pixelAgentService = new PixelAgentService(
                _aiService,
                _aiToolService,
                new Services.AiContextBuilder(),
                historyManager,
                settings,
                planManager);

            // 传递节点库给 AI，让 AI 知道有哪些真实节点可用
            _pixelAgentService.SetNodeLibrary(NodeLibrary);

            // Thinking dots timer
            _thinkingDotsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) =>
            {
                if (AiThinkingDots == null) return;
                _thinkingDotCount = (_thinkingDotCount + 1) % 4;
                AiThinkingDots.Text = "Thinking..." + new string('.', _thinkingDotCount);
            }, Dispatcher);

            // 流式渲染Timer：120ms批量flush token队列到UI，避免高频DOM更新卡死
            // 使用 Background 优先级：高于 SystemIdle，确保不会被无限延迟
            // （当后台任务完成后，立即响应完成事件的 Normal 优先级调度）
            if (_aiRenderTimer != null)
            {
                _aiRenderTimer.Stop();
                _aiRenderTimer.Tick -= AiRenderTimer_Tick;
            }
            _aiRenderTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _aiRenderTimer.Tick += AiRenderTimer_Tick;
            _aiRenderTimer.Start();

            // Wire events
            _pixelAgentService.OnTextDelta += delta =>
            {
                // 非UI线程：写入线程安全队列，由UI线程的DispatcherTimer批量消费
                _aiTextBuffer.Enqueue(delta);
            };
            _pixelAgentService.OnReasoningDelta += delta =>
            {
                // 非UI线程：写入推理专用队列
                _aiReasoningBuffer.Enqueue(delta);
            };
            _pixelAgentService.OnStatusMessage += status =>
            {
                var console = ServiceLocator.TryGetService<IConsoleService>();
                console?.LogInfo(status, "AI");
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    AiChat.StatusText = status;
                    AiChat.AddOrUpdateStatusMessage(status);
                    if (AiThinkingDots != null)
                    {
                        _thinkingDotsTimer?.Stop();
                        AiThinkingDots.Visibility = Visibility.Collapsed;
                    }
                });
            };
            _pixelAgentService.OnLiveStatus += status =>
            {
                _ = SafeDispatcherInvokeAsync(() => SetAiLiveStatus(status));
            };

            // ── 工具调用状态事件：在对话中创建/更新工具调用气泡 ──
            _pixelAgentService.OnToolGroupStarted += groupInfo =>
            {
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    var group = AiChat.AddToolCallGroup();
                    foreach (var (callId, toolName, argsPreview) in groupInfo)
                    {
                        group.AddStep(toolName, argsPreview, callId);
                    }
                    AiChatScrollViewer?.ScrollToBottom();
                });
            };
            _pixelAgentService.OnToolStepChanged += (callId, status, resultOrError) =>
            {
                var toolName = "";
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    var group = AiChat.LastToolCallGroup;
                    if (group == null) return;

                    // 找到对应 callId 的 step
                    ToolCallStepViewModel? step = null;
                    foreach (var s in group.Steps)
                    {
                        if (s.CallId == callId)
                        {
                            step = s;
                            break;
                        }
                    }
                    if (step == null) return;
                    toolName = step.ToolName;

                    step.Status = status;
                    if (status == "success" || status == "error")
                    {
                        step.Result = resultOrError;
                    }
                    if (status == "error")
                    {
                        step.ErrorMessage = resultOrError;
                    }

                    group.CheckAndUpdateGroupStatus();
                });
                var console = ServiceLocator.TryGetService<IConsoleService>();
                if (status == "success")
                    console?.LogSuccess(toolName, "AI");
                else if (status == "error")
                    console?.LogError($"{toolName} 失败: {resultOrError}", "AI");
                else if (status == "cancelled")
                    console?.LogWarning($"{toolName} 已取消", "AI");
            };

            _pixelAgentService.OnStreamRoundEnd += () =>
            {
                _ = SafeDispatcherInvokeAsync(FinalizeAiStreamingMessages, DispatcherPriority.Background);
                // 将对话内容输出到控制台
                var console = ServiceLocator.TryGetService<IConsoleService>();
                if (console != null)
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        var assistant = AiChat.LastAssistantMessage;
                        if (assistant != null && !string.IsNullOrWhiteSpace(assistant.Content))
                        {
                            var preview = assistant.Content.Length > 300
                                ? assistant.Content[..300] + $"\n...（共{assistant.Content.Length}字符）"
                                : assistant.Content;
                            console.LogDebug($"[AI回复] {preview}", "对话");
                        }
                        var toolGroup = AiChat.LastToolCallGroup;
                        if (toolGroup != null)
                        {
                            foreach (var step in toolGroup.Steps)
                            {
                                var icon = step.Status switch
                                {
                                    "success" => "✓",
                                    "error" => "✗",
                                    "cancelled" => "—",
                                    _ => "⋯"
                                };
                                console.LogDebug($"[工具] {icon} {step.ToolName}", "对话");
                            }
                        }
                    }, DispatcherPriority.Background);
                }
            };
            _pixelAgentService.OnDone += () =>
            {
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    AiChat.IsProcessing = false;
                    AiChat.StatusText = "Ready";
                    AiChat.AddOrUpdateStatusMessage("AI 回复完成");
                    ResetAiLiveStatus();
                    ResetAiThinkingIndicator();
                    FinalizeAiStreamingMessages();
                    UpdateAiSendButtonState();
                    AiChatScrollViewer?.ScrollToBottom();

                    // AI 回复完成后自动保存会话
                    AutoSaveCurrentSession();

                    // 工具执行后刷新预览（如果有节点创建/修改）
                    // Background 优先级避免阻塞 UI 上的其他重要事件
                    RequestPreviewRefresh(false);
                }, DispatcherPriority.Background);
            };
            _pixelAgentService.OnError += error =>
            {
                var console = ServiceLocator.TryGetService<IConsoleService>();
                console?.LogError(error, "AI");
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    AiChat.AddErrorMessage(error);
                    AiChat.IsProcessing = false;
                    AiChat.StatusText = "Error";
                    ResetAiLiveStatus("Error");
                    ResetAiThinkingIndicator();
                    FinalizeAiStreamingMessages();
                    UpdateAiSendButtonState();
                    AiChatScrollViewer?.ScrollToBottom();
                });
            };
            _pixelAgentService.OnConfirmRequired += async (toolName, description) =>
            {
                var tcs = new TaskCompletionSource<bool>();
                _ = Dispatcher.InvokeAsync(() =>
                {
                    var loc = Services.Localization.LocalizationService.Instance;
                    var title = loc.GetStringFast("AI_ConfirmTitle");
                    var message = $"{loc.GetStringFast("AI_ConfirmMessage")}\n\n{description}";
                    var dlg = new DarkMessageBox(message, title, showCancel: true) { Owner = this };
                    var result = dlg.ShowDialog();
                    tcs.SetResult(result == true && dlg.Result == true);
                });

                var timeoutTask = Task.Delay(60_000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _ = Dispatcher.InvokeAsync(() =>
                        AiChat.AddErrorMessage($"确认超时（60秒），已自动拒绝：{description}"));
                    return false;
                }

                return await tcs.Task;
            };

            // ── PlanManager 事件（纯 UI 展示）──
            if (_planManager != null)
            {
                _planManager.OnPlanCreated += plan =>
                {
                    _ = SafeDispatcherInvokeAsync(() =>
                    {
                        AiPlanViewer.ActivePlan = plan;
                    });
                };
                _planManager.OnPlanUpdated += plan =>
                {
                    // 每次计划状态变更时同步更新 UI 面板，让用户能看到步骤进度
                    _ = SafeDispatcherInvokeAsync(() =>
                    {
                        AiPlanViewer.ActivePlan = plan;
                    });
                };
                _planManager.OnProgressChanged += (completed, total, desc) =>
                {
                    _ = SafeDispatcherInvokeAsync(() => RequestPreviewRefresh(false));
                };
                _planManager.OnPlanCompleted += plan =>
                {
                    _ = SafeDispatcherInvokeAsync(() => RequestPreviewRefresh(true));
                };
            }

            // ── 计划检测事件 ──
            _pixelAgentService.OnPlanDetected += (planText, planFilePath) =>
            {
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    var plan = Services.MdPlanParser.ExtractPlan(planText);
                    if (plan != null)
                    {
                        AiPlanViewer.Plan = plan;
                        AiPlanTitleText.Text = plan.Title ?? "Task Plan";
                    }
                    else
                    {
                        var layered = Services.MdPlanParser.ExtractLayeredPlan(planText);
                        if (layered != null)
                        {
                            AiPlanViewer.LayeredPlan = layered;
                            AiPlanTitleText.Text = layered.Title ?? "Plan";
                        }
                    }

                    AiPlanPanel.Visibility = System.Windows.Visibility.Visible;
                    AiPlanCollapsedBar.Visibility = System.Windows.Visibility.Collapsed;
                });
            };

            // ── 阶段变更事件 ──
            _pixelAgentService.OnPhaseChanged += phaseText =>
            {
                _ = SafeDispatcherInvokeAsync(() =>
                {
                    AiChat.StatusText = phaseText;
                });
            };

            AiChat.IsConnected = true;
            AiChat.StatusText = "Connected";
            UpdateAiSendButtonState();
            AiChat.AddSystemMessage("Connected to AI service. Describe the pixel assets you want, and I'll help you build a node graph.");
        }

        private async void AiSendButton_Click(object sender, RoutedEventArgs? e)
        {
            if (AiChat.IsProcessing)
            {
                return;
            }

            // Auto-connect if not connected
            if (_pixelAgentService == null)
            {
                // 先把 combo 当前值同步到 settings，避免 EnsureAiConnected→DoConnectAi→SyncAiCombosFromSettings 覆盖 combo
                if (AiReasoningCombo.SelectedItem is System.Windows.Controls.ComboBoxItem preReasonItem)
                {
                    AiConfigManager.Current.Settings.ReasoningEffort = preReasonItem.Tag?.ToString() ?? "off";
                }
                EnsureAiConnected();
                if (_pixelAgentService == null) return;
                // 连接后 combo 可能已被 SyncAiCombosFromSettings 重置，重新同步回来
                if (AiReasoningCombo.SelectedItem is System.Windows.Controls.ComboBoxItem postReasonItem)
                {
                    AiConfigManager.Current.Settings.ReasoningEffort = postReasonItem.Tag?.ToString() ?? "off";
                }
            }

            var text = AiInputBox.Text.Trim();
            if (string.IsNullOrEmpty(text) && _aiAttachments.Count == 0) return;

            // Sync reasoning effort to active settings
            if (AiReasoningCombo.SelectedItem is System.Windows.Controls.ComboBoxItem reasonItem)
            {
                AiConfigManager.Current.Settings.ReasoningEffort = reasonItem.Tag?.ToString() ?? "off";
            }
            AiConfigManager.Current.Save();

            // Clear previous plan
            AiPlanViewer.Plan = null;

            // Convert attachments to AI file attachments
            List<Services.AiFileAttachment>? fileAttachments = null;
            if (_aiAttachments.Count > 0)
            {
                fileAttachments = _aiAttachments
                    .Select(a => new Services.AiFileAttachment(a.FileName, a.MediaType, a.Data))
                    .ToList();
            }

            AiInputBox.Text = "";
            _aiAttachments.Clear();
            UpdateAttachmentUI();

            AiChat.AddUserMessage(text);
            var dialogConsole = ServiceLocator.TryGetService<IConsoleService>();
            var userPreview = text.Length > 300 ? text[..300] + "…" : text;
            dialogConsole?.LogDebug($"[用户] {userPreview}", "对话");

            // ── 会话管理：分配 ID + 自动保存 ──
            _currentSessionId ??= Guid.NewGuid().ToString("N")[..12];
            _userMessageCount++;
            // 第一次发送消息时触发自动保存和标题更新
            if (_userMessageCount == 1)
            {
                AutoSaveCurrentSession();
                // 显示标题
                var title = text.Length > 40 ? text[..40] + "…" : text;
                AiSessionTitle.Text = title;
                RefreshAiSessionList();
            }

            AiChat.IsProcessing = true;
            AiChat.StatusText = "Waiting for response";
            SetAiLiveStatus("Submitting to background thread");
            UpdateAiSendButtonState();
            try { AiChatScrollViewer?.ScrollToBottom(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AiSendButton] ScrollToBottom failed: {ex.Message}"); }

            await Dispatcher.Yield(DispatcherPriority.Background);

            // Show thinking animation
            if (AiThinkingDots != null)
            {
                _thinkingDotCount = 0;
                AiThinkingDots.Text = "Thinking...";
                AiThinkingDots.Visibility = Visibility.Visible;
                _thinkingDotsTimer?.Start();
            }

            try
            {
                // 读取权限模式并同步给 Agent
                if (AiPermissionCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem permItem)
                {
                    var tag = permItem.Tag?.ToString() ?? "Execute";
                    _pixelAgentService.PermissionMode = tag switch
                    {
                        "SkipPermissions" => Models.AiPermissionMode.SkipPermissions,
                        _ => Models.AiPermissionMode.Execute,
                    };
                }

                // 关键修复：使用 ConfigureAwait(false) 避免在 UI 线程返回后立即处理高优先级回调
                // 后台线程完成后让 event handlers 在 background 优先级执行
                await Task.Run(async () => 
                {
                    try
                    {
                        await _pixelAgentService.SendMessageAsync(text, fileAttachments).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 用户Cancel或超时
                        throw;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    AiChat.AddErrorMessage("AI response cancelled");
                    AiChat.IsProcessing = false;
                    AiChat.StatusText = "Cancelled";
                    ResetAiLiveStatus("Cancelled");
                    ResetAiThinkingIndicator();
                    FinalizeAiStreamingMessages();
                    UpdateAiSendButtonState();
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    AiChat.AddErrorMessage($"Error ({ex.GetType().Name}): {ex.Message}");
                    AiChat.IsProcessing = false;
                    AiChat.StatusText = "Error";
                    ResetAiLiveStatus("Error");
                    ResetAiThinkingIndicator();
                    FinalizeAiStreamingMessages();
                    UpdateAiSendButtonState();
                }, DispatcherPriority.Background);
            }
            finally
            {
                // 保底：确保无论OnDone是否已触发，按钮状态都正确恢复
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (!AiChat.IsProcessing)
                    {
                        ResetAiLiveStatus();
                        ResetAiThinkingIndicator();
                        FinalizeAiStreamingMessages();
                        UpdateAiSendButtonState();
                    }
                }, DispatcherPriority.Background);
            }

            try { AiChatScrollViewer?.ScrollToBottom(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AiSendButton] ScrollToBottom failed: {ex.Message}"); }
        }

        private void AiInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateAiSendButtonState();
        }

        private void AiInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                AiSendButton_Click(sender, null);
            }
        }

        private void AiReasoningToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ChatMessageViewModel message } && message.IsReasoning)
            {
                message.ToggleCollapsed();
            }
        }

        private void AiReasoningHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ChatMessageViewModel message } && message.IsReasoning)
            {
                message.ToggleCollapsed();
                e.Handled = true;
            }
        }

        private void AiCopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string text } && !string.IsNullOrEmpty(text))
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch { }
            }
        }

        private void AiSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiSettingsWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // Disconnect existing session if settings changed
                if (_pixelAgentService != null)
                {
                    _pixelAgentService = null;
                    _aiService?.Dispose();
                    _aiService = null;
                    _aiToolService = null;
                    AiChat.Messages.Clear();
                    AiChat.IsConnected = false;
                    AiChat.StatusText = "Disconnected";
                    ResetAiLiveStatus("Disconnected");
                    ResetAiThinkingIndicator();
                }
                // Auto-connect if API key is configured
                var settings = AiConfigManager.Current.Settings;
                settings.Normalize();
                if (!settings.RequiresApiKey || !string.IsNullOrWhiteSpace(settings.ApiKey))
                    DoConnectAi(settings);
                else
                    UpdateAiSendButtonState();
            }
        }

        private void AiNewChat_Click(object sender, RoutedEventArgs e)
        {
            if (_pixelAgentService == null)
            {
                EnsureAiConnected();
                if (_pixelAgentService == null) return;
            }

            // 自动保存当前会话（如果有消息）
            AutoSaveCurrentSession();

            _pixelAgentService.ClearHistory();
            AiChat.Messages.Clear();
            AiChat.IsConnected = true;
            AiChat.StatusText = "Connected";
            ResetAiLiveStatus();
            ResetAiThinkingIndicator();
            UpdateAiSendButtonState();

            // 重置会话 ID
            _currentSessionId = null;
            _userMessageCount = 0;

            // ResetPlan面板
            _currentPlanFilePath = null;
            AiPlanPanel.Visibility = System.Windows.Visibility.Collapsed;
            AiPlanCollapsedBar.Visibility = System.Windows.Visibility.Collapsed;
            AiSessionTitle.Text = Loc.GetString("MW_NewConversation");

            // 刷新历史列表
            RefreshAiSessionList();

            AiChat.AddSystemMessage("New conversation started. Describe the pixel assets you want, and I'll help you build a node graph.");
        }

        /// <summary>
        /// 自动保存当前会话（如果历史中有消息且 AI 已初始化）。
        /// </summary>
        private void AutoSaveCurrentSession()
        {
            if (_pixelAgentService == null) return;
            var history = _pixelAgentService.GetHistory();
            if (history.Count == 0) return;

            // Use existing session ID or create new one
            _currentSessionId ??= Guid.NewGuid().ToString("N")[..12];

            // Generate title from first user message
            string title = "新对话";
            foreach (var msg in history)
            {
                if (msg.Role == "user" && !string.IsNullOrEmpty(msg.Content))
                {
                    title = msg.Content.Length > 40
                        ? msg.Content[..40] + "…"
                        : msg.Content;
                    break;
                }
            }

            _pixelAgentService.SaveSession(_currentSessionId, title, _currentPlanFilePath);
        }

        private void AiHistoryToggle_Click(object sender, RoutedEventArgs e)
        {
            _aiHistorySidebarOpen = !_aiHistorySidebarOpen;
            AiHistorySidebarCol.Width = _aiHistorySidebarOpen
                ? new System.Windows.GridLength(220)
                : new System.Windows.GridLength(0);
            if (_aiHistorySidebarOpen)
                RefreshAiSessionList();
        }

        private void AiOpenPlanFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPlanFilePath) || !System.IO.File.Exists(_currentPlanFilePath))
                return;
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_currentPlanFilePath}\""); }
            catch { }
        }

        private void AiCollapsePlan_Click(object sender, RoutedEventArgs e)
        {
            AiPlanPanel.Visibility = System.Windows.Visibility.Collapsed;
            AiPlanCollapsedBar.Visibility = System.Windows.Visibility.Visible;
        }

        private void AiExpandPlan_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AiPlanPanel.Visibility = System.Windows.Visibility.Visible;
            AiPlanCollapsedBar.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void AiStopButton_Click(object sender, RoutedEventArgs e)
        {
            _pixelAgentService?.CancelStreaming();
            AiStopButton.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void RefreshAiSessionList()
        {
            if (_pixelAgentService == null) return;
            try
            {
                var sessions = _pixelAgentService.GetAllSessionSummaries();
                AiSessionList.Sessions = sessions;
                AiSessionList.ActiveSessionId = _currentSessionId;
            }
            catch { }
        }

        /// <summary>
        /// 从历史消息中提取标题更新会话标题 UI。
        /// </summary>
        private void UpdateAiSessionTitle()
        {
            if (_pixelAgentService == null) return;
            var history = _pixelAgentService.GetHistory();
            foreach (var msg in history)
            {
                if (msg.Role == "user" && !string.IsNullOrEmpty(msg.Content))
                {
                    var title = msg.Content.Length > 40
                        ? msg.Content[..40] + "…"
                        : msg.Content;
                    AiSessionTitle.Text = title;
                    return;
                }
            }
            AiSessionTitle.Text = Loc.GetString("MW_NewConversation");
        }

        private void FlowerColorEntry_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var entry = fe?.DataContext as TileProperties.FlowerColorEntry;
            if (entry == null) return;

            var original = entry.Color;
            var wnd = new ColorWheelWindow(original) { Owner = this };
            void OnColorChanged(System.Windows.Media.Color c)
            {
                entry.Color = c;
                RequestPreviewRefresh(false);
            }

            wnd.ColorChanged += OnColorChanged;
            var res = wnd.ShowDialog();
            if (res == true)
            {
                entry.Color = wnd.SelectedColor;
                RequestPreviewRefresh(false);
            }
            else
            {
                // revert to original if cancelled
                entry.Color = original;
                RequestPreviewRefresh(false);
            }
            wnd.ColorChanged -= OnColorChanged;
        }

        private void OpenColorWheelForParameter_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var param = fe?.Tag as NodeParameterViewModel;
            if (param == null) return;

            var original = param.ColorValue;
            var wnd = new ColorWheelWindow(original) { Owner = this };
            void OnColorChanged(System.Windows.Media.Color c)
            {
                param.ColorValue = c;
                RequestPreviewRefresh(false);
            }

            wnd.ColorChanged += OnColorChanged;
            var res = wnd.ShowDialog();
            if (res == true)
            {
                param.ColorValue = wnd.SelectedColor;
                RequestPreviewRefresh(false);
            }
            else
            {
                // revert if cancelled
                param.ColorValue = original;
                RequestPreviewRefresh(false);
            }
            wnd.ColorChanged -= OnColorChanged;
        }

        // Settings functionality removed.

        private void NodeParamSeedRandom_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var param = fe?.Tag as NodeParameterViewModel;
            if (param == null) return;

            param.IntValue = Random.Shared.Next((int)param.Min, (int)param.Max + 1);
            RequestPreviewRefresh(false);
        }

        private void NodeParamReset_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var param = fe?.Tag as NodeParameterViewModel;
            if (param == null) return;

            param.ResetToDefault();
            RequestPreviewRefresh(false);
        }

        private void AddFlowerColor_Click(object? sender, RoutedEventArgs e)
        {
            var props = ActiveTileProperties;
            if (props == null) return;

            props.FlowerColors.Add(new TileProperties.FlowerColorEntry(GetRandomUnusedFlowerColor(props.FlowerColors), 1.0));
            RequestPreviewRefresh(false);
        }

        private void RemoveFlowerColor_Click(object? sender, RoutedEventArgs e)
        {
            var props = ActiveTileProperties;
            if (props == null) return;
            if (sender is Button b && b.CommandParameter is TileProperties.FlowerColorEntry entry)
            {
                props.FlowerColors.Remove(entry);
                RequestPreviewRefresh(false);
            }
        }

        private void SeedTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            e.Handled = !IsSeedTextValid(GetSeedTextAfterInput(textBox, e.Text));
        }

        private void SeedTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                e.CancelCommand();
                return;
            }

            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!IsSeedTextValid(GetSeedTextAfterInput(textBox, pastedText)))
            {
                e.CancelCommand();
            }
        }

        private void SeedTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox || !string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            var tileType = textBox.Tag is TileType taggedTileType
                ? taggedTileType
                : SelectedNode?.TileType ?? TileType.Grass;

            var fallbackSeed = ActiveTileProperties?.Seed ?? GetDefaultSeed(tileType);
            textBox.Text = fallbackSeed.ToString(CultureInfo.InvariantCulture);
        }

        // Commit TextBox.Text binding when user presses Enter
        private void TextBox_PreviewKeyDown_Commit(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox textBox)
                return;

            // Update the binding source for the Text property so the value is applied immediately
            var be = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
            be?.UpdateSource();

            // Move focus to next element to mimic typical form behavior and avoid the ding sound
            e.Handled = true;
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void SeedRandomButton_Click(object sender, RoutedEventArgs e)
        {
            var props = ActiveTileProperties;
            if (props == null)
            {
                return;
            }

            var tileType = sender is FrameworkElement { Tag: TileType taggedTileType }
                ? taggedTileType
                : SelectedNode?.TileType ?? TileType.Grass;

            props.Seed = CreateRandomSeed(tileType);
        }

        public NodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (ReferenceEquals(_selectedNode, value))
                {
                    return;
                }

                var previous = _selectedNode;
                _selectedNode = value;
                OnPropertyChanged();

                if (previous != null)
                {
                    previous.IsSelected = false;
                }

                if (_selectedNode != null)
                {
                    _selectedNode.IsSelected = true;
                }


                if (_selectedNode != null)
                {
                    EnsureNodeParametersInitialized(_selectedNode);
                    if (_selectedNode.Kind == NodeLibraryItemKind.Compute)
                    {
                        try
                        {
                            _selectedNode.PreviewBrush = GenerateComputeNodePreviewBrush(_selectedNode);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SelectedNode] Preview brush generation failed for '{_selectedNode?.Title}': {ex.Message}");
                        }
                    }

                    UpdateBuildingTilePreview();
                    UpdateVariationPreviews();
                }

                // Update status bar with node description
                if (_selectedNode != null)
                {
                    var desc = NodeLibraryService.GetNodeDescription(_selectedNode.Title);
                    StatusText.Text = !string.IsNullOrEmpty(desc) ? desc : _selectedNode.Title;
                }


                RequestPreviewRefresh(false);
                OnPropertyChanged(nameof(ActiveTileProperties));
            }
        }



        public TileProperties? ActiveTileProperties
        {
            get
            {
                return SelectedNode?.TileProperties;
            }
        }

        public ObservableCollection<TilePreviewCell> BuildingTilePreviewCells { get; } = new();

        // Converter used by XAML to display Color preview as Brush
        public class ColorToBrushConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is System.Windows.Media.Color c)
                {
                    return new SolidColorBrush(c);
                }
                return new SolidColorBrush(System.Windows.Media.Colors.Transparent);
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is SolidColorBrush b)
                {
                    return b.Color;
                }
                return System.Windows.Media.Colors.Transparent;
            }
        }


        public class TilePreviewCell
        {
            public Brush Color { get; set; } = Brushes.Transparent;
        }

        public class VariationPreviewItem
        {
            public Brush Preview { get; set; } = Brushes.Transparent;
            public int Seed { get; set; }
        }

        public NodeLibraryCategory? SelectedNodeCategory
        {
            get => _selectedNodeCategory;
            set
            {
                if (SetField(ref _selectedNodeCategory, value))
                {
                    ApplyNodeLibraryFilter();
                }
            }
        }

        /// <summary>
        /// Rebuilds the display list from the current category selection and search text,
        /// then refreshes the <see cref="NodeLibraryView"/>.
        /// </summary>
        private void ApplyNodeLibraryFilter()
        {
            var text = NodeLibrarySearchBox?.Text ?? "";
            var cat = SelectedNodeCategory;

            // Build new items as a List (no UI binding during construction)
            var newItems = NodeLibraryService.BuildNodeLibraryList(NodeLibrary, SelectedNodeCategory, text);

            // Replace the ObservableCollection contents in one batch.
            // Detach ListBox ItemsSource so WPF doesn't react to per-item changes.
            NodeLibraryList.ItemsSource = null;
            NodeLibraryDisplay.Clear();
            foreach (var item in newItems)
                NodeLibraryDisplay.Add(item);
            NodeLibraryList.ItemsSource = NodeLibraryView;
        }

        public double NodeCanvasScale
        {
            get => _nodeCanvasScale;
            set
            {
                var clamped = Math.Clamp(value, NodeCanvasMinScale, NodeCanvasMaxScale);
                SetField(ref _nodeCanvasScale, clamped);
            }
        }

        public double NodeCanvasOffsetX
        {
            get => _nodeCanvasOffsetX;
            set => SetField(ref _nodeCanvasOffsetX, value);
        }

        public double NodeCanvasOffsetY
        {
            get => _nodeCanvasOffsetY;
            set => SetField(ref _nodeCanvasOffsetY, value);
        }

        public double NodeCanvasWidth
        {
            get => _nodeCanvasWidth;
            set => SetField(ref _nodeCanvasWidth, value);
        }

        public double NodeCanvasHeight
        {
            get => _nodeCanvasHeight;
            set => SetField(ref _nodeCanvasHeight, value);
        }

        internal static SplashWindow? Splash { get; set; }

        /// <summary>
        /// Initializes all MainWindow services and data. Called from App startup flow
        /// so the splash window can display real-time progress.
        /// </summary>
        internal async Task InitializeAsync(SplashWindow splash)
        {
            Splash = splash;
            splash.ReportProgress(0.15, "正在初始化渲染引擎...", "创建图形服务");
            await Task.Delay(1);

            _graphEvalService = new GraphEvaluationService
            {
                TileNodeRenderer = TryRenderTileNodeAsPixelBuffer
            };
            _graphEvalService.EvaluationError += msg =>
            {
                try { StatusText.Text = msg; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GraphEval] Status text update failed: {ex.Message}"); }
            };

            splash.ReportProgress(0.25, "正在初始化节点控制器...", "配置节点图引擎");
            await Task.Delay(1);

            _nodeGraphController = new NodeGraphController(Nodes, NodeConnections)
            {
                RequestPreviewRefresh = (full) => RequestPreviewRefresh(full),
                UpdateNodeCanvasExtent = () => UpdateNodeCanvasExtent(),
                RefreshConnectionsView = () => { try { NodeConnectionsView?.Refresh(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NodeGraphController] RefreshConnectionsView failed: {ex.Message}"); } },
                InvalidateConnectionLayer = () => NodeConnectionLayer?.InvalidateVisual(),
                MarkStatsActive = () => MarkStatsActive(),
                SetStatusText = (text) => { try { StatusText.Text = text; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NodeGraphController] SetStatusText failed: {ex.Message}"); } },
                UpdateConnectionPositions = (node) => UpdateConnectionPositions(node),
                GetNodeCanvasPosition = (p, s) => GetNodeCanvasPosition(p, s),
                OnParameterCreated = (param) => param.PropertyChanged += NodeParameter_PropertyChanged,
                RecordUndoSnapshot = () => RecordUndoSnapshot(),
            };

            splash.ReportProgress(0.40, "正在加载节点库...", "注册内置节点");
            await Task.Delay(1);
            InitializeNodeLibrary();

            splash.ReportProgress(0.55, "正在扫描模板...", "加载模板文件");
            await Task.Delay(1);
            NodeLibraryService.RefreshTemplateFiles(TemplateFiles);

            splash.ReportProgress(0.60, "正在配置数据绑定...", "初始化视图模型");
            await Task.Delay(1);
            Nodes.CollectionChanged += Nodes_CollectionChanged;
            NodeConnections.CollectionChanged += NodeConnections_CollectionChanged;
            foreach (var node in Nodes)
            {
                node.PropertyChanged += Node_PropertyChanged;
            }

            NodeCanvasView = CollectionViewSource.GetDefaultView(Nodes);
            NodeCanvasView.Filter = NodeCanvasFilter;
            OnPropertyChanged(nameof(NodeCanvasView));
            NodeConnectionsView = CollectionViewSource.GetDefaultView(NodeConnections);
            NodeConnectionsView.Filter = NodeConnectionsFilter;
            OnPropertyChanged(nameof(NodeConnectionsView));
            UpdateNodeCanvasExtent();
            NodeLibraryView = CollectionViewSource.GetDefaultView(NodeLibraryDisplay);
            OnPropertyChanged(nameof(NodeLibraryView));
            NodeLibraryList.ItemsSource = NodeLibraryView;
            ApplyNodeLibraryFilter();

            splash.ReportProgress(0.70, "正在生成网格资源...", "子像素平铺笔刷");
            await Task.Delay(1);
            try
            {
                var subpixelScale = DetermineSubpixelScale();
                Resources["FineGridBrush"] = CreateSubpixelTiledBrush(64, Color.FromArgb(0xE0, 0x33, 0x33, 0x33), subpixelScale);
                Resources["CoarseGridBrush"] = CreateSubpixelTiledBrush(256, Color.FromArgb(0xF0, 0x33, 0x33, 0x33), subpixelScale);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeAsync] Grid brush generation failed: {ex.Message}");
            }

            splash.ReportProgress(0.80, "正在注册事件处理...", "鼠标与键盘事件");
            await Task.Delay(1);
            AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(MainWindow_PreviewMouseLeftButtonUp), true);
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);

            try
            {
                if (NodeCanvasHost != null)
                    NodeCanvasHost.LostMouseCapture += NodeCanvasHost_LostMouseCapture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeAsync] Event registration failed: {ex.Message}");
            }

            splash.ReportProgress(0.88, "正在预热GPU...", "初始化着色器");
            await Task.Delay(1);
            try { Core.Gpu.GpuCompute.InitializeInBackground(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[InitializeAsync] GPU init failed: {ex.Message}"); }

            // ---- Generate all node thumbnails during startup (behind splash) ----
            var thumbTileSize = GetSelectedTileSize();
            var thumbSize = (int)Math.Round(NodeThumbnailSize);
            var libItems = NodeLibrary.ToList();
            var thumbBrushes = new Brush?[libItems.Count];

            splash.ReportProgress(0.90, "正在生成节点预览...", "");
            await Task.Delay(1);

            // Step 1: Pre-compile all script nodes (background, I/O + CPU bound)
            await Task.Run(() =>
            {
                foreach (var proto in Core.GraphNodeRegistry.GetAllPrototypes())
                {
                    if (proto is Core.Nodes.Sources.ResourceNodeInstance scriptNode)
                    {
                        try { scriptNode.WarmCompile(); }
                        catch { }
                    }
                }
            });

            // Step 2: Generate thumbnail brushes — the Process() call is CPU-bound and can
            // run on background; ToBitmapSource() and brush creation run on UI thread.
            splash.ReportProgress(0.93, "正在渲染节点缩略图...", "");
            await Task.Delay(1);

            // Process TileType nodes and SpecialPreview nodes quickly on UI thread
            // (they're fast — just drawing operations)
            for (int i = 0; i < libItems.Count; i++)
            {
                var item = libItems[i];
                try
                {
                    if (item.TileType != null)
                    {
                        thumbBrushes[i] = CreateNodePreviewBrush(item.TileType.Value, item.PreviewBrush, thumbTileSize);
                    }
                    else if (item.InputPorts.Count > 0)
                    {
                        thumbBrushes[i] = NodeLibraryService.CreateSpecialPreviewBrush(item.TypeName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InitializeAsync] Thumbnail generation failed for '{item?.TypeName}': {ex.Message}");
                }
            }

            // Step 3: For compute nodes, process on background thread, then create brushes on UI thread
            splash.ReportProgress(0.95, "正在计算节点预览...", "");
            await Task.Delay(1);

            // Collect compute nodes that need real preview generation
            var computeIndices = new List<int>();
            for (int i = 0; i < libItems.Count; i++)
            {
                if (thumbBrushes[i] == null && libItems[i].InputPorts.Count == 0)
                    computeIndices.Add(i);
            }

            // Process in parallel batches to use CPU efficiently
            var processedBuffers = new Dictionary<int, PixelBuffer>();
            var batchSize = Environment.ProcessorCount;
            for (int batchStart = 0; batchStart < computeIndices.Count; batchStart += batchSize)
            {
                var batchIndices = computeIndices.Skip(batchStart).Take(batchSize).ToList();
                await Task.Run(() =>
                {
                    Parallel.ForEach(batchIndices, idx =>
                    {
                        try
                        {
                            var item = libItems[idx];
                            var graphNode = GraphNodeRegistry.Create(item.TypeName);
                            if (graphNode == null) return;

                            var paramValues = CreateParameterValues(item.Parameters);

                            var context = new PixelGraphContext
                            {
                                TileSize = thumbTileSize,
                                Seed = paramValues.TryGetValue("seed", out var s) && s is int si ? si : 42
                            };

                            var buffer = graphNode.Process(
                                new PixelBuffer?[Math.Max(1, graphNode.InputPorts.Count)],
                                paramValues,
                                context);
                            if (buffer != null)
                                processedBuffers[idx] = buffer;
                        }
                        catch { }
                    });
                });
            }

            // Create WPF brushes from processed buffers on UI thread
            splash.ReportProgress(0.97, "正在合成缩略图...", "");
            await Task.Delay(1);
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var kvp in processedBuffers)
                {
                    try
                    {
                        var pb = kvp.Value;
                        if (pb == null) continue;
                        var bmp = pb.ToBitmapSource();
                        var brush = GraphEvaluationService.CreatePreviewBrushFromBitmap(bmp, thumbSize);
                        thumbBrushes[kvp.Key] = brush;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[InitializeAsync] Brush creation failed for buffer {kvp.Key}: {ex.Message}");
                    }
                }

                // Apply all brushes to NodeLibrary
                for (int i = 0; i < libItems.Count; i++)
                {
                    if (thumbBrushes[i] == null) continue;
                    var orig = libItems[i];
                    NodeLibrary[i] = new NodeLibraryItem(
                        orig.Name, orig.Category, orig.Kind, thumbBrushes[i]!, orig.TileType,
                        orig.InputPorts, orig.OutputPorts, orig.Parameters,
                        subcategory: "", typeName: orig.TypeName,
                        categoryKey: orig.CategoryKey,
                        thumbnailWidth: thumbSize, thumbnailHeight: thumbSize);
                }
            }, DispatcherPriority.Background);

            splash.ReportProgress(1.00, "启动完成", "");
            await Task.Delay(1);

            // Force UI binding refresh for node library collections
            OnPropertyChanged(nameof(NodeLibraryCategories));
            OnPropertyChanged(nameof(NodeLibrary));
            OnPropertyChanged(nameof(NodeLibraryDisplay));

            UpdateAnimationUI();
            Splash = null;
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Instance = this;

            _previewDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PreviewThrottleMs)
            };
            _previewDebounceTimer.Tick += PreviewDebounceTimer_Tick;

            _fpsTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _fpsTimer.Tick += FpsTimer_Tick;

            _statsIdleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _statsIdleTimer.Tick += StatsIdleTimer_Tick;

            // Model switch dropdown: force popup to open upward
            AiModelSwitchCombo.DropDownOpened += (_, _) =>
            {
                if (AiModelSwitchCombo.Template.FindName("PART_Popup", AiModelSwitchCombo) is System.Windows.Controls.Primitives.Popup popup)
                    popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            };

            // Populate model switch combo from saved settings (runs before any AI connection)
            SyncModelSwitchCombo();

            // Initialize node library categories immediately so UI bindings work.
            try
            {
                InitializeNodeLibrary();
                System.Diagnostics.Debug.WriteLine($"[Init] NodeLibrary.Count={NodeLibrary.Count} NodeLibraryCategories.Count={NodeLibraryCategories.Count} NodeLibraryDisplay.Count={NodeLibraryDisplay.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] InitNodeLib error: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Persist thumbnail size before exit
            SettingsService.Current.NodeThumbnailSize = _nodeThumbnailSize;
            SettingsService.Save();

            CleanupTimers();
            base.OnClosed(e);
        }

        /// <summary>Stops all DispatcherTimer instances to prevent leaks after window close.</summary>
        private void CleanupTimers()
        {
            try
            {
                _previewDebounceTimer.Stop();
                _previewDebounceTimer.Tick -= PreviewDebounceTimer_Tick;
            } catch { /* timer not started */ }

            try
            {
                _fpsTimer.Stop();
                _fpsTimer.Tick -= FpsTimer_Tick;
            } catch { }

            try
            {
                _statsIdleTimer.Stop();
                _statsIdleTimer.Tick -= StatsIdleTimer_Tick;
            } catch { }

            if (_aiRenderTimer != null)
            {
                try
                {
                    _aiRenderTimer.Stop();
                    _aiRenderTimer.Tick -= AiRenderTimer_Tick;
                } catch { }
                _aiRenderTimer = null;
            }

            if (_nodePreviewDebounceTimer != null)
            {
                try { _nodePreviewDebounceTimer.Stop(); } catch { }
                _nodePreviewDebounceTimer = null;
            }

            // Stop and clean up the thumbnail debounce timer
            if (_thumbnailDebounceTimer != null)
            {
                try { _thumbnailDebounceTimer.Stop(); } catch { }
                _thumbnailDebounceTimer = null;
            }
        }

        // ─── Undo/Redo ─────────────────────────────────────────────────

        /// <summary>
        /// Records the current graph state as an undo snapshot.
        /// Call BEFORE making mutations.
        /// </summary>
        private void RecordUndoSnapshot()
        {
            _undoRedoService.RecordSnapshot(Nodes, NodeConnections);
        }

        /// <summary>
        /// Records a snapshot before user-initiated graph changes.
        /// </summary>
        private void RecordUserActionSnapshot()
        {
            RecordUndoSnapshot();
        }

        private void Undo()
        {
            var snapshot = _undoRedoService.Undo(Nodes, NodeConnections);
            if (snapshot != null)
            {
                UndoRedoService.ApplySnapshot(snapshot, Nodes, NodeConnections);
                UpdateNodeCanvasExtent();
                NodeConnectionsView?.Refresh();
                RequestPreviewRefresh(false);
                try { StatusText.Text = "Undone"; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Undo] Status text failed: {ex.Message}"); }
            }
        }

        private void Redo()
        {
            var snapshot = _undoRedoService.Redo(Nodes, NodeConnections);
            if (snapshot != null)
            {
                UndoRedoService.ApplySnapshot(snapshot, Nodes, NodeConnections);
                UpdateNodeCanvasExtent();
                NodeConnectionsView?.Refresh();
                RequestPreviewRefresh(false);
                try { StatusText.Text = "Redone"; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Redo] Status text failed: {ex.Message}"); }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private static ListBoxItem? GetListBoxItemAtPoint(ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as DependencyObject;
            while (element != null && element is not ListBoxItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }

            return element as ListBoxItem;
        }

        private static float CalculateOpaqueCoverage(byte[] pixels)
        {
            if (pixels.Length == 0)
            {
                return 0f;
            }

            var opaquePixelCount = 0;
            for (var index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] >= 32)
                {
                    opaquePixelCount++;
                }
            }

            return opaquePixelCount / (pixels.Length / 4f);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;

            // Log startup to console
            var console = ServiceLocator.TryGetService<IConsoleService>();
            console?.LogInfo("主窗口已加载", "MainWindow.MainWindow_Loaded");
            console?.LogDebug($"屏幕分辨率: {SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}", "MainWindow.MainWindow_Loaded");

            // Auto-compute thumbnail size on first launch; use persisted value on subsequent launches.
            // Must defer to Background priority so layout is complete and ActualWidth is reliable.
            if (SettingsService.Current.NodeThumbnailSize > 0)
            {
                _nodeThumbnailSize = SettingsService.Current.NodeThumbnailSize;
                OnPropertyChanged(nameof(NodeThumbnailSize));
            }
            else
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var availableWidth = NodeLibraryList?.ActualWidth ?? 200;
                    // 4 columns: each item = thumbnail + 20 padding, gaps between items
                    var gaps = 3 * 8;
                    var itemWidth = (availableWidth - gaps) / 4;
                    _nodeThumbnailSize = Math.Clamp(itemWidth - 20, 52, 96);
                    OnPropertyChanged(nameof(NodeThumbnailSize));
                }), DispatcherPriority.Background);
            }

            try
            {
                // Disable bitmap caching for the node canvas layers — caching as a bitmap
                // causes blurriness when the canvas is scaled. Use nearest-neighbor scaling
                // for pixel-art elements to keep them crisp.
                if (NodeCanvasSurface != null)
                {
                    NodeCanvasSurface.CacheMode = null;
                    RenderOptions.SetBitmapScalingMode(NodeCanvasSurface, BitmapScalingMode.NearestNeighbor);
                }

                if (NodeConnectionLayer != null)
                {
                    NodeConnectionLayer.CacheMode = null;
                    RenderOptions.SetBitmapScalingMode(NodeConnectionLayer, BitmapScalingMode.NearestNeighbor);
                }

                if (NodeCanvasItems != null)
                {
                    NodeCanvasItems.CacheMode = null;
                    RenderOptions.SetBitmapScalingMode(NodeCanvasItems, BitmapScalingMode.NearestNeighbor);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] Canvas init failed: {ex.Message}");
            }

            // Attempt to generate subpixel-tiled brushes on load so grid lines can appear
            // visually thinner than one device pixel (render at higher resolution then
            // downsample). The scale is chosen based on the current DPI so the visual
            // thickness remains consistent across displays. Fail silently and keep XAML
            // brushes if generation fails.
            try
            {
                var scale = DetermineSubpixelScale();
                var fine = CreateSubpixelTiledBrush(16, Color.FromArgb(0xE0, 0x33, 0x33, 0x33), scale);
                this.Resources["FineGridBrush"] = fine;
                var coarse = CreateSubpixelTiledBrush(64, Color.FromArgb(0xF0, 0x33, 0x33, 0x33), scale);
                this.Resources["CoarseGridBrush"] = coarse;
                if (InfiniteGrid != null)
                {
                    InfiniteGrid.MinorBrush = fine;
                    InfiniteGrid.MajorBrush = coarse;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow_Loaded] Subpixel brush generation failed: {ex.Message}");
            }

            // Subscribe to language changes to refresh window title and UI
            Services.Localization.LocalizationService.Instance.CultureChanged += OnCultureChanged;

            // Cache all port positions after layout is complete
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                try { CacheAllPortPositions(); } catch { }
            }), DispatcherPriority.Background);

            RequestPreviewRefresh(true);
            // Thumbnails are already generated during InitializeAsync (splash stage).
            // Only schedule a refresh if the tile size has changed since then.
            ScheduleThumbnailRegeneration();

            // Wire AI session list events after controls are loaded
            if (AiSessionList != null)
            {
                AiSessionList.SessionSelected += sessionId =>
                {
                    // 自动保存当前会话再切换
                    AutoSaveCurrentSession();

                    if (_pixelAgentService == null || !_pixelAgentService.LoadSessionById(sessionId))
                        return;

                    _currentSessionId = sessionId;
                    _userMessageCount = 0;

                    // 重建 UI 消息列表
                    AiChat.Messages.Clear();
                    var history = _pixelAgentService.GetHistory();
                    foreach (var msg in history)
                    {
                        switch (msg.Role)
                        {
                            case "user":
                                AiChat.AddUserMessage(msg.Content ?? "");
                                break;
                            case "assistant":
                                AiChat.AddAssistantMessage(msg.Content ?? "");
                                break;
                            case "system":
                                AiChat.AddSystemMessage(msg.Content ?? "");
                                break;
                            case "error":
                                AiChat.AddErrorMessage(msg.Content ?? "");
                                break;
                        }
                    }
                    AiChat.IsConnected = true;
                    AiChat.StatusText = "Connected";

                    // 恢复计划面板
                    var planPath = _pixelAgentService.GetSessionPlanPath(sessionId);
                    _currentPlanFilePath = planPath;
                    if (planPath != null && System.IO.File.Exists(planPath))
                    {
                        var planMd = System.IO.File.ReadAllText(planPath);
                        var plan = Services.MdPlanParser.ExtractPlan(planMd);
                        var layeredPlan = Services.MdPlanParser.ExtractLayeredPlan(planMd);
                        if (layeredPlan != null)
                            AiPlanViewer.LayeredPlan = layeredPlan;
                        else if (plan != null)
                            AiPlanViewer.Plan = plan;
                        AiPlanPanel.Visibility = System.Windows.Visibility.Visible;
                        AiPlanCollapsedBar.Visibility = System.Windows.Visibility.Collapsed;
                    }

                    // 更新标题
                    UpdateAiSessionTitle();

                    // 刷新列表高亮
                    RefreshAiSessionList();
                    AiHistorySidebarCol.Width = new System.Windows.GridLength(220);
                };
                AiSessionList.SessionDeleteRequested += sessionId =>
                {
                    try
                    {
                        _pixelAgentService?.DeleteSession(sessionId);
                        RefreshAiSessionList();
                    }
                    catch { }
                };
            }
        }

        private void PreviewGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PreviewContentGrid == null || PreviewViewport == null) return;

            var availableHeight = PreviewContentGrid.RowDefinitions[2].ActualHeight;
            var availableWidth = PreviewContentGrid.ActualWidth;

            var size = Math.Min(availableWidth, availableHeight);
            if (size < 0) size = 0;

            PreviewViewport.Width = size;
            PreviewViewport.Height = size;
            UpdatePreviewDisplay();
            UpdateFitScale();
            ApplyZoom();
            // Ensure pixel-grid overlay updates when layout changes
            UpdatePreviewPixelGridOverlay();
        }

        private bool _tiledPreviewEnabled = false;

        private async Task RefreshNodeLibraryThumbnailsAsync()
        {
            try
            {
                var tileSize = GetSelectedTileSize();
                var thumbSize = (int)Math.Round(NodeThumbnailSize);
                var items = NodeLibrary.ToList();
                var batch = new List<(int Index, NodeLibraryItem Item)>();

                // Process in small batches with idle-priority UI updates
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    try
                    {
                        // Skip if this item already has a non-default thumbnail of matching size.
                        // When _forceThumbnailRefresh is set (e.g. after language switch),
                        // ignore the cache and rebuild all thumbnails.
                        if (!_forceThumbnailRefresh
                            && item.ThumbnailWidth == thumbSize
                            && item.ThumbnailHeight == thumbSize
                            && !ReferenceEquals(item.PreviewBrush, Brushes.Transparent)
                            && item.PreviewImage != null)
                            continue;

                        Brush? brush = null;
                        if (item.TileType != null)
                        {
                            brush = CreateNodePreviewBrush(item.TileType.Value, item.PreviewBrush, tileSize);
                        }
                        else if (item.InputPorts.Count == 0)
                        {
                            var tempNode = new NodeViewModel(item.Name, 0, 0, item.PreviewBrush)
                            {
                                Kind = item.Kind,
                                TileType = null,
                                TypeName = item.TypeName
                            };
                            foreach (var def in item.Parameters)
                                tempNode.Parameters.Add(CreateParameterViewModel(def));
                            brush = GenerateGraphNodePreviewBrush(tempNode, tileSize);
                        }
                        else
                        {
                            brush = NodeLibraryService.CreateSpecialPreviewBrush(item.TypeName);
                        }

                        if (brush != null)
                        {
                            batch.Add((i, new NodeLibraryItem(
                                item.Name, item.Category, item.Kind, brush, item.TileType,
                                item.InputPorts, item.OutputPorts, item.Parameters,
                                subcategory: "", typeName: item.TypeName,
                                categoryKey: item.CategoryKey,
                                thumbnailWidth: thumbSize, thumbnailHeight: thumbSize)));
                        }
                    }
                    catch { }

                    // Apply to UI every 5 items, on idle priority so the UI stays responsive
                    if (batch.Count >= 5)
                    {
                        var toApply = batch.ToList();
                        batch.Clear();
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var (idx, newItem) in toApply)
                                NodeLibrary[idx] = newItem;
                        }, DispatcherPriority.ApplicationIdle);
                    }
                }

                // Apply remaining items
                if (batch.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var (idx, newItem) in batch)
                            NodeLibrary[idx] = newItem;
                    }, DispatcherPriority.ApplicationIdle);
                }

                // Final refresh
                await Dispatcher.InvokeAsync(ApplyNodeLibraryFilter, DispatcherPriority.Background);

                _forceThumbnailRefresh = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshNodeLibraryThumbnailsAsync] Failed: {ex.Message}");
            }
        }

        private sealed class RelayCommand<T> : ICommand
        {
            private readonly Action<T> _execute;
            private readonly Predicate<T>? _canExecute;

            public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
            {
                ArgumentNullException.ThrowIfNull(execute);
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter)
            {
                return parameter is T value && (_canExecute?.Invoke(value) ?? true);
            }

            public void Execute(object? parameter)
            {
                if (parameter is T value)
                {
                    _execute(value);
                }
            }

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
        }

        // ─── Number parameter drag-scrub ────────────────────────────────────────

        private bool _isScrubbing;
        private Point _scrubStartPoint;
        private double _scrubStartValue;
        private const double ScrubSensitivity = 0.01;

        private void NumberValueText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not NodeParameterViewModel vm) return;
            _isScrubbing = true;
            _scrubStartPoint = e.GetPosition(element);
            _scrubStartValue = vm.NumberValue;
            element.CaptureMouse();
            e.Handled = true;
        }

        private void NumberValueText_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isScrubbing || sender is not FrameworkElement element || element.DataContext is not NodeParameterViewModel vm) return;
            var pos = e.GetPosition(element);
            var delta = pos.X - _scrubStartPoint.X;
            var newValue = _scrubStartValue + delta * ScrubSensitivity;
            vm.NumberValue = Math.Max(vm.Min, Math.Min(vm.Max, newValue));
            e.Handled = true;
        }

        private void NumberValueText_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isScrubbing || sender is not FrameworkElement element) return;
            _isScrubbing = false;
            element.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void NodeLibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyNodeLibraryFilter();
            if (ClearSearchButton != null)
                ClearSearchButton.Visibility = string.IsNullOrEmpty(NodeLibrarySearchBox?.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (NodeLibrarySearchBox != null)
            {
                NodeLibrarySearchBox.Text = "";
                NodeLibrarySearchBox.Focus();
            }
        }

        /// <summary>
        /// Allows the mouse wheel to scroll the parent ScrollViewer even when the cursor
        /// is over a Slider. Without this, Sliders steal focus and swallow scroll events,
        /// making nested scroll panels feel unresponsive.
        /// </summary>
        private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0) return;
            // Walk up the visual tree to find the parent ScrollViewer
            for (var el = e.OriginalSource as DependencyObject; el != null; el = VisualTreeHelper.GetParent(el))
            {
                if (el is System.Windows.Controls.ScrollViewer sv)
                {
                    // e.Delta is ±120 per notch; scale to a reasonable pixel scroll amount
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - Math.Sign(e.Delta) * 48);
                    e.Handled = true;
                    break;
                }
            }
        }

        // -----------------------------------------------------------------------
        // Skill graph import — instantiates a skill's sub-graph onto the current canvas
        // -----------------------------------------------------------------------

        /// <summary>
        /// Imports a skill's serialized sub-graph onto the canvas at the given position.
        /// Returns true on success.
        /// </summary>
        private bool ImportSkillGraph(string serializedJson, double offsetX, double offsetY)
        {
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<ProjectFileService.ProjectData>(
                    serializedJson, SettingsService.JsonOptions);
                if (data == null || data.Nodes.Count == 0) return false;

                var offsetIsValid = !double.IsNaN(offsetX) && !double.IsNaN(offsetY);
                var baseX = offsetIsValid ? offsetX : 80;
                var baseY = offsetIsValid ? offsetY : 80;

                var created = new List<NodeViewModel>();
                var idMap = new Dictionary<int, NodeViewModel>();

                for (int i = 0; i < data.Nodes.Count; i++)
                {
                    var nd = data.Nodes[i];
                    var node = new NodeViewModel(nd.Title, baseX + nd.X, baseY + nd.Y)
                    {
                        Kind = nd.Kind,
                        TileType = nd.TileType,
                        TypeName = string.IsNullOrEmpty(nd.TypeName) ? nd.Title : nd.TypeName
                    };

                    if (nd.Properties != null)
                        node.TileProperties = nd.Properties.Clone();

                    node.Parameters.Clear();
                    if (node.Kind != NodeLibraryItemKind.Tile)
                    {
                        foreach (var pd in nd.Parameters)
                        {
                            var param = new NodeParameterViewModel(pd.Name, pd.Kind, 0, 1, 0.01, new List<string>());
                            param.NumberValue = pd.NumberValue;
                            param.IntValue = pd.IntValue;
                            param.BoolValue = pd.BoolValue;
                            param.SelectedChoice = pd.SelectedChoice;
                            param.PropertyChanged += NodeParameter_PropertyChanged;
                            node.Parameters.Add(param);
                        }
                    }

                    // Initialize ports from prototype
                    if (node.TileType != null)
                    {
                        node.InputPorts.Clear();
                        node.OutputPorts.Clear();
                        node.InputPorts.Add(new NodePortViewModel("Input", PortValueType.Image, false));
                        node.OutputPorts.Add(new NodePortViewModel("Output", PortValueType.Image, true));
                    }
                    else
                    {
                        var proto = GraphNodeRegistry.Create(node.RegistryKey);
                        if (proto != null)
                        {
                            foreach (var port in proto.InputPorts)
                                node.InputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), false));
                            foreach (var port in proto.OutputPorts)
                                node.OutputPorts.Add(new NodePortViewModel(port.Name, MapGraphPortType(port.Type), true));
                        }
                    }

                    Nodes.Add(node);
                    created.Add(node);
                    idMap[i] = node;
                }

                // Create connections
                foreach (var cd in data.Connections)
                {
                    if (idMap.TryGetValue(cd.StartNodeIndex, out var startNode) &&
                        idMap.TryGetValue(cd.EndNodeIndex, out var endNode))
                    {
                        if (cd.StartPortIndex < startNode.OutputPorts.Count &&
                            cd.EndPortIndex < endNode.InputPorts.Count)
                        {
                            NodeConnections.Add(new NodeConnectionViewModel
                            {
                                StartNode = startNode,
                                StartPortIndex = cd.StartPortIndex,
                                EndNode = endNode,
                                EndPortIndex = cd.EndPortIndex,
                                IsPreview = false
                            });
                        }
                    }
                }

                // Arrange layout
                if (created.Count > 0)
                {
                    var minX = created.Min(n => n.X);
                    var minY = created.Min(n => n.Y);
                    foreach (var n in created)
                    {
                        n.X -= minX - baseX;
                        n.Y -= minY - baseY;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        // ─── Animation playback ───────────────────────────────────────────

        /// <summary>
        /// Checks the current node graph for any animation-capable nodes
        /// (FrameSequence, Rain, Snow, Smoke, Fog, Lightning, etc.) and
        /// shows/hides the animation playback toolbar accordingly.
        /// </summary>
        private void UpdateAnimationUI()
        {
            if (AnimControlPanel == null || !_isInitialized) return;
            var hasAnimNode = Nodes.Any(n =>
            {
                var t = n.TypeName;
                return t == "FrameSequence"
                    || t == "ParameterAnimation"
                    || t == "FrameInterpolation"
                    || t == "Rain" || t == "Snow"
                    || t == "Smoke" || t == "Fog"
                    || t == "Lightning" || t == "LavaFlow"
                    || t == "WaterFlow" || t == "Fire"
                    || t == "ParticleEmitter" || t == "ParticleForce"
                    || t == "ParticleRender" || t == "PhysicsSimulate"
                    || t == "PhysicsField" || t == "Time"
                    || t == "AnimatedParameter";
            });
            AnimControlPanel.Visibility = hasAnimNode ? Visibility.Visible : Visibility.Collapsed;
        }

        private Services.AnimationPlaybackService EnsureAnimationService()
        {
            if (_animationService == null)
            {
                _animationService = new Services.AnimationPlaybackService();
                _animationService.FrameChanged += (frame, t) =>
                {
                    AnimFrameLabel.Text = $"{frame + 1}/{_animationService.FrameCount}";
                    // Trigger particle simulation and preview refresh with the current animation frame
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        UpdateParticleSimulation(frame, t);
                        RequestPreviewRefresh(false);
                    });
                };
            }
            return _animationService;
        }

        /// <summary>
        /// Advances particle system simulation for the current animation frame.
        /// Called on each animation frame tick before preview refresh.
        /// </summary>
        private void UpdateParticleSimulation(int frame, float normalizedTime)
        {
            if (_particleEvalService == null)
                _particleEvalService = new Services.ParticleEvaluationService();

            // Build temporary instance list from current nodes
            var instances = new List<GraphNodeInstance>(Nodes.Count);
            var instanceMap = new Dictionary<int, GraphNodeInstance>();
            foreach (var node in Nodes)
            {
                var graphNode = GraphNodeRegistry.Create(node.RegistryKey);
                if (graphNode == null) continue;
                var inst = new GraphNodeInstance(node.Id, graphNode);
                // Copy parameter values
                foreach (var param in node.Parameters)
                {
                    if (inst.ParameterValues.ContainsKey(param.Name))
                    {
                        inst.ParameterValues[param.Name] = param.Kind switch
                        {
                            NodeParameterKind.Number => (object)param.NumberValue,
                            NodeParameterKind.Integer => param.IntValue,
                            NodeParameterKind.Boolean => param.BoolValue,
                            NodeParameterKind.Choice => param.SelectedChoice ?? string.Empty,
                            NodeParameterKind.Color => (object)param.ColorValue,
                            NodeParameterKind.Seed => param.IntValue,
                            _ => (object)param.NumberValue
                        };
                    }
                }
                instances.Add(inst);
                instanceMap[node.Id] = inst;
            }

            // Restore previous frame's state
            _particleEvalService.RestoreState(instances);

            // Simulate one frame
            var context = new PixelGraphContext
            {
                TileSize = GetSelectedTileSize(),
                Seed = 42,
                AnimationTime = normalizedTime,
                AnimationFrame = frame,
                AnimationFrameCount = _animationService?.FrameCount ?? 1,
                DeltaTime = _animationService != null ? 1f / (float)(_animationService.FrameRate) : 1f / 60f,
            };

            _particleEvalService.SimulateParticleFrame(instances, new Dictionary<string, object>(), context);

            // Save state back
            _particleEvalService.SaveState(instances);
        }

        private void AnimPlay_Click(object sender, RoutedEventArgs e)
        {
            // If currently in Still mode, auto-switch to Animation mode
            if (_previewMode == PreviewMode.Still)
            {
                _previewMode = PreviewMode.Animation;
                if (AnimPreviewRadio != null) AnimPreviewRadio.IsChecked = true;
                if (StillPreviewRadio != null) StillPreviewRadio.IsChecked = false;
                // UpdateAnimationUI and state clear handled by AnimPreviewRadio_Checked
            }

            var svc = EnsureAnimationService();
            if (svc.IsPlaying)
            {
                svc.Pause();
                AnimPlayBtn.Content = "▶";
            }
            else
            {
                svc.Play();
                AnimPlayBtn.Content = "⏸";
            }
        }

        private void AnimStop_Click(object sender, RoutedEventArgs e)
        {
            if (_animationService == null) return;
            _animationService.Stop();
            AnimPlayBtn.Content = "▶";
            _ = Dispatcher.InvokeAsync(() => RequestPreviewRefresh(false));
        }

        private void AnimStep_Click(object sender, RoutedEventArgs e)
        {
            var svc = EnsureAnimationService();
            svc.StepForward();
        }

        private void AnimFpsCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_animationService == null || AnimFpsCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
            if (double.TryParse(item.Tag?.ToString(), out var fps))
                _animationService.FrameRate = fps;
        }

        // ─── Preview mode switching ─────────────────────────────────────

        private void StillPreviewRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _previewMode = PreviewMode.Still;

            // Hide animation controls
            if (AnimControlPanel != null)
                AnimControlPanel.Visibility = Visibility.Collapsed;

            // Stop any playing animation
            if (_animationService != null && _animationService.IsPlaying)
            {
                _animationService.Stop();
                if (AnimPlayBtn != null)
                    AnimPlayBtn.Content = "▶";
            }

            // Clear particle accumulation so static frame shows initial state
            _particleEvalService?.ClearState();

            // Disable comparison mode when switching to Still (reset state)
            DisableComparisonMode();

            // Update frame slider visibility based on graph content
            UpdateStillFrameSliderVisibility();

            // Refresh preview with the selected node's output
            RequestPreviewRefresh(false);
        }

        private void AnimPreviewRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _previewMode = PreviewMode.Animation;

            // Hide frame slider
            if (StillFrameSliderBar != null)
                StillFrameSliderBar.Visibility = Visibility.Collapsed;
            _stillFrameSliderActive = false;

            // Disable comparison mode
            DisableComparisonMode();

            // Show animation controls if animation nodes exist
            UpdateAnimationUI();

            // Clear particle state and refresh
            _particleEvalService?.ClearState();
            RequestPreviewRefresh(false);
        }

        // ─── Still mode frame slider ─────────────────────────────────────

        /// <summary>
        /// Shows/hides the frame slider bar in Still mode when animation nodes exist.
        /// </summary>
        private void UpdateStillFrameSliderVisibility()
        {
            if (StillFrameSliderBar == null || !_isInitialized) return;

            var hasAnimNode = Nodes.Any(n =>
            {
                var t = n.TypeName;
                return t == "FrameSequence" || t == "ParameterAnimation"
                    || t == "AnimatedParameter" || t == "Time"
                    || t == "ParticleEmitter" || t == "ParticleRender"
                    || t == "PhysicsSimulate" || t == "FrameInterpolation";
            });

            if (hasAnimNode)
            {
                // Determine frame count from animation service or default
                _stillFrameCount = _animationService?.FrameCount ?? 8;
                _stillFrameSliderActive = true;
                _stillFrameNormalizedTime = 0f;

                StillFrameSlider.Value = 0;
                StillFrameLabel.Text = $"1/{_stillFrameCount}";
                StillFrameSliderBar.Visibility = Visibility.Visible;
            }
            else
            {
                _stillFrameSliderActive = false;
                _stillFrameNormalizedTime = -1f;
                StillFrameSliderBar.Visibility = Visibility.Collapsed;
            }
        }

        private void StillFrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || !_stillFrameSliderActive) return;

            _stillFrameNormalizedTime = (float)Math.Clamp(e.NewValue, 0.0, 1.0);
            var frameIndex = (int)(_stillFrameNormalizedTime * (_stillFrameCount - 1));
            StillFrameLabel.Text = $"{frameIndex + 1}/{_stillFrameCount}";

            // Clear particle state and refresh with new frame
            // Throttle by using the existing debounce mechanism
            RequestPreviewRefresh(false);
        }

        // ─── Comparison mode ─────────────────────────────────────────────

        private void ComparisonToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _comparisonModeEnabled = ComparisonToggleButton.IsChecked == true;
            if (_comparisonModeEnabled)
            {
                // Render terminal node output and show split view
                RenderComparisonPreview();
            }
            else
            {
                // Hide comparison overlay
                if (ComparisonImage != null)
                    ComparisonImage.Visibility = Visibility.Collapsed;
                if (ComparisonDivider != null)
                    ComparisonDivider.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Renders both the selected node and terminal node output for comparison.
        /// The selected node output stays in PreviewImage (left half); the terminal output
        /// goes to ComparisonImage (right half), separated by ComparisonDivider.
        /// </summary>
        private void RenderComparisonPreview()
        {
            if (SelectedNode == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!TryGetNodeOutputSize(out var size)) return;

                    // Find terminal node
                    var terminalNode = FindTerminalNode();
                    if (terminalNode == null || terminalNode.Id == SelectedNode.Id)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ComparisonToggleButton.IsChecked = false;
                            _comparisonModeEnabled = false;
                            ComparisonImage.Visibility = Visibility.Collapsed;
                            ComparisonDivider.Visibility = Visibility.Collapsed;
                        });
                        return;
                    }

                    // Render terminal output
                    var terminalBitmap = EvaluateGraphPipelineWithTime(size, terminalNode, null);
                    terminalBitmap?.Freeze();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (terminalBitmap != null && _comparisonModeEnabled && PreviewImage != null)
                        {
                            _comparisonTerminalBitmap = terminalBitmap;
                            ComparisonImage.Source = terminalBitmap;
                            ComparisonImage.Visibility = Visibility.Visible;

                            // Clip PreviewImage (selected node) to left half
                            if (_lastGenerated != null)
                            {
                                PreviewImage.Clip = new RectangleGeometry(
                                    new Rect(0, 0, _lastGenerated.PixelWidth / 2.0, _lastGenerated.PixelHeight));
                            }

                            // Clip ComparisonImage (terminal) to right half
                            var tw = terminalBitmap.PixelWidth / 2.0;
                            var th = terminalBitmap.PixelHeight;
                            ComparisonImage.Clip = new RectangleGeometry(
                                new Rect(tw, 0, tw, th));

                            ComparisonDivider.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            ComparisonImage.Visibility = Visibility.Collapsed;
                            ComparisonDivider.Visibility = Visibility.Collapsed;
                        }
                    });
                }
                catch { }
            });
        }

        /// <summary>
        /// Disables comparison mode split view. Call when switching away from Still mode
        /// or when the selected node changes.
        /// </summary>
        private void DisableComparisonMode()
        {
            _comparisonModeEnabled = false;
            if (ComparisonToggleButton != null) ComparisonToggleButton.IsChecked = false;
            if (ComparisonImage != null) ComparisonImage.Visibility = Visibility.Collapsed;
            if (ComparisonDivider != null) ComparisonDivider.Visibility = Visibility.Collapsed;
            if (PreviewImage != null) PreviewImage.Clip = null;
        }

        /// <summary>
        /// Evaluates the graph pipeline for a given node with animation time optionally overridden.
        /// Used by comparison mode to render the terminal node in Still mode.
        /// </summary>
        private BitmapSource? EvaluateGraphPipelineWithTime(int size, NodeViewModel target,
            float? animationTimeOverride)
        {
            // Save and override still frame time
            var savedSliderActive = _stillFrameSliderActive;
            var savedFrameTime = _stillFrameNormalizedTime;

            if (animationTimeOverride.HasValue)
            {
                _stillFrameSliderActive = true;
                _stillFrameNormalizedTime = animationTimeOverride.Value;
            }
            else
            {
                _stillFrameSliderActive = false;
                _stillFrameNormalizedTime = -1f;
            }

            try
            {
                return EvaluateGraphPipeline(size, target);
            }
            finally
            {
                _stillFrameSliderActive = savedSliderActive;
                _stillFrameNormalizedTime = savedFrameTime;
            }
        }

        /// <summary>
        /// Finds the "terminal" node — the one that would be the final output
        /// (same logic as animation mode's FindPreviewTargetNode).
        /// </summary>
        private NodeViewModel? FindTerminalNode()
        {
            // Priority 1: ParticleRenderNode
            foreach (var n in Nodes)
            {
                if (n.TypeName == "ParticleRender" && IsGraphNode(n))
                    return n;
            }

            // Priority 2: Output node
            var outputNode = GetOutputNode();
            if (outputNode != null)
                return outputNode;

            // Priority 3: Terminal node (no outgoing connections)
            foreach (var n in Nodes)
            {
                var hasOutgoing = NodeConnections.Any(c =>
                    !c.IsPreview && c.StartNode != null && c.StartNode.Id == n.Id);
                if (!hasOutgoing && IsGraphNode(n))
                    return n;
            }

            return null;
        }

        /// <summary>
        /// Called from ConsoleControl when errors are recorded while on a different tab,
        /// to switch to the console tab.
        /// </summary>
        public void ShowConsoleBadge(int errorCount)
        {
            // Console is now in LeftPanelTabs as a TabItem — no badge needed
        }
    }
}

namespace PixelAssetGenerator
{
    /// <summary>
    /// Converts a node type name to its functional description string.
    /// Uses the current UI language to pick the correct locale from .node.json;
    /// falls back to the English description dictionary.
    /// </summary>
    public class NodeDescriptionConverter : IValueConverter
    {
        private static Services.Localization.ILocalizationService Loc
            => Services.ServiceLocator.GetService<Services.Localization.ILocalizationService>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string typeName)
            {
                // Try to get description from .node.json using current UI language
                var catalog = Core.GraphNodeRegistry.GetCatalog();
                foreach (var r in catalog)
                {
                    if (string.Equals(r.Identity.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        var currentLocale = Loc.CurrentCulture;
                        var desc = r.Identity.Description.Get(currentLocale);
                        if (!string.IsNullOrEmpty(desc))
                            return desc;
                        break;
                    }
                }

                // Fallback to English description dictionary
                return NodeLibraryService.GetNodeDescription(typeName);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
