using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Automation.V2.Views.SettingsPages;

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

        if (ApiKeyTextBox != null)
        {
            ApiKeyTextBox.LostFocus += (_, _) => AutoSave();
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
            if (BaseUrlTextBox == null || ApiKeyTextBox == null || ModelComboBox == null) return;

            BaseUrlTextBox.Text = _settings.CurrentBaseUrl;
            ApiKeyTextBox.Text = _settings.CurrentApiKey;

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
        // 兼容获取 ComboBox 选中的对象或手动输入的文本
        var model = (ModelComboBox?.SelectedItem?.ToString() ?? ModelComboBox?.Text ?? "").Trim();

        switch (_settings.Provider)
        {
            case AiProviderType.MiMo:
                _settings.MiMoBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.MiMoApiKey = ApiKeyTextBox?.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.MiMoModel = model;
                break;
            case AiProviderType.DeepSeek:
                _settings.DeepSeekBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.DeepSeekApiKey = ApiKeyTextBox?.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.DeepSeekModel = model;
                break;
            default:
                _settings.CustomBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.CustomApiKey = ApiKeyTextBox?.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.CustomModel = model;
                break;
        }
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
                        // 如果之前选中的模型在列表中，继续选中它；否则默认选第一个
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
