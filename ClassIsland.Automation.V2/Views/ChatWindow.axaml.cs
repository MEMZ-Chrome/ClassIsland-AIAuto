using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.Automation.V2.Views;

public partial class ChatWindow : Window
{
    private byte[]? _selectedImageBytes;
    private string _imageMimeType = "image/png";
    private readonly AiClientService _aiClient = new();
    private readonly IProfileService? _profileService;

    public ChatWindow(IProfileService? profileService = null)
    {
        InitializeComponent();
        _profileService = profileService;

        if (SelectImageButton != null) SelectImageButton.Click += OnSelectImageClicked;
        if (ClearImageButton != null) ClearImageButton.Click += OnClearImageClicked;
        if (SendButton != null) SendButton.Click += OnSendClicked;

        AppendLog("AI 助手已就绪。你可以上传课表图片并点击发送，助手将自动提取科目、作息并配置到当前课表中；亦可直接输入设置修改指令。");
    }

    private void AppendLog(string message)
    {
        if (ChatOutputTextBox != null)
        {
            ChatOutputTextBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n\n";
            LogScrollViewer?.ScrollToEnd();
        }
    }

    private async void OnSelectImageClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择课表图片",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                FilePickerFileTypes.ImageAll
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _selectedImageBytes = ms.ToArray();

            var ext = Path.GetExtension(file.Name).ToLowerInvariant();
            _imageMimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = $"已选择: {file.Name}";
            if (ClearImageButton != null) ClearImageButton.IsVisible = true;
            AppendLog($"已加载图片文件: {file.Name} ({_selectedImageBytes.Length / 1024} KB)");
        }
    }

    private void OnClearImageClicked(object? sender, RoutedEventArgs e)
    {
        _selectedImageBytes = null;
        if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = "未选择图片";
        if (ClearImageButton != null) ClearImageButton.IsVisible = false;
    }

    private async void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        var prompt = InputTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(prompt) && (_selectedImageBytes == null || _selectedImageBytes.Length == 0))
        {
            AppendLog("请输入指令或选择图片后再发送。");
            return;
        }

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        var settingsPath = Path.Combine(configDir, "settings.json");
        var settings = new PluginSettings();
        if (File.Exists(settingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(settingsPath)) ?? new PluginSettings();
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(settings.CurrentModel))
        {
            AppendLog("未配置 AI 模型！请先在【设置 -> CI自动化】中选择提供商并设置模型。");
            return;
        }

        AppendLog($"正在请求 AI 模型 ({settings.CurrentModel})...");
        if (SendButton != null) SendButton.IsEnabled = false;

        try
        {
            var result = await _aiClient.ProcessScheduleOrCommandAsync(
                settings,
                prompt,
                _selectedImageBytes,
                _imageMimeType);

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                AppendLog(result.Message);
            }

            // 应用课表与时间表到 ClassIsland Profile
            if (_profileService != null && (result.Subjects.Count > 0 || result.ClassPlans.Count > 0))
            {
                ScheduleApplierService.ApplyToProfile(_profileService, result);
                AppendLog("已成功应用课表与时间表配置到 ClassIsland 档案中！");
            }

            // 处理应用设置项
            if (result.AppSettings != null && result.AppSettings.Count > 0)
            {
                AppendLog($"检测到设置项修改指令: {JsonSerializer.Serialize(result.AppSettings)}");
            }

            if (InputTextBox != null) InputTextBox.Text = "";
            OnClearImageClicked(null, null);
        }
        catch (Exception ex)
        {
            AppendLog($"发生错误: {ex.Message}");
        }
        finally
        {
            if (SendButton != null) SendButton.IsEnabled = true;
        }
    }
}
