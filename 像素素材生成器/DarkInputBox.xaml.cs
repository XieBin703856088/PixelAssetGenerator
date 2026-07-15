using System.Windows;
using System.Windows.Controls;
using PixelAssetGenerator.Services.Localization;

namespace PixelAssetGenerator
{
    public class DarkInputBox : Window
    {
        public string? ResultText { get; private set; }
        private static string L(string key) => LocalizationService.Instance.GetStringFast(key);

        public DarkInputBox(string prompt, string title = "Input", string defaultText = "")
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ShowInTaskbar = false;
            MinWidth = 400;
            MaxWidth = 560;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 提示文字
            var text = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(text, 0);
            grid.Children.Add(text);

            // 输入框
            var inputBox = new TextBox
            {
                Text = defaultText,
                FontSize = 13,
                MinHeight = 34,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 18)
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
                Height = 32,
                Content = L("DarkMsgBox_Cancel"),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => { ResultText = null; Close(); };

            var okBtn = new Button
            {
                Width = 80,
                Height = 32,
                Content = L("DarkMsgBox_OK"),
                IsDefault = true
            };
            okBtn.SetResourceReference(StyleProperty, "AccentButton");
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
    }
}
