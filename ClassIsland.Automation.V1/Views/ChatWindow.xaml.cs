using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Win32;

namespace ClassIsland.Automation.V1.Views;

public partial class ChatWindow : Window
{
    private byte[]? _selectedImageBytes;
    private string _imageMimeType = "image/png";
    private readonly AiClientService _aiClient = new();
    private readonly IProfileService? _profileService;
    private readonly IUriNavigationService? _uriService;
    private readonly JsonArray _conversationMessages = new();

    public ChatWindow(IProfileService? profileService = null, IUriNavigationService? uriService = null)
    {
        InitializeComponent();
        _profileService = profileService;
        _uriService = uriService;

        if (SelectImageButton != null) SelectImageButton.Click += OnSelectImageClicked;
        if (ClearImageButton != null) ClearImageButton.Click += OnClearImageClicked;
        if (SendButton != null) SendButton.Click += OnSendClicked;

        InitConversation();
        AppendLog("AI 助手已就绪（支持灵活 Tools Call 工具调用）。你可以上传课表图片让 AI 识别并调用对应工具写入，或直接用自然语言要求调整课表、调课及打开相关设置。");
    }

    private void InitConversation()
    {
        _conversationMessages.Clear();
        _conversationMessages.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = "你是一个专门为 ClassIsland 课表信息显示软件服务的智能助手。" +
                          "你可以根据用户的需求、上传的课表图片或指令，通过调用提供的工具来灵活管理科目 (upsert_subjects)、时间表 (create_time_layout)、" +
                          "每日课表 (set_class_plan)、临时调课 (setup_temp_class_plan) 以及导航设置页面 (navigate_app_page)。" +
                          "请在需要执行具体操作时主动使用对应工具，并在最后给出简明扼要的回复说明。"
        });
    }

    private void AppendLog(string message)
    {
        if (ChatOutputTextBox != null)
        {
            ChatOutputTextBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n\n";
            LogScrollViewer?.ScrollToEnd();
        }
    }

    private void OnSelectImageClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择课表图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            _selectedImageBytes = File.ReadAllBytes(filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            _imageMimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            var fileName = Path.GetFileName(filePath);
            if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = $"已选择: {fileName}";
            if (ClearImageButton != null) ClearImageButton.Visibility = Visibility.Visible;
            AppendLog($"已加载图片文件: {fileName} ({_selectedImageBytes.Length / 1024} KB)");
        }
    }

    private void OnClearImageClicked(object? sender, RoutedEventArgs e)
    {
        _selectedImageBytes = null;
        if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = "未选择图片";
        if (ClearImageButton != null) ClearImageButton.Visibility = Visibility.Collapsed;
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

        // 构造用户消息
        var userMsgContent = new JsonArray();
        var textContent = string.IsNullOrWhiteSpace(prompt) ? "请分析这张课表图片，并调用相应工具完成课表配置。" : prompt;
        userMsgContent.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = textContent
        });

        if (_selectedImageBytes != null && _selectedImageBytes.Length > 0)
        {
            var base64 = Convert.ToBase64String(_selectedImageBytes);
            userMsgContent.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{_imageMimeType};base64,{base64}"
                }
            });
        }

        _conversationMessages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = userMsgContent
        });

        AppendLog($"[用户] {textContent}");
        AppendLog($"正在请求 AI 模型 ({settings.CurrentModel})，等待工具调用与处理...");
        if (SendButton != null) SendButton.IsEnabled = false;

        try
        {
            var executor = new ToolExecutor(_profileService, _uriService);
            var response = await _aiClient.ExecuteAgentTurnAsync(
                settings,
                _conversationMessages,
                executor);

            foreach (var exec in response.ExecutedResults)
            {
                var status = exec.Success ? "成功" : "失败";
                AppendLog($"[工具调用: {exec.Name}] 状态: {status}\n执行结果: {exec.Result}");
            }

            if (!string.IsNullOrWhiteSpace(response.ReplyText))
            {
                AppendLog($"[AI] {response.ReplyText}");
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
