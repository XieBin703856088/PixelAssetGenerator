using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;

namespace PixelAssetGenerator.Controls;

/// <summary>
/// Renders markdown text as formatted FlowDocument in a read-only RichTextBox.
/// Supports: headers, bold, italic, inline code, code blocks, bullet lists, numbered lists.
/// Code blocks include a copy button for easy clipboard access.
/// </summary>
public sealed class MarkdownViewer : RichTextBox
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(nameof(MarkdownText), typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata("", OnMarkdownChanged));

    /// <summary>
    /// 流式输出期间设为 True，MarkdownViewer 将跳过 FlowDocument 重建以避免 UI 卡死。
    /// 当从 True 变为 False 时触发最终渲染。
    /// </summary>
    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(MarkdownViewer),
            new PropertyMetadata(false, OnIsStreamingChanged));

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value ?? "");
    }

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public MarkdownViewer()
    {
        IsReadOnly = true;
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "PrimaryText");
        BorderThickness = new Thickness(0);
        IsDocumentEnabled = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        AutoWordSelection = true;
        IsTabStop = false;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ForegroundProperty)
        {
            RenderMarkdown(MarkdownText);
        }
    }

    private static void OnIsStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 流式结束时做最终完整渲染
        if (d is MarkdownViewer viewer && e.NewValue is false)
            viewer.RenderMarkdown(viewer.MarkdownText);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer && !viewer.IsStreaming)
            viewer.RenderMarkdown(e.NewValue as string ?? "");
    }

    private void RenderMarkdown(string markdown)
    {
        var foreground = Foreground ?? SystemColors.ControlTextBrush;
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            LineHeight = 1.15,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Left,
            Foreground = foreground
        };

        if (string.IsNullOrEmpty(markdown))
        {
            Document = doc;
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        ParseBlocks(doc, lines, foreground);
        Document = doc;
    }

    private static void ParseBlocks(FlowDocument doc, string[] lines, Brush foreground)
    {
        var codeBlock = false;
        var codeLines = new List<string>();
        var codeLang = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (codeBlock)
            {
                if (line.TrimStart().StartsWith("```"))
                {
                    doc.Blocks.Add(CreateCodeBlock(codeLines, codeLang, foreground));
                    codeLines.Clear();
                    codeBlock = false;
                    continue;
                }
                codeLines.Add(line);
                continue;
            }

            if (line.TrimStart().StartsWith("```"))
            {
                codeBlock = true;
                codeLang = line.TrimStart()[3..].Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                // Add vertical space between paragraphs
                doc.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 0, 0, 4) });
                continue;
            }

            var trimmed = line.TrimStart();

            // Headers
            if (trimmed.StartsWith("### "))
            {
                doc.Blocks.Add(CreateHeader(trimmed[4..], 3, foreground));
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                doc.Blocks.Add(CreateHeader(trimmed[3..], 2, foreground));
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                doc.Blocks.Add(CreateHeader(trimmed[2..], 1, foreground));
                continue;
            }

            // Horizontal rule
            if (trimmed is "---" or "***" or "___")
            {
                doc.Blocks.Add(new BlockUIContainer(new System.Windows.Shapes.Line
                {
                    X1 = 0, Y1 = 0, X2 = 200, Y2 = 0,
                    Stroke = new SolidColorBrush(Color.FromRgb(80, 90, 110)),
                    StrokeThickness = 1,
                    Margin = new Thickness(0, 4, 0, 4)
                }));
                continue;
            }

            // Bullet list
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var content = trimmed[2..];
                var para = new Paragraph { Margin = new Thickness(12, 0, 0, 2), Foreground = foreground };
                para.Inlines.Add(new Run("• ") { FontSize = 10, Foreground = foreground });
                ParseInlines(para, content, foreground);
                doc.Blocks.Add(para);
                continue;
            }

            // Numbered list
            if (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
            {
                var endIdx = 2;
                while (endIdx < trimmed.Length && char.IsDigit(trimmed[endIdx])) endIdx++;
                if (endIdx < trimmed.Length && trimmed[endIdx] == '.')
                {
                    var num = trimmed[..endIdx];
                    var content = trimmed[(endIdx + 1)..].TrimStart();
                    var para = new Paragraph { Margin = new Thickness(12, 0, 0, 2), Foreground = foreground };
                    para.Inlines.Add(new Run($"{num}. ") { FontSize = 10, Foreground = foreground });
                    ParseInlines(para, content, foreground);
                    doc.Blocks.Add(para);
                    continue;
                }
            }

            // Regular paragraph
            var para2 = new Paragraph { Margin = new Thickness(0, 0, 0, 2), Foreground = foreground };
            ParseInlines(para2, line, foreground);
            doc.Blocks.Add(para2);
        }

        // Close unclosed code block
        if (codeBlock && codeLines.Count > 0)
            doc.Blocks.Add(CreateCodeBlock(codeLines, codeLang, foreground));
    }

    private static Paragraph CreateHeader(string text, int level, Brush foreground)
    {
        var size = level switch { 1 => 18, 2 => 15, _ => 13 };
        var para = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 2),
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground
        };
        ParseInlines(para, text, foreground);
        return para;
    }

    private static Block CreateCodeBlock(List<string> codeLines, string lang, Brush foreground)
    {
        var code = string.Join("\n", codeLines);

        // 构建代码文本块
        var codeRun = new Run(code)
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 11,
            Foreground = foreground
        };
        var codePara = new Paragraph(codeRun)
        {
            Margin = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Foreground = foreground
        };

        // 代码区域容器
        var codeBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 32)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0),
            Child = new Grid()
        };

        // Row 0: 标题栏 (语言标签 + 复制按钮)
        var headerRow = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 31, 44)),
            Height = 26,
            Padding = new Thickness(8, 0, 4, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 语言标签
        var langLabel = new TextBlock
        {
            Text = !string.IsNullOrEmpty(lang) ? lang : "code",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 130, 150)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langLabel, 0);
        headerGrid.Children.Add(langLabel);

        // 复制按钮（默认隐藏，鼠标悬停代码块时显示）
        var copyBtn = new Button
        {
            Content = "⧉",
            FontSize = 11,
            Width = 20,
            Height = 18,
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 190)),
            Background = new SolidColorBrush(Color.FromRgb(36, 42, 56)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 58, 72)),
            BorderThickness = new Thickness(1),
            ToolTip = "复制代码",
            Opacity = 0,
        };
        copyBtn.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
                copyBtn.Content = "✓";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.2)
                };
                timer.Tick += (s, _) =>
                {
                    copyBtn.Content = "⧉";
                    timer.Stop();
                };
                timer.Start();
            }
            catch { }
        };

        // 鼠标悬停代码块时显示复制按钮
        codeBorder.MouseEnter += (_, _) => { copyBtn.Opacity = 1; };
        codeBorder.MouseLeave += (_, _) => { copyBtn.Opacity = 0; };
        copyBtn.MouseEnter += (_, _) => { copyBtn.Opacity = 1; };
        copyBtn.MouseLeave += (_, _) => {
            if (!codeBorder.IsMouseOver) copyBtn.Opacity = 0;
        };

        Grid.SetColumn(copyBtn, 2);
        headerGrid.Children.Add(copyBtn);

        headerRow.Child = headerGrid;

        // Row 1: 代码内容 (可滚动)
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 400,
            Content = new Border
            {
                Padding = new Thickness(4, 0, 4, 4),
                Child = new FlowDocumentScrollViewer
                {
                    Document = new FlowDocument(codePara)
                    {
                        PagePadding = new Thickness(0),
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                        FontSize = 11,
                        Background = Brushes.Transparent
                    },
                    IsTabStop = false,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    // 让 FlowDocument 自动换行
                    IsInactiveSelectionHighlightEnabled = false
                }
            }
        };

        var stack = new StackPanel();
        stack.Children.Add(headerRow);
        stack.Children.Add(scrollViewer);

        codeBorder.Child = stack;

        var container = new BlockUIContainer(codeBorder)
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        return container;
    }

    private static void ParseInlines(Paragraph para, string text, Brush foreground)
    {
        // Handle inline formatting: **bold**, *italic*, `code`
        int i = 0;
        while (i < text.Length)
        {
            // Inline code
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    var code = text[(i + 1)..end];
                    para.Inlines.Add(new Run(code)
                    {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                        FontSize = 11,
                        Background = new SolidColorBrush(Color.FromRgb(30, 35, 48)),
                        Foreground = foreground,
                    });
                    i = end + 1;
                    continue;
                }
            }

            // Bold **text**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2);
                if (end > i)
                {
                    para.Inlines.Add(new Run(text[(i + 2)..end]) { FontWeight = FontWeights.Bold, Foreground = foreground });
                    i = end + 2;
                    continue;
                }
            }

            // Italic *text*
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i)
                {
                    para.Inlines.Add(new Run(text[(i + 1)..end]) { FontStyle = FontStyles.Italic, Foreground = foreground });
                    i = end + 1;
                    continue;
                }
            }

            // Regular char
            int start = i;
            while (i < text.Length && text[i] != '`' && text[i] != '*')
                i++;
            if (i > start)
                para.Inlines.Add(new Run(text[start..i]) { Foreground = foreground });
        }
    }
}
