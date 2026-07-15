using System.Windows;
using System.Windows.Controls;
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
            WindowStyle = WindowStyle.SingleBorderWindow;
            ShowInTaskbar = false;
            MinWidth = 380;
            MaxWidth = 560;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                FontSize = 13,
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 18)
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
                    Height = 32,
                    Content = L("DarkMsgBox_Cancel"),
                    Margin = new Thickness(0, 0, 8, 0),
                    IsCancel = true
                };
                cancelBtn.Click += (_, _) => { Result = false; Close(); };
                panel.Children.Add(cancelBtn);
            }

            var ok = new Button
            {
                Width = 80,
                Height = 32,
                Content = showCancel ? L("DarkMsgBox_Yes") : L("DarkMsgBox_OK"),
                IsDefault = true
            };
            ok.SetResourceReference(StyleProperty, "AccentButton");
            ok.Click += (_, _) => { Result = true; Close(); };

            panel.Children.Add(ok);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);

            Content = grid;
        }

        public static bool Show(Window owner, string message, string title = "Notice")
        {
            var dlg = new DarkMessageBox(message, title) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Result == true;
        }

        public static bool? ShowConfirm(Window owner, string message, string title = "Confirm")
        {
            var dlg = new DarkMessageBox(message, title, showCancel: true) { Owner = owner };
            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}
