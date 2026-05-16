using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Controls;

/// <summary>
/// Sidebar control that lists saved chat sessions.
/// Fires <see cref="SessionSelected"/> when the user clicks a session,
/// and <see cref="SessionDeleteRequested"/> when the user clicks the delete button.
/// </summary>
public sealed class AiSessionListControl : Control
{
    // ── Dependency properties ───────────────────────────────────────────────

    public static readonly DependencyProperty SessionsProperty =
        DependencyProperty.Register(nameof(Sessions),
            typeof(IReadOnlyList<ChatSessionSummary>),
            typeof(AiSessionListControl),
            new PropertyMetadata(null, OnSessionsChanged));

    public static readonly DependencyProperty ActiveSessionIdProperty =
        DependencyProperty.Register(nameof(ActiveSessionId),
            typeof(string),
            typeof(AiSessionListControl),
            new PropertyMetadata(null, OnActiveChanged));

    public IReadOnlyList<ChatSessionSummary>? Sessions
    {
        get => (IReadOnlyList<ChatSessionSummary>?)GetValue(SessionsProperty);
        set => SetValue(SessionsProperty, value);
    }

    public string? ActiveSessionId
    {
        get => (string?)GetValue(ActiveSessionIdProperty);
        set => SetValue(ActiveSessionIdProperty, value);
    }

    // ── Events ──────────────────────────────────────────────────────────────

    public event Action<string>? SessionSelected;
    public event Action<string>? SessionDeleteRequested;

    // ── Private fields ───────────────────────────────────────────────────────

    private readonly StackPanel _list;
    private readonly ScrollViewer _scroll;

    // ── Constructor ─────────────────────────────────────────────────────────

    public AiSessionListControl()
    {
        _list = new StackPanel { Margin = new Thickness(0) };

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list
        };

        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 28)),
            Child = _scroll
        };

        AddVisualChild(root);
        AddLogicalChild(root);
        _rootBorder = root;
    }

    private readonly Border _rootBorder;

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _rootBorder;

    protected override Size MeasureOverride(Size availableSize)
    {
        _rootBorder.Measure(availableSize);
        return _rootBorder.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _rootBorder.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ── Rebuild list ─────────────────────────────────────────────────────────

    private static void OnSessionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AiSessionListControl)d).Rebuild();

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AiSessionListControl)d).Rebuild();

    private void Rebuild()
    {
        _list.Children.Clear();
        if (Sessions == null) return;

        foreach (var session in Sessions)
        {
            var isActive = session.Id == ActiveSessionId;
            var row = BuildSessionRow(session, isActive);
            _list.Children.Add(row);
        }
    }

    private UIElement BuildSessionRow(ChatSessionSummary session, bool isActive)
    {
        var activeBg = new SolidColorBrush(Color.FromRgb(30, 40, 60));
        var normalBg = new SolidColorBrush(Color.FromRgb(18, 24, 36));
        var hoverBg = new SolidColorBrush(Color.FromRgb(24, 32, 48));

        var titleText = new TextBlock
        {
            Text = session.Title,
            FontSize = 12,
            Foreground = isActive
                ? new SolidColorBrush(Color.FromRgb(200, 220, 255))
                : new SolidColorBrush(Color.FromRgb(160, 175, 200)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dateText = new TextBlock
        {
            Text = session.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 95, 120)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var planDot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(80, 160, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = session.HasPlan ? Visibility.Visible : Visibility.Collapsed,
            ToolTip = "Has plan file"
        };

        var deleteBtn = new Button
        {
            Content = "×",
            FontSize = 12,
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Delete this conversation",
            Visibility = Visibility.Hidden   // shown on hover
        };
        var capturedId = session.Id;
        deleteBtn.Click += (_, _) => SessionDeleteRequested?.Invoke(capturedId);

        var metaPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        metaPanel.Children.Add(planDot);
        metaPanel.Children.Add(dateText);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(titleText);
        textPanel.Children.Add(metaPanel);

        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textPanel, 0);
        Grid.SetColumn(deleteBtn, 1);
        innerGrid.Children.Add(textPanel);
        innerGrid.Children.Add(deleteBtn);

        var border = new Border
        {
            Padding = new Thickness(12, 8, 8, 8),
            Background = isActive ? activeBg : normalBg,
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 38, 55)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
            Child = innerGrid
        };

        border.MouseEnter += (_, _) =>
        {
            if (!isActive) border.Background = hoverBg;
            deleteBtn.Visibility = Visibility.Visible;
        };
        border.MouseLeave += (_, _) =>
        {
            if (!isActive) border.Background = normalBg;
            deleteBtn.Visibility = Visibility.Hidden;
        };
        border.MouseLeftButtonUp += (_, _) => SessionSelected?.Invoke(capturedId);

        return border;
    }
}
