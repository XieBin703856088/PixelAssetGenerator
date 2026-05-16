using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelAssetGenerator
{
    public class DarkInputBox : Window
    {
        public string? ResultText { get; private set; }

        public DarkInputBox(string prompt, string title = "Input", string defaultText = "")
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
            MinWidth = 340;
            MaxWidth = 440;

            // 从当前主题资源读取Color
            var bg = GetThemeBrush("WindowBackground", 0x0B, 0x0E, 0x14);
            var fg = GetThemeBrush("PrimaryText", 0xC8, 0xD8, 0xF0);
            var ctrlBg = GetThemeBrush("ControlBackground", 0x14, 0x1C, 0x30);
            var ctrlBorder = GetThemeBrush("ControlBorder", 0x1E, 0x28, 0x40);
            var accent = GetThemeBrush("Accent", 0x4A, 0x80, 0xC8);
            Background = bg;
            Foreground = fg;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 提示文字
            var text = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = fg,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(text, 0);
            grid.Children.Add(text);

            // 输入框
            var inputBox = new TextBox
            {
                Text = defaultText,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Background = ctrlBg,
                Foreground = fg,
                BorderBrush = ctrlBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = fg,
                SelectionBrush = accent,
                Margin = new Thickness(0, 0, 0, 12)
            };
            inputBox.SelectAll();
            Grid.SetRow(inputBox, 1);
            grid.Children.Add(inputBox);

            // 按钮栏
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Width = 80,
                Height = 28,
                Content = "Cancel",
                Background = ctrlBg,
                Foreground = fg,
                BorderBrush = ctrlBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cancelBtn.Click += (_, _) => { ResultText = null; Close(); };

            var okBtn = new Button
            {
                Width = 80,
                Height = 28,
                Content = "OK",
                Background = accent,
                Foreground = fg,
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = true
            };
            okBtn.Click += (_, _) => { ResultText = inputBox.Text; Close(); };

            panel.Children.Add(cancelBtn);
            panel.Children.Add(okBtn);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            Content = grid;

            // 回车提交、Escape Cancel
            inputBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    ResultText = inputBox.Text;
                    Close();
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    ResultText = null;
                    Close();
                }
            };

            Loaded += (_, _) => inputBox.Focus();
        }

        /// <summary>
        /// 显示Input弹窗。返回用户Input的文字，Cancel返回 null。
        /// </summary>
        public static string? Show(Window owner, string prompt, string title = "Input", string defaultText = "")
        {
            var dlg = new DarkInputBox(prompt, title, defaultText) { Owner = owner };
            dlg.ShowDialog();
            return dlg.ResultText;
        }

        private static Brush GetThemeBrush(string resourceKey, byte fallbackR, byte fallbackG, byte fallbackB)
        {
            try
            {
                var res = Application.Current.TryFindResource(resourceKey);
                if (res is Brush brush)
                    return brush;
            }
            catch { }
            var fb = new SolidColorBrush(Color.FromRgb(fallbackR, fallbackG, fallbackB));
            fb.Freeze();
            return fb;
        }
    }
}
