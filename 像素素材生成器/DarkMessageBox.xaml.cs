using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelAssetGenerator.Services.Localization;

namespace PixelAssetGenerator
{
    public partial class DarkMessageBox : Window
    {
        public bool? Result { get; private set; }

        private static string L(string key) => LocalizationService.Instance.GetStringFast(key);

        public DarkMessageBox(string message, string title = "Notice", bool showCancel = false)
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
            MinWidth = 300;
            MaxWidth = 480;

            // 从当前主题资源读取Color
            var bg = GetThemeBrush("WindowBackground", 0x0B, 0x0E, 0x14);
            var fg = GetThemeBrush("PrimaryText", 0xC8, 0xD8, 0xF0);
            var ctrlBg = GetThemeBrush("ControlBackground", 0x14, 0x1C, 0x30);
            var ctrlBorder = GetThemeBrush("ControlBorder", 0x1E, 0x28, 0x40);
            var accent = GetThemeBrush("Accent", 0x4A, 0x80, 0xC8);
            Background = bg;
            Foreground = fg;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                FontSize = 13,
                Foreground = fg,
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid.SetRow(text, 0);
            grid.Children.Add(text);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };

            if (showCancel)
            {
                var cancelBtn = new Button
                {
                    Width = 80,
                    Height = 28,
                    Content = L("DarkMsgBox_Cancel"),
                    Background = ctrlBg,
                    Foreground = fg,
                    BorderBrush = ctrlBorder,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                cancelBtn.Click += (_, _) => { Result = false; Close(); };
                panel.Children.Add(cancelBtn);
            }

            var ok = new Button
            {
                Width = 80,
                Height = 28,
                Content = showCancel ? L("DarkMsgBox_Yes") : L("DarkMsgBox_OK"),
                Background = accent,
                Foreground = fg,
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ok.Click += (_, _) => { Result = true; Close(); };

            panel.Children.Add(ok);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);

            Content = grid;
        }

        public static bool Show(Window owner, string message, string title = "Notice")
        {
            var dlg = new DarkMessageBox(message, title) { Owner = owner };
            return dlg.ShowDialog() == true && dlg.Result == true;
        }

        public static bool? ShowConfirm(Window owner, string message, string title = "Confirm")
        {
            var dlg = new DarkMessageBox(message, title, showCancel: true) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Result;
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
