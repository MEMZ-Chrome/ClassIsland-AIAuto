using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Automation.V1.Views.SettingsPages;

[SettingsPageInfo("memz.ci.automation.settings", "CI自动化", SettingsPageCategory.External)]
public partial class AutomationSettingsPage : SettingsPageBase
{
    private readonly PluginSettings _settings;
    private readonly string _settingsFilePath;
    private readonly AiClientService _aiClient = new();
    private bool _isUpdatingUi = false;

    public AutomationSettingsPage()
    {
        InitializeComponent();

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "settings.json");
        _settings = LoadSettings();

        InitControls();
        Unloaded += (_, _) => AutoSave();
    }

    private PluginSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
            }
        }
        catch { }
        return new PluginSettings();
    }

    private void SaveSettingsToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
            if (StatusTextBlock != null) StatusTextBlock.Text = "设置已自动保存！";
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"保存失败: {ex.Message}";
        }
    }

    private void AutoSave()
    {
        if (_isUpdatingUi) return;
        SyncFromInputs();
        SaveSettingsToFile();
    }

    private void InitControls()
    {
        if (ProviderComboBox != null)
        {
            ProviderComboBox.SelectedIndex = (int)_settings.Provider;
            ProviderComboBox.SelectionChanged += OnProviderChanged;
        }

        UpdateInputsForProvider();

        // 监听输入控件改变自动保存
        if (BaseUrlTextBox != null)
        {
            BaseUrlTextBox.LostFocus += (_, _) => AutoSave();
        }

        if (ApiKeyPasswordBox != null)
        {
            ApiKeyPasswordBox.PasswordChanged += (_, _) => AutoSave();
        }

        if (ModelComboBox != null)
        {
            ModelComboBox.SelectionChanged += (_, _) =>
            {
                if (!_isUpdatingUi) AutoSave();
            };
            ModelComboBox.LostFocus += (_, _) => AutoSave();
        }

        if (FetchModelsButton != null)
        {
            FetchModelsButton.Click += OnFetchModelsClicked;
        }

        if (SaveButton != null)
        {
            SaveButton.Click += (_, _) =>
            {
                SyncFromInputs();
                SaveSettingsToFile();
            };
        }

        // 高级设置与记忆系统控件
        if (IsMemoryEnabledCheckBox != null)
        {
            IsMemoryEnabledCheckBox.IsChecked = _settings.IsMemoryEnabled;
            IsMemoryEnabledCheckBox.Checked += (_, _) => AutoSave();
            IsMemoryEnabledCheckBox.Unchecked += (_, _) => AutoSave();
        }

        if (IsCommandExecutionEnabledCheckBox != null)
        {
            IsCommandExecutionEnabledCheckBox.IsChecked = _settings.IsCommandExecutionEnabled;
            IsCommandExecutionEnabledCheckBox.Checked += (_, _) => AutoSave();
            IsCommandExecutionEnabledCheckBox.Unchecked += (_, _) => AutoSave();
        }

        if (CustomPromptTextBox != null)
        {
            CustomPromptTextBox.Text = _settings.CustomPrompt;
            CustomPromptTextBox.LostFocus += (_, _) => AutoSave();
        }

        if (CustomMemoryTextBox != null)
        {
            CustomMemoryTextBox.Text = _settings.CustomMemory;
            CustomMemoryTextBox.LostFocus += (_, _) => AutoSave();
        }

        if (ClearMemoryButton != null)
        {
            ClearMemoryButton.Click += (_, _) =>
            {
                if (CustomMemoryTextBox != null) CustomMemoryTextBox.Text = "";
                _settings.CustomMemory = "";
                AutoSave();
                if (StatusTextBlock != null) StatusTextBlock.Text = "长期记忆库已清空并保存！";
            };
        }
    }

    private void OnProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        SyncFromInputs();
        _settings.Provider = (AiProviderType)(ProviderComboBox?.SelectedIndex ?? 0);
        UpdateInputsForProvider();
        SaveSettingsToFile();
    }

    private void UpdateInputsForProvider()
    {
        _isUpdatingUi = true;
        try
        {
            if (BaseUrlTextBox == null || ApiKeyPasswordBox == null || ModelComboBox == null) return;

            BaseUrlTextBox.Text = _settings.CurrentBaseUrl;
            ApiKeyPasswordBox.Password = _settings.CurrentApiKey;

            var currentModel = _settings.CurrentModel;
            ModelComboBox.SelectedItem = currentModel;
            ModelComboBox.Text = currentModel;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void SyncFromInputs()
    {
        var model = (ModelComboBox?.SelectedItem?.ToString() ?? ModelComboBox?.Text ?? "").Trim();

        switch (_settings.Provider)
        {
            case AiProviderType.MiMo:
                _settings.MiMoBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.MiMoApiKey = ApiKeyPasswordBox?.Password ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.MiMoModel = model;
                break;
            case AiProviderType.DeepSeek:
                _settings.DeepSeekBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.DeepSeekApiKey = ApiKeyPasswordBox?.Password ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.DeepSeekModel = model;
                break;
            default:
                _settings.CustomBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.CustomApiKey = ApiKeyPasswordBox?.Password ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.CustomModel = model;
                break;
        }

        // 同步高级设置与记忆库
        _settings.IsMemoryEnabled = IsMemoryEnabledCheckBox?.IsChecked ?? true;
        _settings.IsCommandExecutionEnabled = IsCommandExecutionEnabledCheckBox?.IsChecked ?? false;
        _settings.CustomPrompt = CustomPromptTextBox?.Text?.Trim() ?? "";
        _settings.CustomMemory = CustomMemoryTextBox?.Text?.Trim() ?? "";
    }

    private async void OnFetchModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (StatusTextBlock != null) StatusTextBlock.Text = "正在获取模型列表...";
        SyncFromInputs();

        try
        {
            var models = await _aiClient.FetchModelsAsync(_settings.CurrentBaseUrl, _settings.CurrentApiKey);
            if (ModelComboBox != null)
            {
                _isUpdatingUi = true;
                try
                {
                    ModelComboBox.ItemsSource = models;
                    if (models.Count > 0)
                    {
                        var existingIndex = models.IndexOf(_settings.CurrentModel);
                        ModelComboBox.SelectedIndex = existingIndex >= 0 ? existingIndex : 0;
                    }
                }
                finally
                {
                    _isUpdatingUi = false;
                }
                AutoSave();
            }
            if (StatusTextBlock != null) StatusTextBlock.Text = $"成功获取 {models.Count} 个模型并已自动保存！";
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"获取失败: {ex.Message}";
        }
    }
}
