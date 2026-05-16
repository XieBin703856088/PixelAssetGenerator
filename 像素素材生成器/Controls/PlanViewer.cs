using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Controls;

/// <summary>
/// Displays a task plan with progress tracking.
/// Supports flat (MdTaskPlan), layered (LayeredPlan), and active (ActivePlan) plans.
/// </summary>
public sealed class PlanViewer : ContentControl
{
    public static readonly DependencyProperty PlanProperty =
        DependencyProperty.Register(nameof(Plan), typeof(MdTaskPlan), typeof(PlanViewer),
            new PropertyMetadata(null, OnPlanChanged));

    public static readonly DependencyProperty LayeredPlanProperty =
        DependencyProperty.Register(nameof(LayeredPlan), typeof(LayeredPlan), typeof(PlanViewer),
            new PropertyMetadata(null, OnLayeredPlanChanged));

    public static readonly DependencyProperty ActivePlanProperty =
        DependencyProperty.Register(nameof(ActivePlan), typeof(ActivePlan), typeof(PlanViewer),
            new PropertyMetadata(null, OnActivePlanChanged));

    public MdTaskPlan? Plan
    {
        get => (MdTaskPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public LayeredPlan? LayeredPlan
    {
        get => (LayeredPlan?)GetValue(LayeredPlanProperty);
        set => SetValue(LayeredPlanProperty, value);
    }

    public ActivePlan? ActivePlan
    {
        get => (ActivePlan?)GetValue(ActivePlanProperty);
        set => SetValue(ActivePlanProperty, value);
    }

    private const double ProgressBarWidth = 200;

    private static readonly Color BgColor = Color.FromRgb(18, 24, 36);
    private static readonly Color BorderColor = Color.FromRgb(40, 48, 64);
    private static readonly Color TitleColor = Color.FromRgb(200, 210, 230);
    private static readonly Color TextColor = Color.FromRgb(180, 190, 210);
    private static readonly Color MutedColor = Color.FromRgb(120, 130, 150);
    private static readonly Color TrackBg = Color.FromRgb(30, 38, 54);
    private static readonly Color FillColor = Color.FromRgb(80, 180, 255);
    private static readonly Color DoneColor = Color.FromRgb(80, 220, 120);
    private static readonly Color InProgressColor = Color.FromRgb(100, 180, 255);
    private static readonly Color FailedColor = Color.FromRgb(255, 100, 80);

    // Phase header colors
    private static readonly Color PhaseCompColor = Color.FromRgb(110, 180, 240);
    private static readonly Color PhaseObjColor = Color.FromRgb(160, 210, 120);
    private static readonly Color PhaseMatColor = Color.FromRgb(240, 190, 100);
    private static readonly Color PhaseAdjColor = Color.FromRgb(210, 140, 230);

    private readonly Border _root;
    private readonly StackPanel _contentPanel;
    private readonly TextBlock _titleBlock;

    public PlanViewer()
    {
        _root = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(BgColor),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed
        };

        _contentPanel = new StackPanel();

        _titleBlock = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TitleColor),
            Margin = new Thickness(0, 0, 0, 4)
        };

        _contentPanel.Children.Add(_titleBlock);
        _root.Child = _contentPanel;
        Content = _root;
    }

    private static void OnPlanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlanViewer viewer)
            viewer.UpdateFromFlatPlan();
    }

    private static void OnLayeredPlanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlanViewer viewer)
            viewer.UpdateFromLayeredPlan();
    }

    private static void OnActivePlanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlanViewer viewer)
            viewer.UpdateFromActivePlan();
    }

    private void UpdateFromActivePlan()
    {
        if (ActivePlan == null || ActivePlan.Steps.Count == 0)
        {
            _root.Visibility = Visibility.Collapsed;
            return;
        }

        _root.Visibility = Visibility.Visible;
        ClearContentExceptTitle();
        _titleBlock.Text = string.IsNullOrEmpty(ActivePlan.Title) ? "计划" : ActivePlan.Title;

        int completed = 0, total = ActivePlan.Steps.Count;

        // 按阶段分组渲染
        var phaseOrder = new[] { PlanCreationPhase.Composition, PlanCreationPhase.Object,
                                 PlanCreationPhase.Material, PlanCreationPhase.Adjustment };
        var phaseMap = ActivePlan.StepsByPhase;
        bool hasPhaseContent = false;

        foreach (var phase in phaseOrder)
        {
            if (!phaseMap.TryGetValue(phase, out var phaseSteps) || phaseSteps.Count == 0)
                continue;

            hasPhaseContent = true;
            var headerColor = PhaseColorFromActive(phase);
            _contentPanel.Children.Add(BuildPhaseHeaderFromActive(phase, headerColor));

            foreach (var step in phaseSteps)
            {
                if (step.Status == StepStatus.Completed) completed++;
                _contentPanel.Children.Add(BuildStepRowFromActive(step));
            }
        }

        // 通用阶段的步骤
        if (phaseMap.TryGetValue(PlanCreationPhase.General, out var generalSteps) && generalSteps.Count > 0)
        {
            hasPhaseContent = true;
            foreach (var step in generalSteps)
            {
                if (step.Status == StepStatus.Completed) completed++;
                _contentPanel.Children.Add(BuildStepRowFromActive(step));
            }
        }

        if (!hasPhaseContent)
        {
            foreach (var step in ActivePlan.Steps)
            {
                if (step.Status == StepStatus.Completed) completed++;
                _contentPanel.Children.Add(BuildStepRowFromActive(step));
            }
        }

        AddProgressBar(completed, total);
    }

    private void UpdateFromFlatPlan()
    {
        if (Plan == null || Plan.Steps.Count == 0)
        {
            _root.Visibility = Visibility.Collapsed;
            return;
        }

        // If a layered plan is set, prefer it
        if (LayeredPlan != null && LayeredPlan.Steps.Count > 0)
        {
            UpdateFromLayeredPlan();
            return;
        }

        _root.Visibility = Visibility.Visible;
        RebuildUI(Plan.Title, null, Plan.Steps);
    }

    private void UpdateFromLayeredPlan()
    {
        if (LayeredPlan == null || LayeredPlan.Steps.Count == 0)
        {
            _root.Visibility = Visibility.Collapsed;
            return;
        }

        _root.Visibility = Visibility.Visible;
        RebuildLayeredUI(LayeredPlan);
    }

    /// <summary>Rebuild UI for a flat (non-layered) plan.</summary>
    private void RebuildUI(string title, Dictionary<CreationPhase, List<LayeredTaskStep>>? stepsByPhase,
                           IList<MdTaskStep>? flatSteps)
    {
        ClearContentExceptTitle();
        _titleBlock.Text = string.IsNullOrEmpty(title) ? "Task Plan" : title;

        int completed = 0, total = 0;

        if (flatSteps != null)
        {
            total = flatSteps.Count;
            foreach (var step in flatSteps)
            {
                if (step.Status == "completed") completed++;
                _contentPanel.Children.Add(BuildStepRow(step.Status, step.Description));
            }
        }

        AddProgressBar(completed, total);
    }

    /// <summary>Rebuild UI for a 4-phase layered plan with phase section headers.</summary>
    private void RebuildLayeredUI(LayeredPlan plan)
    {
        ClearContentExceptTitle();
        _titleBlock.Text = string.IsNullOrEmpty(plan.Title) ? "Layered Plan" : plan.Title;

        int completed = 0, total = plan.Steps.Count;
        var phaseOrder = new[] { CreationPhase.Composition, CreationPhase.Object, CreationPhase.Material, CreationPhase.Adjustment };
        var phaseMap = plan.StepsByPhase;
        bool hasPhaseContent = false;

        foreach (var phase in phaseOrder)
        {
            if (!phaseMap.TryGetValue(phase, out var phaseSteps) || phaseSteps.Count == 0)
                continue;

            hasPhaseContent = true;
            var headerColor = PhaseColor(phase);
            _contentPanel.Children.Add(BuildPhaseHeader(phase, headerColor));

            foreach (var step in phaseSteps)
            {
                if (step.Status == "completed") completed++;
                _contentPanel.Children.Add(BuildStepRow(step.Status, step.Description));
            }
        }

        // Steps without a specific phase
        if (phaseMap.TryGetValue(CreationPhase.General, out var generalSteps) && generalSteps.Count > 0)
        {
            hasPhaseContent = true;
            foreach (var step in generalSteps)
            {
                if (step.Status == "completed") completed++;
                _contentPanel.Children.Add(BuildStepRow(step.Status, step.Description));
            }
        }

        if (!hasPhaseContent)
        {
            // Fallback: render as flat list
            foreach (var step in plan.Steps)
            {
                if (step.Status == "completed") completed++;
                _contentPanel.Children.Add(BuildStepRow(step.Status, step.Description));
            }
        }

        AddProgressBar(completed, total);
    }

    private void ClearContentExceptTitle()
    {
        _contentPanel.Children.Clear();
        _contentPanel.Children.Add(_titleBlock);
    }

    private static StackPanel BuildStepRow(string status, string description)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

        var indicator = new TextBlock
        {
            Text = status switch
            {
                "completed" => "✓",
                "in_progress" => "◉",
                "failed" => "✗",
                _ => "○"
            },
            Foreground = new SolidColorBrush(status switch
            {
                "completed" => DoneColor,
                "in_progress" => InProgressColor,
                "failed" => FailedColor,
                _ => MutedColor
            }),
            FontSize = 11,
            Width = 16
        };

        var text = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = new SolidColorBrush(TextColor),
            TextWrapping = TextWrapping.Wrap
        };

        row.Children.Add(indicator);
        row.Children.Add(text);
        return row;
    }

    private static Border BuildPhaseHeader(CreationPhase phase, Color accent)
    {
        var name = LayeredPlan.PhaseDisplayName(phase);
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 4, 0, 2),
            Child = new TextBlock
            {
                Text = $"▸ {name}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent)
            }
        };
        return badge;
    }

    private void AddProgressBar(int completed, int total)
    {
        if (total <= 0) return;

        var pct = (double)completed / total;
        var progressBorder = new Border
        {
            Height = 3,
            Background = new SolidColorBrush(TrackBg),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var progressFill = new Border
        {
            Width = pct * ProgressBarWidth,
            Height = 3,
            Background = new SolidColorBrush(FillColor),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        progressBorder.Child = progressFill;
        _contentPanel.Children.Add(progressBorder);

        var summary = new TextBlock
        {
            Text = $"{completed}/{total} done ({(int)(pct * 100)}%)",
            FontSize = 10,
            Foreground = new SolidColorBrush(MutedColor),
            Margin = new Thickness(0, 2, 0, 0)
        };
        _contentPanel.Children.Add(summary);
    }

    private static Color PhaseColor(CreationPhase phase) => phase switch
    {
        CreationPhase.Composition => PhaseCompColor,
        CreationPhase.Object => PhaseObjColor,
        CreationPhase.Material => PhaseMatColor,
        CreationPhase.Adjustment => PhaseAdjColor,
        _ => MutedColor
    };

    // ── ActivePlan 支持方法 ──

    private static StackPanel BuildStepRowFromActive(PlanStep step)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

        var isCurrent = step.Status == StepStatus.InProgress;
        var indicator = new TextBlock
        {
            Text = step.Status switch
            {
                StepStatus.Completed => "✓",
                StepStatus.InProgress => "◉",
                StepStatus.Failed => "✗",
                StepStatus.Skipped => "–",
                _ => "○"
            },
            Foreground = new SolidColorBrush(step.Status switch
            {
                StepStatus.Completed => DoneColor,
                StepStatus.InProgress => InProgressColor,
                StepStatus.Failed => FailedColor,
                StepStatus.Skipped => MutedColor,
                _ => MutedColor
            }),
            FontSize = 11,
            Width = 16
        };

        var text = new TextBlock
        {
            Text = step.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(isCurrent ? TitleColor : TextColor),
            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

        row.Children.Add(indicator);
        row.Children.Add(text);
        return row;
    }

    private static Border BuildPhaseHeaderFromActive(PlanCreationPhase phase, Color accent)
    {
        var name = ActivePlan.PhaseDisplayName(phase);
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 4, 0, 2),
            Child = new TextBlock
            {
                Text = $"▸ {name}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent)
            }
        };
        return badge;
    }

    private static Color PhaseColorFromActive(PlanCreationPhase phase) => phase switch
    {
        PlanCreationPhase.Composition => PhaseCompColor,
        PlanCreationPhase.Object => PhaseObjColor,
        PlanCreationPhase.Material => PhaseMatColor,
        PlanCreationPhase.Adjustment => PhaseAdjColor,
        _ => MutedColor
    };
}
