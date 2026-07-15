using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PixelAssetGenerator.Services.Localization;

namespace PixelAssetGenerator
{
    public sealed class ExportOptionsDialog : Window
    {
        private static ILocalizationService Loc => LocalizationService.Instance;
        private readonly TextBox _fileTextBox;
        private readonly ComboBox _formatComboBox;
        private readonly CheckBox _alphaCheckBox;
        public string? SelectedFilePath { get; private set; }

        public string? SelectedFormatKey { get; private set; }

        public bool SaveAlpha { get; private set; } = true;

        public ExportOptionsDialog()
        {
            Title = Loc.GetString("Export_Title");
            // Increase default size and allow resizing to avoid control clipping on small displays
            Height = 440;
            Width = 560;
            MinHeight = 390;
            MinWidth = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            var rootBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Margin = new Thickness(12)
            };
            rootBorder.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
            rootBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = Loc.GetString("Export_Header"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            header.SetResourceReference(StyleProperty, "WindowTitleText");

            var subtitle = new TextBlock
            {
                Text = Loc.GetString("Export_Subtitle"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            subtitle.SetResourceReference(StyleProperty, "WindowSubtitleText");
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);

            var formatLabel = new TextBlock
            {
                Text = Loc.GetString("Export_Format"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(formatLabel, 2);
            grid.Children.Add(formatLabel);

            _formatComboBox = new ComboBox
            {
                MinWidth = 260,
                SelectedIndex = 0,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _formatComboBox.Items.Add(new ComboBoxItem { Content = "PNG", Tag = "Png" });
            _formatComboBox.Items.Add(new ComboBoxItem { Content = "JPEG", Tag = "Jpeg" });
            _formatComboBox.Items.Add(new ComboBoxItem { Content = "BMP", Tag = "Bmp" });
            _formatComboBox.Items.Add(new ComboBoxItem { Content = "TIFF", Tag = "Tiff" });
            _formatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
            Grid.SetRow(_formatComboBox, 3);
            grid.Children.Add(_formatComboBox);

            _alphaCheckBox = new CheckBox
            {
                Content = Loc.GetString("Export_AlphaChannel"),
                Margin = new Thickness(0, 12, 0, 0),
                IsChecked = true
            };
            Grid.SetRow(_alphaCheckBox, 4);
            grid.Children.Add(_alphaCheckBox);

            // Add file path / filename controls so users can choose path within this dialog
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fileLabel = new TextBlock
            {
                Text = Loc.GetString("Export_FileLabel"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            };
            Grid.SetRow(fileLabel, 5);
            grid.Children.Add(fileLabel);

            var filePanel = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            };
            filePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _fileTextBox = new TextBox
            {
                MinWidth = 260,
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var browseButton = new Button
            {
                Content = Loc.GetString("Export_Browse"),
                Width = 88,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            browseButton.Click += BrowseButton_Click;

            Grid.SetColumn(_fileTextBox, 0);
            Grid.SetColumn(browseButton, 1);
            filePanel.Children.Add(_fileTextBox);
            filePanel.Children.Add(browseButton);
            Grid.SetRow(filePanel, 6);
            grid.Children.Add(filePanel);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = Loc.GetString("Export_Cancel"),
                Width = 88,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                IsCancel = true
            };
            cancelButton.Click += CancelButton_Click;

            var okButton = new Button
            {
                Content = Loc.GetString("Export_OK"),
                Width = 88,
                Padding = new Thickness(12, 6, 12, 6),
                IsDefault = true
            };
            okButton.SetResourceReference(StyleProperty, "AccentButton");
            okButton.Click += OkButton_Click;

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);
            Grid.SetRow(buttonPanel, 7);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(buttonPanel);

            rootBorder.Child = grid;
            Content = rootBorder;
            UpdateAlphaAvailability();
        }

        private void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            var key = GetSelectedFormatKey();
            var filter = "PNG 图像 (*.png)|*.png";
            var defaultExt = ".png";
            if (key == "Jpeg") { filter = "JPEG 图像 (*.jpg;*.jpeg)|*.jpg;*.jpeg"; defaultExt = ".jpg"; }
            else if (key == "Bmp") { filter = "BMP 图像 (*.bmp)|*.bmp"; defaultExt = ".bmp"; }
            else if (key == "Tiff") { filter = "TIFF 图像 (*.tif;*.tiff)|*.tif;*.tiff"; defaultExt = ".tif"; }

            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = $"tile{defaultExt}",
                AddExtension = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                _fileTextBox.Text = dialog.FileName;
            }
        }

        private static ControlTemplate CreateButtonTemplate(CornerRadius cornerRadius, Brush? hoverBrush, Brush? pressedBrush)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
            border.AppendChild(presenter);
            template.VisualTree = border;

            if (hoverBrush != null)
            {
                var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hoverBrush));
                hoverTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, hoverBrush));
                template.Triggers.Add(hoverTrigger);
            }

            if (pressedBrush != null)
            {
                var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, pressedBrush));
                pressedTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, pressedBrush));
                template.Triggers.Add(pressedTrigger);
            }

            return template;
        }

        private static ControlTemplate CreateComboBoxTemplate(CornerRadius cornerRadius, Brush background, Brush borderBrush)
        {
            var template = new ControlTemplate(typeof(ComboBox));

            var grid = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ComboBox.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ComboBox.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(ComboBox.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);
            grid.AppendChild(border);

            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton));
            toggleButton.Name = "ToggleButton";
            toggleButton.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
            toggleButton.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
            toggleButton.SetValue(ToggleButton.ForegroundProperty, new TemplateBindingExtension(ComboBox.ForegroundProperty));
            toggleButton.SetValue(ToggleButton.FocusableProperty, false);
            toggleButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            });
            toggleButton.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);

            var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            var toggleBorder = new FrameworkElementFactory(typeof(Border));
            toggleBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ToggleButton.BackgroundProperty));
            toggleBorder.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            toggleBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0));

            var toggleContentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            toggleContentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            toggleContentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            toggleBorder.AppendChild(toggleContentPresenter);
            toggleTemplate.VisualTree = toggleBorder;
            toggleButton.SetValue(Control.TemplateProperty, toggleTemplate);
            border.AppendChild(toggleButton);

            var dockPanel = new FrameworkElementFactory(typeof(DockPanel));
            toggleButton.AppendChild(dockPanel);

            var path = new FrameworkElementFactory(typeof(Path));
            path.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0 Z"));
            path.SetValue(Path.FillProperty, new TemplateBindingExtension(ComboBox.ForegroundProperty));
            path.SetValue(Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            path.SetValue(Path.VerticalAlignmentProperty, VerticalAlignment.Center);
            path.SetValue(FrameworkElement.WidthProperty, 12d);
            path.SetValue(FrameworkElement.HeightProperty, 8d);
            path.SetValue(DockPanel.DockProperty, Dock.Right);
            path.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0));
            dockPanel.AppendChild(path);

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(ComboBox.PaddingProperty));
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            contentPresenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            dockPanel.AppendChild(contentPresenter);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, background);
            popupBorder.SetValue(Border.BorderBrushProperty, borderBrush);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, cornerRadius);
            popupBorder.SetValue(Border.WidthProperty, new TemplateBindingExtension(ComboBox.ActualWidthProperty));
            popup.AppendChild(popupBorder);

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.MarginProperty, new Thickness(2));
            scrollViewer.SetValue(ScrollViewer.MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));
            popupBorder.AppendChild(scrollViewer);

            var itemsHost = new FrameworkElementFactory(typeof(StackPanel));
            itemsHost.SetValue(Panel.IsItemsHostProperty, true);
            scrollViewer.AppendChild(itemsHost);

            grid.AppendChild(popup);

            template.VisualTree = grid;
            return template;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFormatKey = GetSelectedFormatKey();
            SaveAlpha = _alphaCheckBox.IsChecked == true;
            SelectedFilePath = string.IsNullOrWhiteSpace(_fileTextBox.Text) ? null : _fileTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAlphaAvailability();
        }

        private void UpdateAlphaAvailability()
        {
            var key = GetSelectedFormatKey();
            var supportsAlpha = key is "Png" or "Tiff";
            _alphaCheckBox.IsEnabled = supportsAlpha;
            if (!supportsAlpha)
            {
                _alphaCheckBox.IsChecked = false;
            }
        }

        private string? GetSelectedFormatKey()
        {
            return (_formatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        }
    }
}
