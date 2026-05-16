using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PixelAssetGenerator.Services;

namespace PixelAssetGenerator;

public partial class AiSettingsWindow : Window
{
	private bool _isLoading = true;
	private bool _isSyncingScroll = false;

	// In-memory list of profile button borders for UI updates
	private readonly List<Border> _profileItems = new();

	public AiSettingsWindow()
	{
		InitializeComponent();

		_isLoading = true;
		RebuildProfileListUI();
		LoadProfileToUI(AiConfigManager.Current.ActiveProfileIndex);
		RebuildModelChips();
		_isLoading = false;

		// Wire live preview events
		ProviderCombo.SelectionChanged += (_, _) => { if (!_isLoading) { UpdateProviderDefaults(); UpdateLiveUI(); } };
		ApiKeyBox.PasswordChanged += (_, _) => { if (!_isLoading) UpdateLiveUI(); };
		BaseUrlBox.TextChanged += (_, _) => { if (!_isLoading) UpdateLiveUI(); };
		TemperatureSlider.ValueChanged += (_, _) => { if (!_isLoading) UpdateLiveUI(); };
		ReasoningEffortCombo.SelectionChanged += (_, _) => { if (!_isLoading) UpdateLiveUI(); };
		MaxTokensBox.TextChanged += (_, _) => { if (!_isLoading) UpdateLiveUI(); };

		ContextLimitSlider.ValueChanged += (_, _) =>
		{
			if (!_isLoading)
			{
				ContextLimitBox.Text = ((int)ContextLimitSlider.Value).ToString();
				UpdateLiveUI();
			}
		};
		ContextLimitBox.LostFocus += (_, _) => SyncContextLimitFromTextBox();
		ContextLimitBox.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter) SyncContextLimitFromTextBox();
		};

		// Sync side scroll bar with ScrollViewer
		SettingsScrollViewer.ScrollChanged += SettingsScrollViewer_ScrollChanged;
		SyncSideScrollBar();

		// Model dropdown: force popup to open upward
		ModelCombo.DropDownOpened += (_, _) =>
		{
			if (ModelCombo.Template.FindName("PART_Popup", ModelCombo) is System.Windows.Controls.Primitives.Popup popup)
				popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
		};
	}

	// ═══════════════════════════════════════════════════════════════
	//  Side ScrollBar ↔ ScrollViewer sync (normalized 0~1)
	// ═══════════════════════════════════════════════════════════════

	private double GetScrollRange()
	{
		var r = SettingsScrollViewer.ExtentHeight - SettingsScrollViewer.ViewportHeight;
		return r > 0 ? r : 1;
	}

	private void SyncSideScrollBar()
	{
		var sv = SettingsScrollViewer;
		var range = sv.ExtentHeight - sv.ViewportHeight;
		if (range <= 0)
		{
			SideScrollBar.IsEnabled = false;
			SideScrollBar.Visibility = Visibility.Collapsed;
			SideScrollBar.Value = 0;
		}
		else
		{
			SideScrollBar.Minimum = 0;
			SideScrollBar.Maximum = 1;
			SideScrollBar.ViewportSize = sv.ViewportHeight / range;
			SideScrollBar.SmallChange = 16.0 / range;
			SideScrollBar.LargeChange = sv.ViewportHeight / range;
			SideScrollBar.IsEnabled = true;
			SideScrollBar.Visibility = Visibility.Visible;
		}
	}

	private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (_isSyncingScroll) return;
		_isSyncingScroll = true;
		SideScrollBar.Value = SettingsScrollViewer.VerticalOffset / GetScrollRange();
		_isSyncingScroll = false;
	}

	private void SideScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (_isSyncingScroll) return;
		_isSyncingScroll = true;
		SettingsScrollViewer.ScrollToVerticalOffset(e.NewValue * GetScrollRange());
		_isSyncingScroll = false;
	}

	// ═══════════════════════════════════════════════════════════════
	//  Profile 列表 UI
	// ═══════════════════════════════════════════════════════════════

	private void RebuildProfileListUI()
	{
		ProfileListPanel.Children.Clear();
		_profileItems.Clear();

		var profiles = AiConfigManager.Current.Profiles;
		for (int i = 0; i < profiles.Count; i++)
		{
			var idx = i; // closure capture
			var profile = profiles[i];

			var item = new Border
			{
				Style = (Style)FindResource("ProfileListItem"),
				Background = i == AiConfigManager.Current.ActiveProfileIndex
					? (Brush)FindResource("NodeHeaderBackground")
					: Brushes.Transparent,
				Tag = idx
			};

			var innerGrid = new Grid();
			innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var dot = new Border
			{
				Width = 8, Height = 8,
				CornerRadius = new CornerRadius(4),
				Background = (Brush)FindResource("Accent"),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 8, 0),
				Visibility = i == AiConfigManager.Current.ActiveProfileIndex
					? Visibility.Visible : Visibility.Collapsed
			};
			Grid.SetColumn(dot, 0);
			innerGrid.Children.Add(dot);

			var nameBlock = new TextBlock
			{
				Text = profile.Name,
				FontSize = 13,
				FontWeight = i == AiConfigManager.Current.ActiveProfileIndex
					? FontWeights.SemiBold : FontWeights.Normal,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			Grid.SetColumn(nameBlock, 1);
			innerGrid.Children.Add(nameBlock);

			item.Child = innerGrid;

			item.MouseLeftButtonUp += (_, _) => SelectProfile(idx);
			item.MouseEnter += (_, _) =>
			{
				if (idx != AiConfigManager.Current.ActiveProfileIndex)
					item.Background = (Brush)FindResource("PanelBackground");
			};
			item.MouseLeave += (_, _) =>
			{
				if (idx != AiConfigManager.Current.ActiveProfileIndex)
					item.Background = Brushes.Transparent;
			};

			ProfileListPanel.Children.Add(item);
			_profileItems.Add(item);
		}
	}

	private void RefreshProfileListItem(int index)
	{
		if (index < 0 || index >= _profileItems.Count) return;
		var item = _profileItems[index];
		var profile = AiConfigManager.Current.Profiles[index];
		var isActive = index == AiConfigManager.Current.ActiveProfileIndex;

		item.Background = isActive
			? (Brush)FindResource("NodeHeaderBackground")
			: Brushes.Transparent;

		if (item.Child is Grid grid && grid.Children.Count >= 2)
		{
			// dot
			if (grid.Children[0] is Border dot)
				dot.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
			// name
			if (grid.Children[1] is TextBlock tb)
			{
				tb.Text = profile.Name;
				tb.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
			}
		}
	}

	private void SelectProfile(int index)
	{
		if (_isLoading) return;
		if (index == AiConfigManager.Current.ActiveProfileIndex) return;

		// Save current settings to current profile
		var current = BuildSettingsFromControls();
		SaveToProfile(AiConfigManager.Current.ActiveProfileIndex, current);

		// Switch
		AiConfigManager.Current.SwitchProfile(index);
		RebuildProfileListUI();

		// Load new profile
		_isLoading = true;
		LoadProfileToUI(index);
		RebuildModelChips();
		_isLoading = false;
	}

	// ═══════════════════════════════════════════════════════════════
	//  加载 / 保存 Profile
	// ═══════════════════════════════════════════════════════════════

	private void LoadProfileToUI(int index)
	{
		var settings = AiConfigManager.Current.Profiles[index].Settings;
		settings.Normalize();

		ProfileNameBox.Text = AiConfigManager.Current.Profiles[index].Name;

		ProviderCombo.SelectedIndex = settings.Provider switch
		{
			"Anthropic Claude" => 1,
			"Local OpenAI Compatible" => 2,
			_ => 0
		};
		ApiKeyBox.Password = settings.ApiKey ?? "";
		BaseUrlBox.Text = settings.BaseUrl ?? "https://api.openai.com/v1";

		// Populate model combo with all models
		ModelCombo.ItemsSource = null;
		ModelCombo.Items.Clear();
		foreach (var m in settings.Models)
			ModelCombo.Items.Add(m);
		ModelCombo.Text = settings.Model ?? "gpt-4o-mini";

		TemperatureSlider.Value = settings.Temperature;
		SyncReasoningEffortComboFromSettings();
		MaxTokensBox.Text = settings.MaxTokens.ToString();
		ContextLimitSlider.Value = settings.ContextLimit;
		ContextLimitBox.Text = settings.ContextLimit.ToString();

		UpdateLiveUI();
	}

	private void SaveToProfile(int index, AiSettings settings)
	{
		if (index >= 0 && index < AiConfigManager.Current.Profiles.Count)
		{
			AiConfigManager.Current.Profiles[index].Settings = settings;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	//  Profile 管理
	// ═══════════════════════════════════════════════════════════════

	private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_isLoading) return;
		var idx = AiConfigManager.Current.ActiveProfileIndex;
		var name = ProfileNameBox.Text.Trim();
		if (!string.IsNullOrEmpty(name))
		{
			AiConfigManager.Current.RenameProfile(idx, name);
			RefreshProfileListItem(idx);
		}
	}

	private void AddProfileButton_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new InputDialog("新建配置", "请输入配置名称：",
			$"配置 {AiConfigManager.Current.Profiles.Count + 1}");
		if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
		{
			// Save current first
			var current = BuildSettingsFromControls();
			SaveToProfile(AiConfigManager.Current.ActiveProfileIndex, current);

			AiConfigManager.Current.AddProfile(dialog.Value.Trim());
			RebuildProfileListUI();
			RebuildProfileListUI();
			_isLoading = true;
			LoadProfileToUI(AiConfigManager.Current.ActiveProfileIndex);
			RebuildModelChips();
			_isLoading = false;
		}
	}

	private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
	{
		var currentName = AiConfigManager.Current.Profiles[AiConfigManager.Current.ActiveProfileIndex].Name;
		var dialog = new InputDialog("复制配置", "请输入新配置名称：", $"{currentName} (副本)");
		if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
		{
			var current = BuildSettingsFromControls();
			SaveToProfile(AiConfigManager.Current.ActiveProfileIndex, current);

			AiConfigManager.Current.DuplicateCurrentProfile(dialog.Value.Trim());
			RebuildProfileListUI();
			_isLoading = true;
			LoadProfileToUI(AiConfigManager.Current.ActiveProfileIndex);
			RebuildModelChips();
			_isLoading = false;
		}
	}

	private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
	{
		if (AiConfigManager.Current.Profiles.Count <= 1)
		{
			MessageBox.Show("至少保留一个配置。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var index = AiConfigManager.Current.ActiveProfileIndex;
		var name = AiConfigManager.Current.Profiles[index].Name;

		var result = MessageBox.Show($"确定要删除配置「{name}」吗？", "删除配置",
			MessageBoxButton.YesNo, MessageBoxImage.Warning);
		if (result != MessageBoxResult.Yes) return;

		AiConfigManager.Current.DeleteProfile(index);
		RebuildProfileListUI();
		_isLoading = true;
		LoadProfileToUI(AiConfigManager.Current.ActiveProfileIndex);
		RebuildModelChips();
		_isLoading = false;
	}

	// ═══════════════════════════════════════════════════════════════
	//  Model 多模型管理
	// ═══════════════════════════════════════════════════════════════

	private class ModelChipItem
	{
		public string ModelName { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public bool IsActive { get; set; }
	}

	private void RebuildModelChips()
	{
		var settings = AiConfigManager.Current.Settings;
		if (settings.Models.Count <= 1)
		{
			ModelChipList.Visibility = Visibility.Collapsed;
			return;
		}

		ModelChipList.Visibility = Visibility.Visible;
		var items = settings.Models.Select(m => new ModelChipItem
		{
			ModelName = m,
			DisplayName = m == settings.Model ? $"✦ {m}" : m,
			IsActive = m == settings.Model
		}).ToList();
		ModelChipList.ItemsSource = items;
	}

	private void AddModelButton_Click(object sender, RoutedEventArgs e)
	{
		var model = ModelCombo.Text?.Trim();
		if (string.IsNullOrWhiteSpace(model)) return;

		var settings = AiConfigManager.Current.Settings;
		if (!settings.Models.Contains(model))
		{
			settings.Models.Add(model);
			ModelCombo.Items.Add(model);
		}
		settings.ActiveModelIndex = settings.Models.IndexOf(model);
		settings.Model = model;
		RebuildModelChips();
		UpdateLiveUI();
	}

	private void RemoveModelChip_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is string modelName)
		{
			var settings = AiConfigManager.Current.Settings;
			if (settings.Models.Count <= 1) return; // keep at least one
			settings.Models.Remove(modelName);
			ModelCombo.Items.Remove(modelName);

			if (!settings.Models.Contains(settings.Model))
			{
				settings.Model = settings.Models.FirstOrDefault() ?? "";
				settings.ActiveModelIndex = settings.Models.IndexOf(settings.Model);
				ModelCombo.Text = settings.Model;
			}
			RebuildModelChips();
			UpdateLiveUI();
		}
	}

	private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isLoading) return;
		if (ModelCombo.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
		{
			var settings = AiConfigManager.Current.Settings;
			settings.Model = model;
			if (!settings.Models.Contains(model))
			{
				settings.Models.Add(model);
			}
			settings.ActiveModelIndex = settings.Models.IndexOf(model);
			RebuildModelChips();
			UpdateLiveUI();
		}
	}

	private void ModelCombo_TextChanged(object sender, TextChangedEventArgs e)
	{
		// Live preview will fire from the text-changed wire-up in constructor
	}

	private void ModelCombo_LostFocus(object sender, RoutedEventArgs e)
	{
		if (_isLoading) return;
		var model = ModelCombo.Text?.Trim();
		if (string.IsNullOrWhiteSpace(model)) return;

		var settings = AiConfigManager.Current.Settings;
		settings.Model = model;
		if (!settings.Models.Contains(model))
		{
			settings.Models.Add(model);
			ModelCombo.Items.Add(model);
		}
		settings.ActiveModelIndex = settings.Models.IndexOf(model);
		RebuildModelChips();
	}

	// ═══════════════════════════════════════════════════════════════
	//  UI 更新
	// ═══════════════════════════════════════════════════════════════

	private void SyncReasoningEffortComboFromSettings()
	{
		var effort = AiConfigManager.Current.Settings.ReasoningEffort;
		for (int i = 0; i < ReasoningEffortCombo.Items.Count; i++)
		{
			if (ReasoningEffortCombo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
				item.Tag?.ToString() == effort)
			{
				ReasoningEffortCombo.SelectedIndex = i;
				return;
			}
		}
		// Default to "off"
		ReasoningEffortCombo.SelectedIndex = 0;
	}

	private void ReasoningEffortCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		// Handled by the live preview wire-up in constructor
	}

	private void SyncContextLimitFromTextBox()
	{
		if (int.TryParse(ContextLimitBox.Text, out var val))
		{
			val = Math.Clamp(val, 4096, 1048576);
			ContextLimitSlider.Value = val;
		}
		ContextLimitBox.Text = ((int)ContextLimitSlider.Value).ToString();
	}

	private void UpdateLiveUI()
	{
		var settings = BuildSettingsFromControls();
		UpdateConfigFile(settings);
	}

	private void UpdateConfigFile(AiSettings settings)
	{
		ConfigFileBox.Text = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
	}

	private void UpdateProviderDefaults()
	{
		var isAnthropic = ProviderCombo.SelectedIndex == 1;
		var isLocal = ProviderCombo.SelectedIndex == 2;
		var settings = AiConfigManager.Current.Settings;

		string defaultModel;
		if (isAnthropic)
		{
			if (string.IsNullOrWhiteSpace(BaseUrlBox.Text) || !BaseUrlBox.Text.Contains("anthropic"))
				BaseUrlBox.Text = "https://api.anthropic.com";
			defaultModel = "claude-sonnet-4-20250514";
		}
		else if (isLocal)
		{
			if (string.IsNullOrWhiteSpace(BaseUrlBox.Text) || BaseUrlBox.Text.Contains("openai.com") || BaseUrlBox.Text.Contains("anthropic.com"))
				BaseUrlBox.Text = "http://127.0.0.1:11434/v1";
			defaultModel = "qwen2.5:latest";
		}
		else
		{
			if (string.IsNullOrWhiteSpace(BaseUrlBox.Text) || BaseUrlBox.Text.Contains("anthropic"))
				BaseUrlBox.Text = "https://api.openai.com/v1";
			defaultModel = "gpt-4o-mini";
		}

		// Update model combo with provider default if model field is empty
		var currentModel = ModelCombo.Text?.Trim();
		if (string.IsNullOrWhiteSpace(currentModel) || !settings.Models.Contains(currentModel))
		{
			ModelCombo.Text = defaultModel;
			if (!settings.Models.Contains(defaultModel))
			{
				settings.Models.Add(defaultModel);
				ModelCombo.Items.Add(defaultModel);
			}
			settings.Model = defaultModel;
			RebuildModelChips();
		}
	}

	private void ProviderCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		if (!_isLoading)
		{
			UpdateProviderDefaults();
			UpdateLiveUI();
		}
	}

	// ═══════════════════════════════════════════════════════════════
	//  连接测试
	// ═══════════════════════════════════════════════════════════════

	private async void TestButton_Click(object sender, RoutedEventArgs e)
	{
		TestButton.IsEnabled = false;
		TestResultText.Text = "连接中...";

		try
		{
			var settings = BuildSettingsFromControls();
			using var httpClient = new HttpClient();
			httpClient.Timeout = TimeSpan.FromSeconds(15);

			if (!settings.IsAnthropicProvider)
			{
				if (!string.IsNullOrWhiteSpace(settings.ApiKey))
					httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

				var payload = new { model = settings.Model ?? "gpt-4o-mini", messages = new[] { new { role = "user", content = "Hello" } }, max_tokens = Math.Min(settings.MaxTokens, 10) };
				var json = JsonSerializer.Serialize(payload);
				var content = new StringContent(json, Encoding.UTF8, "application/json");
				var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? "https://api.openai.com";
				var url = baseUrl.Contains("/v1") ? $"{baseUrl}/chat/completions" : $"{baseUrl}/v1/chat/completions";
				var response = await httpClient.PostAsync(url, content);
				TestResultText.Text = response.IsSuccessStatusCode ? "连接成功！" : $"连接失败 ({(int)response.StatusCode}): {response.ReasonPhrase}";
			}
			else
			{
				httpClient.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey ?? "");
				httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
				var payload = new { model = settings.Model ?? "claude-sonnet-4-20250514", max_tokens = Math.Min(settings.MaxTokens, 10), messages = new[] { new { role = "user", content = "Hello" } } };
				var json = JsonSerializer.Serialize(payload);
				var content = new StringContent(json, Encoding.UTF8, "application/json");
				var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? "https://api.anthropic.com";
				var response = await httpClient.PostAsync($"{baseUrl}/v1/messages", content);
				TestResultText.Text = response.IsSuccessStatusCode ? "连接成功！" : $"连接失败 ({(int)response.StatusCode}): {response.ReasonPhrase}";
			}
		}
		catch (Exception ex) { TestResultText.Text = $"连接失败: {ex.Message}"; }
		finally { TestButton.IsEnabled = true; }
	}

	// ═══════════════════════════════════════════════════════════════
	//  保存 / 取消
	// ═══════════════════════════════════════════════════════════════

	private string GetReasoningEffortFromCombo()
	{
		if (ReasoningEffortCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
			return item.Tag?.ToString() ?? "off";
		return "off";
	}

	private AiSettings BuildSettingsFromControls()
	{
		var model = ModelCombo.Text.Trim();
		var models = AiConfigManager.Current.Settings.Models.ToList();
		// Ensure the current model is in the Models list
		if (!string.IsNullOrWhiteSpace(model) && !models.Contains(model))
			models.Add(model);
		var activeIndex = models.IndexOf(model);
		if (activeIndex < 0) activeIndex = 0;

		return new()
		{
			Provider = ProviderCombo.SelectedIndex switch { 1 => "Anthropic Claude", 2 => "Local OpenAI Compatible", _ => "OpenAI Compatible" },
			ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password,
			BaseUrl = BaseUrlBox.Text.Trim(),
			Model = model,
			Temperature = TemperatureSlider.Value,
			ContextLimit = (int)ContextLimitSlider.Value,
			MaxTokens = ParseMaxTokens(),
			MaxToolCallRounds = AiConfigManager.Current.Settings.MaxToolCallRounds,
			EnableScriptNode = AiConfigManager.Current.Settings.EnableScriptNode,
			ReasoningEffort = GetReasoningEffortFromCombo(),
			CustomConfigCode = AiConfigManager.Current.Settings.CustomConfigCode,
			Models = models,
			ActiveModelIndex = activeIndex
		};
	}

	private int ParseMaxTokens()
	{
		if (!int.TryParse(MaxTokensBox.Text, out var maxTokens))
			maxTokens = AiConfigManager.Current.Settings.MaxTokens;
		maxTokens = Math.Clamp(maxTokens, 1, 1_048_576);
		if (MaxTokensBox.Text != maxTokens.ToString())
			MaxTokensBox.Text = maxTokens.ToString();
		return maxTokens;
	}

	private void OkButton_Click(object sender, RoutedEventArgs e)
	{
		var controlSettings = BuildSettingsFromControls();

		// Parse JSON override for advanced fields
		var json = ConfigFileBox.Text.Trim();
		if (!string.IsNullOrEmpty(json) && json.StartsWith("{"))
		{
			try
			{
				var parsed = JsonSerializer.Deserialize<AiSettings>(json, SettingsService.JsonOptions);
				if (parsed != null)
				{
					parsed.Normalize();
					// ReasoningEffort 由 combo 决定，不从 JSON 覆盖
					controlSettings.MaxToolCallRounds = parsed.MaxToolCallRounds;
					controlSettings.EnableScriptNode = parsed.EnableScriptNode;
					controlSettings.CustomConfigCode = parsed.CustomConfigCode;
				}
			}
			catch (JsonException ex)
			{
				MessageBox.Show($"AI 配置 JSON 格式错误:\n{ex.Message}",
					"配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}

		// Save
		SaveToProfile(AiConfigManager.Current.ActiveProfileIndex, controlSettings);
		controlSettings.Normalize();
		AiConfigManager.Current.Profiles[AiConfigManager.Current.ActiveProfileIndex].Settings = controlSettings;
		AiConfigManager.Current.Save();
		DialogResult = true;
		Close();
	}

	private void CancelButton_Click(object? sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (e.Delta == 0) return;

		// Prevent the Slider from changing its value on mouse wheel.
		// Let the event bubble up to the parent ScrollViewer so that
		// the page scrolls instead.
		e.Handled = true;

		// Re-raise the wheel event on the parent so the ScrollViewer
		// can handle it naturally with correct direction.
		if (sender is Slider slider)
		{
			var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
			{
				RoutedEvent = UIElement.MouseWheelEvent
			};
			slider.RaiseEvent(args);
		}
	}

	private void ContextLimitBox_LostFocus(object sender, RoutedEventArgs e)
		=> SyncContextLimitFromTextBox();

	private void ContextLimitBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter) SyncContextLimitFromTextBox();
	}
}

// ═══════════════════════════════════════════════════════════════════
//  Input Dialog
// ═══════════════════════════════════════════════════════════════════

public sealed class InputDialog : Window
{
	private readonly TextBox _textBox;

	public string? Value => _textBox.Text.Trim();

	public InputDialog(string title, string message, string defaultValue = "")
	{
		Title = title;
		Width = 380;
		Height = 180;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

		// Apply app theme
		Background = TryFindResource("WindowBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(32, 32, 32));
		Foreground = TryFindResource("PrimaryText") as Brush ?? new SolidColorBrush(Colors.White);

		var panel = new StackPanel { Margin = new Thickness(16, 16, 16, 16) };

		panel.Children.Add(new TextBlock
		{
			Text = message,
			FontSize = 12,
			Margin = new Thickness(0, 0, 0, 10),
			TextWrapping = TextWrapping.Wrap,
			Foreground = TryFindResource("PrimaryText") as Brush ?? Brushes.White
		});

		_textBox = new TextBox
		{
			Text = defaultValue,
			FontSize = 13,
			Padding = new Thickness(8, 5, 8, 5),
			Margin = new Thickness(0, 0, 0, 14),
			Background = TryFindResource("ControlBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(51, 51, 51)),
			Foreground = TryFindResource("PrimaryText") as Brush ?? new SolidColorBrush(Colors.White),
			BorderBrush = TryFindResource("ControlBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(85, 85, 85))
		};
		_textBox.SelectAll();
		_textBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ok(); };
		panel.Children.Add(_textBox);

		var buttonPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};

		var cancelBtn = new Button
		{
			Content = "取消",
			Padding = new Thickness(16, 6, 16, 6),
			MinWidth = 70,
			Margin = new Thickness(0, 0, 8, 0),
			Cursor = Cursors.Hand,
			Background = TryFindResource("ControlBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(68, 68, 68)),
			Foreground = TryFindResource("PrimaryText") as Brush ?? new SolidColorBrush(Colors.White),
			BorderBrush = TryFindResource("ControlBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(85, 85, 85)),
			BorderThickness = new Thickness(1)
		};
		cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
		buttonPanel.Children.Add(cancelBtn);

		var okBtn = new Button
		{
			Content = "确定",
			Padding = new Thickness(16, 6, 16, 6),
			MinWidth = 70,
			IsDefault = true,
			Cursor = Cursors.Hand,
			Background = TryFindResource("Accent") as Brush ?? new SolidColorBrush(Colors.DodgerBlue),
			Foreground = TryFindResource("AccentForeground") as Brush ?? new SolidColorBrush(Colors.White),
			BorderThickness = new Thickness(0)
		};
		okBtn.Click += (_, _) => Ok();
		buttonPanel.Children.Add(okBtn);

		panel.Children.Add(buttonPanel);
		Content = panel;
		Loaded += (_, _) => _textBox.Focus();
	}

	private void Ok()
	{
		if (!string.IsNullOrWhiteSpace(_textBox.Text))
		{
			DialogResult = true;
			Close();
		}
	}
}

// ═══════════════════════════════════════════════════════════════════
//  Value converters for skill toggle UI
// ═══════════════════════════════════════════════════════════════════

public sealed class BoolToToggleBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
	{
		return value is bool b && b
			? Application.Current.TryFindResource("Accent") ?? Brushes.DodgerBlue
			: Application.Current.TryFindResource("ControlBorder") ?? Brushes.Gray;
	}

	public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		=> throw new NotImplementedException();
}

public sealed class BoolToToggleAlignmentConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		=> value is bool b && b ? HorizontalAlignment.Right : HorizontalAlignment.Left;

	public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		=> throw new NotImplementedException();
}
