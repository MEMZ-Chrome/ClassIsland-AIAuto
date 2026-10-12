using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private readonly IUriNavigationService? _uriService;
    private readonly JsonArray _conversationMessages = new();
    private readonly ObservableCollection<ChatMessageItem> _displayMessages = new();
    private PluginSettings _settings = new();
    private bool _isUpdatingModelCombo = false;
    private bool _isSending = false;

    public ChatWindow(IProfileService? profileService = null, IUriNavigationService? uriService = null)
    {
        InitializeComponent();
        _profileService = profileService;
        _uriService = uriService;

        if (MessagesItemsControl != null)
        {
            MessagesItemsControl.ItemsSource = _displayMessages;
        }

        if (SelectImageButton != null) SelectImageButton.Click += OnSelectImageClicked;
        if (ClearImageButton != null) ClearImageButton.Click += OnClearImageClicked;
        if (SendButton != null) SendButton.Click += (s, e) => SendCurrentMessage();

        // 回车发送，Shift+Enter 换行。
        // 使用 AddHandler 并开启 RoutingStrategies.Tunnel，保证在 TextBox 自身插入换行之前
        // 就能拦截；同时兼容中文输入法：输入法候选框弹出时 e.Key 为 Key.ImeProcessed，
        // 此时应放行给输入法（用 Enter 上屏候选词），不触发发送。
        if (InputTextBox != null)
        {
            InputTextBox.AddHandler(
                KeyDownEvent,
                (s, e) =>
                {
                    if (e.Key != Key.Enter) return;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

                    e.Handled = true;
                    SendCurrentMessage();
                },
                RoutingStrategies.Tunnel);
        }

        _settings = LoadPluginSettings();
        InitModelSelector();
        InitConversation();

        // 设置页或 AI 工具改了供应商/模型后，下拉框要跟着刷新，
        // 否则会继续持有已被删除的 ActiveModelOption，发送时又把它写回配置。
        SettingsManager.SettingsChanged += OnSettingsChangedExternally;
        Closed += (_, _) => SettingsManager.SettingsChanged -= OnSettingsChangedExternally;
    }

    private void OnSettingsChangedExternally(PluginSettings updated)
    {
        if (_isUpdatingModelCombo) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isUpdatingModelCombo) return;
            var previous = (ModelSelectComboBox?.SelectedItem as ActiveModelOption)?.DisplayText;
            InitModelSelector();

            // 尽量恢复用户原先选中的模型
            if (previous != null && ModelSelectComboBox != null)
            {
                var match = ModelSelectComboBox.ItemsSource?
                    .OfType<ActiveModelOption>()
                    .FirstOrDefault(o => o.DisplayText == previous);
                if (match != null) ModelSelectComboBox.SelectedItem = match;
            }
        });
    }

    private void InitModelSelector()
    {
        if (ModelSelectComboBox == null) return;
        _isUpdatingModelCombo = true;
        try
        {
            _settings.EnsureProvidersInitialized();
            var options = _settings.GetEnabledModelOptions();
            ModelSelectComboBox.ItemsSource = options;

            if (options.Count > 0)
            {
                var matched = options.FirstOrDefault(o =>
                    o.ProviderId == _settings.ActiveProviderId && o.ModelName == _settings.ActiveModelName);
                ModelSelectComboBox.SelectedItem = matched ?? options[0];
                if (matched == null)
                {
                    _settings.SelectActiveModel(options[0].ProviderId, options[0].ModelName);
                }
            }
            else
            {
                ModelSelectComboBox.ItemsSource = new List<string> { "(暂无已勾选模型)" };
                ModelSelectComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingModelCombo = false;
        }

        ModelSelectComboBox.SelectionChanged += (s, e) =>
        {
            if (_isUpdatingModelCombo) return;
            if (ModelSelectComboBox.SelectedItem is ActiveModelOption selected)
            {
                _settings.SelectActiveModel(selected.ProviderId, selected.ModelName);
                var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
                var settingsPath = Path.Combine(configDir, "settings.json");
                SaveSettingsToFile(_settings, settingsPath);
            }
        };
    }

    private PluginSettings LoadPluginSettings()
    {
        // 统一走 SettingsManager，保证对话窗口与设置页操作的是同一份配置对象。
        var settings = SettingsManager.Current;
        settings.EnsureProvidersInitialized();
        return settings;
    }

    private void SaveSettingsToFile(PluginSettings settings, string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private static string BuildSystemPrompt(PluginSettings settings)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是一个专门为 ClassIsland 课表信息显示软件服务的智能助手 IslandAgent。");
        sb.AppendLine("你可以根据用户的需求、上传的课表图片或指令，通过调用提供的工具来灵活管理科目 (upsert_subjects)、时间表 (create_time_layout)、" +
                      "每日课表 (set_class_plan)、查询课表详情 (get_schedule_details)、临时调课 (setup_temp_class_plan)、获取系统时间 (get_current_time)、调整设置 (update_app_settings) 以及记录更新长期记忆 (update_memory)。");
        if (settings.IsCommandExecutionEnabled)
        {
            sb.AppendLine("你已被授权在必要时调用 execute_command 在系统终端执行命令行指令。");
        }
        sb.AppendLine();
        sb.AppendLine("【重要执行原则】：");
        sb.AppendLine("1. 你具备全自主连续工具调用能力（ReAct），在必要时可自主连续调用工具（例如：先获取当前时间或课表详情，再执行调课），无需让用户确认继续。");
        sb.AppendLine("2. 当用户涉及“今天”、“明天”、“周几”等相对时间概念时，请调用 get_current_time 工具获取当前精确基准时间与星期几，严禁盲目猜测日期。");
        sb.AppendLine("3. 当用户询问课表内容或调课前需要确认现有课程时，请主动调用 get_profile_summary 或 get_schedule_details 查看确切课程安排。");
        if (settings.IsMemoryEnabled)
        {
            sb.AppendLine("4. 【长期记忆与自动更新】：当用户在对话中透露或你分析得出班级信息、作息习惯、科目/老师偏好、调课规律等具有持久价值的信息时，请自主调用 update_memory 工具自动记录并更新长期记忆库（无需用户每次明确提醒“请记住”）；用户明确吩咐“记住...”或要求修改记忆时也必须调用。在回答关于用户偏好、班级、老师或之前记录过的内容时，必须严格查阅并遵循【AI 长期记忆库 / 偏好事实】中的记录！");
        }
        sb.AppendLine("5. 操作完成后，请用清晰明了的中文 Markdown 富文本给出回复。");

        if (!string.IsNullOrWhiteSpace(settings.CustomPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("【用户自定义要求】：");
            sb.AppendLine(settings.CustomPrompt.Trim());
        }

        if (settings.IsMemoryEnabled && !string.IsNullOrWhiteSpace(settings.CustomMemory))
        {
            sb.AppendLine();
            sb.AppendLine("【AI 长期记忆库 / 偏好事实】：");
            sb.AppendLine(settings.CustomMemory.Trim());
        }

        return sb.ToString();
    }

    private void InitConversation()
    {
        _conversationMessages.Clear();
        _displayMessages.Clear();

        var settings = LoadPluginSettings();
        _conversationMessages.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = BuildSystemPrompt(settings)
        });

        var now = DateTime.Now;
        var todayStr = ToolExecutor.GetChineseWeekDay(now.DayOfWeek);

        _displayMessages.Add(new ChatMessageItem
        {
            Role = ChatRole.Assistant,
            TimeString = now.ToString("HH:mm"),
            Content = "你好，我是 ClassIsland AI 助手。"
        });

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ChatScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
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

            // 上限 8 MB。图片会被 Base64 后随每次请求发送（体积再涨约 1/3），
            // 不设限的话一张高分辨率图就可能撑爆请求体或直接 OOM。
            const long maxBytes = 8 * 1024 * 1024;
            if (stream.CanSeek && stream.Length > maxBytes)
            {
                _selectedImageBytes = null;
                if (ImageStatusTextBlock != null)
                {
                    ImageStatusTextBlock.Text = $"图片过大（{stream.Length / 1024 / 1024} MB），请压缩到 8 MB 以内";
                }
                if (ClearImageButton != null) ClearImageButton.IsVisible = false;
                ShowToast("图片过大，未加载", isError: true);
                return;
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            if (ms.Length > maxBytes)
            {
                _selectedImageBytes = null;
                if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = "图片过大，请压缩到 8 MB 以内";
                if (ClearImageButton != null) ClearImageButton.IsVisible = false;
                ShowToast("图片过大，未加载", isError: true);
                return;
            }

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
        }
    }

    private void OnClearImageClicked(object? sender, RoutedEventArgs? e)
    {
        _selectedImageBytes = null;
        if (ImageStatusTextBlock != null) ImageStatusTextBlock.Text = "未选择图片";
        if (ClearImageButton != null) ClearImageButton.IsVisible = false;
    }

    private async void SendCurrentMessage()
    {
        var prompt = InputTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(prompt) && (_selectedImageBytes == null || _selectedImageBytes.Length == 0))
        {
            return;
        }

        // 防重入：Enter 处理器不受 SendButton.IsEnabled 约束，连按回车会并发发起多次请求，
        // 同时读写 _conversationMessages / _displayMessages 与共享配置。
        if (_isSending)
        {
            return;
        }
        _isSending = true;
        try
        {
            await SendCurrentMessageCoreAsync(prompt);
        }
        finally
        {
            _isSending = false;
        }
    }

    private async Task SendCurrentMessageCoreAsync(string prompt)
    {
        // 使用 SettingsManager 的共享实例，而不是每次都从磁盘重新反序列化。
        // 重新 Load 会替换静态实例，导致设置页正在编辑的对象与对话窗口持有的对象
        // 变成两份，设置页的修改会在下次发送时被静默丢弃。
        var settings = SettingsManager.Current;
        if (ModelSelectComboBox?.SelectedItem is ActiveModelOption opt)
        {
            settings.SelectActiveModel(opt.ProviderId, opt.ModelName);
        }

        if (string.IsNullOrWhiteSpace(settings.CurrentModel))
        {
            _displayMessages.Add(new ChatMessageItem
            {
                Role = ChatRole.System,
                TimeString = DateTime.Now.ToString("HH:mm"),
                Content = "未配置可用模型！请先在【设置 -> IslandAgent设置 -> 模型服务】中配置供应商并勾选所需模型。"
            });
            ScrollToBottom();
            return;
        }

        var now = DateTime.Now;

        // 构造用户消息
        var textContent = string.IsNullOrWhiteSpace(prompt) ? "请分析这张课表图片，并调用相应工具完成课表配置。" : prompt;
        var userMsgContent = new JsonArray();
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

        // 每次对话轮次都刷新最新的系统提示词（确保长期记忆库更新后下一轮直接生效）
        var systemPromptContent = BuildSystemPrompt(settings);
        if (_conversationMessages.Count > 0 && _conversationMessages[0]?["role"]?.GetValue<string>() == "system")
        {
            _conversationMessages[0] = new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPromptContent
            };
        }
        else
        {
            _conversationMessages.Insert(0, new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPromptContent
            });
        }

        var userMessage = new JsonObject
        {
            ["role"] = "user",
            ["content"] = userMsgContent
        };
        _conversationMessages.Add(userMessage);
        var userMessageIndex = _conversationMessages.Count - 1;
        var hadImage = _selectedImageBytes != null && _selectedImageBytes.Length > 0;

        // 添加到对话气泡流
        _displayMessages.Add(new ChatMessageItem
        {
            Role = ChatRole.User,
            TimeString = now.ToString("HH:mm"),
            Content = textContent
        });

        if (InputTextBox != null) InputTextBox.Text = "";
        OnClearImageClicked(null, null);

        var assistantMsg = new ChatMessageItem
        {
            Role = ChatRole.Assistant,
            TimeString = now.ToString("HH:mm"),
            Content = "正在思考中...",
            IsThinking = true
        };
        _displayMessages.Add(assistantMsg);
        ScrollToBottom();

        if (SendButton != null) SendButton.IsEnabled = false;

        try
        {
            var executor = new ToolExecutor(_profileService, _uriService, settings, () =>
            {
                SettingsManager.Save(settings);
                if (_conversationMessages.Count > 0 && _conversationMessages[0]?["role"]?.GetValue<string>() == "system")
                {
                    _conversationMessages[0]["content"] = BuildSystemPrompt(settings);
                }
            })
            {
                // 聊天窗口不提供解锁入口，因此只有「未设置任何访问保护」时才视为已授权。
                // 设置了密码/TOTP 的用户必须去设置页改高危项，AI 不能替他开。
                IsAuthorizedSession = !SecurityService.IsActiveProtectionEnabled(settings)
            };
            var response = await _aiClient.ExecuteAgentTurnAsync(
                settings,
                _conversationMessages,
                executor,
                onProgress: (statusText, execResult) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        assistantMsg.AddToolBadge(statusText);
                        ScrollToBottom();
                    });
                });

            assistantMsg.IsThinking = false;
            assistantMsg.Content = string.IsNullOrWhiteSpace(response.ReplyText) ? "操作已执行完毕。" : response.ReplyText;
            ScrollToBottom();
        }
        catch (Exception ex)
        {
            assistantMsg.IsThinking = false;
            assistantMsg.Content = $"请求处理失败: {ex.Message}";
            ScrollToBottom();
        }
        finally
        {
            // 请求已发出，现在把历史里的 base64 图片换成文字占位。
            // 否则这张图会一直留在 _conversationMessages 中，之后每一次 ReAct 请求
            // 以及之后所有轮次都会 DeepClone 并重新上传，持续放大流量与 token 费用。
            if (hadImage && userMessageIndex >= 0 && userMessageIndex < _conversationMessages.Count)
            {
                _conversationMessages[userMessageIndex] = new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = $"{textContent}（用户上传了一张课表图片）"
                };
            }

            if (SendButton != null) SendButton.IsEnabled = true;
        }
    }
}
