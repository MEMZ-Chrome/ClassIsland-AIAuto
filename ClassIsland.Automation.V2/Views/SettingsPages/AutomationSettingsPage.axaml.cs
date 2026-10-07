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

    public AutomationSettingsPage()
    {
        InitializeComponent();

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "settings.json");
        _settings = LoadSettings();

        InitControls();
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
            if (StatusTextBlock != null) StatusTextBlock.Text = "设置已保存！";
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"保存失败: {ex.Message}";
        }
    }

    private void InitControls()
    {
        if (ProviderComboBox != null)
        {
            ProviderComboBox.SelectedIndex = (int)_settings.Provider;
            ProviderComboBox.SelectionChanged += OnProviderChanged;
        }

        UpdateInputsForProvider();

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
        SyncFromInputs();
        _settings.Provider = (AiProviderType)(ProviderComboBox?.SelectedIndex ?? 0);
        UpdateInputsForProvider();
    }

    private void UpdateInputsForProvider()
    {
        if (BaseUrlTextBox == null || ApiKeyTextBox == null || ModelComboBox == null) return;

        BaseUrlTextBox.Text = _settings.CurrentBaseUrl;
        ApiKeyTextBox.Text = _settings.CurrentApiKey;
        ModelComboBox.SelectedItem = null;
        ModelComboBox.Text = _settings.CurrentModel;
    }

    private void SyncFromInputs()
    {
        var model = ModelComboBox?.Text ?? "";
        switch (_settings.Provider)
        {
            case AiProviderType.MiMo:
                _settings.MiMoBaseUrl = BaseUrlTextBox?.Text ?? "";
                _settings.MiMoApiKey = ApiKeyTextBox?.Text ?? "";
                _settings.MiMoModel = model;
                break;
            case AiProviderType.DeepSeek:
                _settings.DeepSeekBaseUrl = BaseUrlTextBox?.Text ?? "";
                _settings.DeepSeekApiKey = ApiKeyTextBox?.Text ?? "";
                _settings.DeepSeekModel = model;
                break;
            default:
                _settings.CustomBaseUrl = BaseUrlTextBox?.Text ?? "";
                _settings.CustomApiKey = ApiKeyTextBox?.Text ?? "";
                _settings.CustomModel = model;
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
                ModelComboBox.ItemsSource = models;
                if (models.Count > 0)
                {
                    ModelComboBox.SelectedIndex = 0;
                }
            }
            if (StatusTextBlock != null) StatusTextBlock.Text = $"成功获取 {models.Count} 个模型！";
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"获取失败: {ex.Message}";
        }
    }
}
