using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Automation.V2.Views.SettingsPages;

[SettingsPageInfo("memzchrome.islandagent.settings", "IslandAgent璁剧疆", SettingsPageCategory.External)]
public partial class AutomationSettingsPage : SettingsPageBase
{
    private PluginSettings _settings;
    private readonly string _settingsFilePath;
    private readonly AiClientService _aiClient = new();
    private bool _isUpdatingUi = false;
    private bool _isUnlocked = false;

    // 妯℃€佸脊绐楃紪杈戠姸鎬?    private AiProviderConfig? _dialogEditingProvider = null;
    private List<AiModelItem> _dialogModels = new();

    // 娴姩 Toast 瀹氭椂鍣?    private DispatcherTimer? _toastTimer;

    public AutomationSettingsPage()
    {
        InitializeComponent();

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "settings.json");
        // 澶嶇敤鍏变韩瀹炰緥鑰屼笉鏄噸鏂颁粠纾佺洏鍔犺浇锛岄伩鍏嶄笌瀵硅瘽绐楀彛鎸佹湁鐨勯厤缃璞″垎鍙夈€?        SettingsManager.SetSettingsFilePath(_settingsFilePath);
        _settings = SettingsManager.Current;
        _settings.EnsureProvidersInitialized();

        InitSecurityState();
        InitControls();
        InitSecurityControls();

        SettingsManager.SettingsChanged += OnExternalSettingsChanged;
        Unloaded += (_, _) =>
        {
            SettingsManager.SettingsChanged -= OnExternalSettingsChanged;
            AutoSave();
        };
    }

    public void ShowToast(string message, bool isError = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ToastBorder == null || ToastMessageTextBlock == null) return;
            ToastMessageTextBlock.Text = message;
            ToastBorder.IsVisible = true;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                if (ToastBorder != null) ToastBorder.IsVisible = false;
            };
            _toastTimer.Start();
        });
    }

    private void OnExternalSettingsChanged(PluginSettings updated)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isUpdatingUi) return;
            _isUpdatingUi = true;
            try
            {
                _settings.CustomMemory = updated.CustomMemory;
                if (CustomMemoryTextBox != null)
                {
                    CustomMemoryTextBox.Text = updated.CustomMemory;
                }
            }
            finally
            {
                _isUpdatingUi = false;
            }
        });
    }

    private void InitSecurityState()
    {
        // 鍏抽敭瀹夊叏璁捐锛氬緟楠岃瘉鐨勬殏瀛?TOTP 缁濅笉鑳戒娇椤甸潰杩涘叆閿佸畾鐘舵€侊紝闃叉鐢ㄦ埛鍥犳墜鐜湭鍚屾鑰屽崱姝伙紒
        if (SecurityService.IsActiveProtectionEnabled(_settings) && !_isUnlocked)
        {
            if (LockOverlayBorder != null) LockOverlayBorder.IsVisible = true;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.IsVisible = false;
        }
        else
        {
            _isUnlocked = true;
            if (LockOverlayBorder != null) LockOverlayBorder.IsVisible = false;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.IsVisible = true;
        }

        if (UnlockButton != null)
        {
            UnlockButton.Click += (_, _) => AttemptUnlock();
        }

        if (UnlockPasswordBox != null)
        {
            UnlockPasswordBox.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    AttemptUnlock();
                }
            };
        }
    }

    private void AttemptUnlock()
    {
        var input = UnlockPasswordBox?.Text?.Trim() ?? "";
        var result = SecurityService.VerifyAccess(_settings, input);

        if (result != AccessAuthResult.Failed)
        {
            _isUnlocked = true;
            if (UnlockErrorTextBlock != null) UnlockErrorTextBlock.IsVisible = false;
            if (LockOverlayBorder != null) LockOverlayBorder.IsVisible = false;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.IsVisible = true;
            if (UnlockPasswordBox != null) UnlockPasswordBox.Text = "";
            ShowToast("瀹夊叏楠岃瘉閫氳繃锛屽凡瑙ｉ攣璁剧疆椤甸潰锛?);
        }
        else
        {
            if (UnlockErrorTextBlock != null)
            {
                UnlockErrorTextBlock.Text = "瀵嗙爜鎴?TOTP 鍔ㄦ€侀獙璇佺爜閿欒锛岃閲嶆柊杈撳叆锛?;
                UnlockErrorTextBlock.IsVisible = true;
            }
            ShowToast("楠岃瘉澶辫触锛岃閲嶈瘯锛?, isError: true);
        }
    }

    private void SaveSettingsToFile()
    {
        try
        {
            SettingsManager.Save(_settings);
            if (StatusTextBlock != null) StatusTextBlock.Text = "璁剧疆宸茶嚜鍔ㄤ繚瀛橈紒";
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"淇濆瓨澶辫触: {ex.Message}";
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
        InitPresetButtons();
        RenderProvidersList();

        if (AddProviderButton != null)
        {
            AddProviderButton.Click += (_, _) => OpenAddProviderDialog();
        }

        if (DialogCloseButton != null)
        {
            DialogCloseButton.Click += (_, _) => CloseProviderDialog();
        }

        if (DialogCancelButton != null)
        {
            DialogCancelButton.Click += (_, _) => CloseProviderDialog();
        }

        if (DialogSaveButton != null)
        {
            DialogSaveButton.Click += (_, _) => SaveProviderDialog();
        }

        if (DialogAddModelButton != null)
        {
            DialogAddModelButton.Click += OnDialogAddModelClicked;
        }

        if (DialogNewModelTextBox != null)
        {
            DialogNewModelTextBox.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    OnDialogAddModelClicked(null, null);
                }
            };
        }

        if (DialogFetchModelsButton != null)
        {
            DialogFetchModelsButton.Click += OnDialogFetchModelsClicked;
        }

        if (SaveButton != null)
        {
            SaveButton.Click += (_, _) =>
            {
                SyncFromInputs();
                SaveSettingsToFile();
                ShowToast("鎵€鏈夎缃凡淇濆瓨鎴愬姛锛?);
            };
        }

        if (IsMemoryEnabledCheckBox != null)
        {
            IsMemoryEnabledCheckBox.IsChecked = _settings.IsMemoryEnabled;
            IsMemoryEnabledCheckBox.IsCheckedChanged += (_, _) => AutoSave();
        }

        if (IsCommandExecutionEnabledCheckBox != null)
        {
            IsCommandExecutionEnabledCheckBox.IsChecked = _settings.IsCommandExecutionEnabled;
            IsCommandExecutionEnabledCheckBox.IsCheckedChanged += (_, _) => AutoSave();
        }

        if (MaxToolIterationsNumeric != null)
        {
            MaxToolIterationsNumeric.Value = _settings.EffectiveMaxToolIterations;
            MaxToolIterationsNumeric.ValueChanged += (_, _) => AutoSave();
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
                ShowToast("闀挎湡璁板繂搴撳凡娓呯┖骞朵繚瀛橈紒");
            };
        }
    }

    private void InitPresetButtons()
    {
        if (PresetButtonsWrapPanel == null) return;
        PresetButtonsWrapPanel.Children.Clear();

        var presets = new (string Name, string Url)[]
        {
            ("DeepSeek", "https://api.deepseek.com"),
            ("OpenRouter", "https://openrouter.ai/api/v1"),
            ("灏忕背 MiMo", "https://api.xiaomimimo.com/v1"),
            ("OpenAI", "https://api.openai.com/v1"),
            ("鑷缓 Ollama", "http://localhost:11434/v1")
        };

        foreach (var p in presets)
        {
            var btn = new Button
            {
                Content = p.Name,
                Padding = new Thickness(8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 11
            };
            btn.Click += (_, _) => ApplyPreset(p.Name, p.Url);
            PresetButtonsWrapPanel.Children.Add(btn);
        }
    }

    private void ApplyPreset(string name, string baseUrl)
    {
        if (DialogNameTextBox != null) DialogNameTextBox.Text = name;
        if (DialogBaseUrlTextBox != null) DialogBaseUrlTextBox.Text = baseUrl;

        ShowToast($"宸插～鍏ャ€寋name}銆嶉缃湴鍧€锛岃濉啓瀵嗛挜鍚庣偣鍑昏嚜鍔ㄨ幏鍙栨ā鍨嬨€?);
    }

    /// <summary>
    /// 娆¤鏂囧瓧鐢诲埛銆傛繁鑹蹭富棰樹笅鍐欐 Gray 浼氫笌鑳屾櫙绯婂湪涓€璧凤紝蹇呴』璺熼殢涓婚銆?    /// </summary>
    private IBrush SubtleBrush
        => this.TryFindResource("TextFillColorSecondaryBrush", out var res) && res is IBrush b
            ? b
            : Brushes.Gray;

    private void RenderProvidersList()
    {
        if (ProvidersListContainer == null) return;
        ProvidersListContainer.Children.Clear();

        if (_settings.Providers.Count == 0)
        {
            var emptyBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24)
            };
            if (this.TryFindResource("CardBackgroundFillColorDefaultBrush", out var bgRes) && bgRes is IBrush bgBrush)
            {
                emptyBorder.Background = bgBrush;
            }
            if (this.TryFindResource("CardStrokeColorDefaultBrush", out var strokeRes) && strokeRes is IBrush strokeBrush)
            {
                emptyBorder.BorderBrush = strokeBrush;
                emptyBorder.BorderThickness = new Thickness(1);
            }

            var emptyText = new TextBlock
            {
                Text = "鏆傛棤渚涘簲鍟?,
                Foreground = SubtleBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            emptyBorder.Child = emptyText;
            ProvidersListContainer.Children.Add(emptyBorder);
            return;
        }

        foreach (var provider in _settings.Providers)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 8)
            };
            if (this.TryFindResource("CardBackgroundFillColorDefaultBrush", out var bgRes) && bgRes is IBrush bgBrush)
            {
                card.Background = bgBrush;
            }
            if (this.TryFindResource("CardStrokeColorDefaultBrush", out var strokeRes) && strokeRes is IBrush strokeBrush)
            {
                card.BorderBrush = strokeBrush;
                card.BorderThickness = new Thickness(1);
            }

            var mainStack = new StackPanel { Spacing = 10 };

            // 椤惰锛氬埆鍚嶄笌鎿嶄綔鎸夐挳
            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto")
            };

            var titleStack = new StackPanel { Spacing = 2 };
            var nameText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(provider.Name) ? "(鏈懡鍚嶄緵搴斿晢)" : provider.Name,
                FontSize = 15,
                FontWeight = FontWeight.Bold
            };
            var urlText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(provider.BaseUrl) ? "(鏈缃?API 鍦板潃)" : provider.BaseUrl,
                FontSize = 11,
                Foreground = SubtleBrush
            };
            titleStack.Children.Add(nameText);
            titleStack.Children.Add(urlText);
            Grid.SetColumn(titleStack, 0);

            var actionsStack = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8
            };
            Grid.SetColumn(actionsStack, 1);

            var editBtn = new Button
            {
                Content = "缂栬緫",
                Padding = new Thickness(10, 4)
            };
            editBtn.Click += (_, _) => OpenEditProviderDialog(provider);
            actionsStack.Children.Add(editBtn);

            var delBtn = new Button
            {
                Content = "鍒犻櫎",
                Padding = new Thickness(10, 4)
            };
            delBtn.Click += (_, _) =>
            {
                if (_settings.Providers.Count <= 1)
                {
                    ShowToast("鑷冲皯闇€淇濈暀涓€涓緵搴斿晢锛?, isError: true);
                    return;
                }
                _settings.Providers.Remove(provider);
                if (_settings.ActiveProviderId == provider.Id)
                {
                    _settings.ActiveProviderId = _settings.Providers.FirstOrDefault()?.Id ?? "";
                }
                SaveSettingsToFile();
                RenderProvidersList();
                ShowToast($"宸插垹闄や緵搴斿晢銆寋provider.Name}銆嶏紒");
            };
            actionsStack.Children.Add(delBtn);

            topGrid.Children.Add(titleStack);
            topGrid.Children.Add(actionsStack);
            mainStack.Children.Add(topGrid);

            // 鍒嗗壊绾?            var divider = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 2)
            };
            if (this.TryFindResource("CardStrokeColorDefaultBrush", out var divStrokeRes) && divStrokeRes is IBrush divStrokeBrush)
            {
                divider.Background = divStrokeBrush;
            }
            mainStack.Children.Add(divider);

            // 妯″瀷鍕鹃€夊尯鍩燂細鎸夊垪鎺掑竷锛屽绐楀彛涓嬭嚜鍔ㄩ摵寮€锛岄伩鍏嶅崱鐗囧彸渚уぇ鐗囩暀鐧?            var modelsStack = new StackPanel { Spacing = 6 };
            var modelsLabel = new TextBlock
            {
                Text = "妯″瀷鍒楄〃",
                FontSize = 11,
                Foreground = SubtleBrush
            };
            modelsStack.Children.Add(modelsLabel);

            if (provider.Models.Count == 0)
            {
                var noModelsText = new TextBlock
                {
                    Text = "(鏆傛棤妯″瀷)",
                    FontSize = 11,
                    Foreground = SubtleBrush,
                    FontStyle = FontStyle.Italic
                };
                modelsStack.Children.Add(noModelsText);
            }
            else
            {
                // 鐢ㄥ浐瀹氬搴︾殑鍗曞厓鏍艰鍕鹃€夐」鎸夊垪瀵归綈锛屽绐楀彛涓嬭嚜鍔ㄩ摵寮€锛?                // 閬垮厤鍗＄墖鍙充晶鍑虹幇澶х墖鐣欑櫧銆?                var wrap = new WrapPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                foreach (var model in provider.Models)
                {
                    var cb = new CheckBox
                    {
                        Content = model.Name,
                        IsChecked = model.IsEnabled,
                        Width = 210,
                        Margin = new Thickness(0, 2, 8, 2)
                    };
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        model.IsEnabled = cb.IsChecked == true;
                        SaveSettingsToFile();
                        ShowToast($"宸瞷(model.IsEnabled ? "鍚敤" : "绂佺敤")}妯″瀷銆寋model.Name}銆?);
                    };
                    wrap.Children.Add(cb);
                }
                modelsStack.Children.Add(wrap);
            }

            mainStack.Children.Add(modelsStack);
            card.Child = mainStack;
            ProvidersListContainer.Children.Add(card);
        }
    }

    private void OpenAddProviderDialog()
    {
        _dialogEditingProvider = null;
        _dialogModels = new List<AiModelItem>();

        if (DialogTitleTextBlock != null) DialogTitleTextBlock.Text = "娣诲姞渚涘簲鍟?;
        if (DialogNameTextBox != null) DialogNameTextBox.Text = "";
        if (DialogBaseUrlTextBox != null) DialogBaseUrlTextBox.Text = "";
        if (DialogApiKeyTextBox != null) DialogApiKeyTextBox.Text = "";
        if (DialogNewModelTextBox != null) DialogNewModelTextBox.Text = "";

        RenderDialogModels();
        if (ProviderDialogOverlay != null) ProviderDialogOverlay.IsVisible = true;
    }

    private void OpenEditProviderDialog(AiProviderConfig provider)
    {
        _dialogEditingProvider = provider;
        _dialogModels = provider.Models.Select(m => new AiModelItem(m.Name, m.IsEnabled)).ToList();

        if (DialogTitleTextBlock != null) DialogTitleTextBlock.Text = $"缂栬緫渚涘簲鍟?- {provider.Name}";
        if (DialogNameTextBox != null) DialogNameTextBox.Text = provider.Name;
        if (DialogBaseUrlTextBox != null) DialogBaseUrlTextBox.Text = provider.BaseUrl;
        if (DialogApiKeyTextBox != null) DialogApiKeyTextBox.Text = provider.ApiKey;
        if (DialogNewModelTextBox != null) DialogNewModelTextBox.Text = "";

        RenderDialogModels();
        if (ProviderDialogOverlay != null) ProviderDialogOverlay.IsVisible = true;
    }

    private void RenderDialogModels()
    {
        if (DialogModelsContainer == null) return;
        DialogModelsContainer.Children.Clear();

        if (_dialogModels.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "鏆傛棤妯″瀷",
                Foreground = SubtleBrush,
                FontSize = 12,
                Margin = new Thickness(4)
            };
            DialogModelsContainer.Children.Add(hint);
            return;
        }

        foreach (var model in _dialogModels)
        {
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto"),
                Margin = new Thickness(0, 2)
            };

            var checkBox = new CheckBox
            {
                Content = model.Name,
                IsChecked = model.IsEnabled,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                model.IsEnabled = checkBox.IsChecked == true;
            };

            var delBtn = new Button
            {
                Content = "鍒犻櫎",
                Padding = new Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            delBtn.Click += (_, _) =>
            {
                _dialogModels.Remove(model);
                RenderDialogModels();
                ShowToast($"宸茬Щ闄ゆā鍨嬨€寋model.Name}銆?);
            };
            Grid.SetColumn(delBtn, 1);

            rowGrid.Children.Add(checkBox);
            rowGrid.Children.Add(delBtn);
            DialogModelsContainer.Children.Add(rowGrid);
        }
    }

    private void OnDialogAddModelClicked(object? sender, RoutedEventArgs? e)
    {
        var modelName = DialogNewModelTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(modelName)) return;

        if (!_dialogModels.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogModels.Add(new AiModelItem(modelName, true));
            if (DialogNewModelTextBox != null) DialogNewModelTextBox.Text = "";
            RenderDialogModels();
            ShowToast($"宸叉坊鍔犳ā鍨嬨€寋modelName}銆?);
        }
        else
        {
            ShowToast("璇ユā鍨嬪凡鍦ㄥ垪琛ㄤ腑锛?, isError: true);
        }
    }

    private async void OnDialogFetchModelsClicked(object? sender, RoutedEventArgs e)
    {
        var baseUrl = DialogBaseUrlTextBox?.Text?.Trim() ?? "";
        var apiKey = DialogApiKeyTextBox?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            ShowToast("璇峰厛濉啓 API 鍦板潃 (Base URL)锛?, isError: true);
            return;
        }

        ShowToast("姝ｅ湪浠?API 鑾峰彇妯″瀷鍒楄〃...");

        try
        {
            var models = await _aiClient.FetchModelsAsync(baseUrl, apiKey);
            if (models.Count > 0)
            {
                int addedCount = 0;
                foreach (var m in models)
                {
                    if (!_dialogModels.Any(existing => existing.Name.Equals(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        _dialogModels.Add(new AiModelItem(m, true));
                        addedCount++;
                    }
                }
                RenderDialogModels();
                ShowToast($"鎴愬姛鑾峰彇 {models.Count} 涓ā鍨嬶紙鏂板 {addedCount} 涓級锛?);
            }
            else
            {
                ShowToast("鏈兘鑾峰彇鍒颁换浣曟ā鍨嬶紝璇锋鏌ュ湴鍧€涓庡瘑閽ャ€?, isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"鑾峰彇妯″瀷澶辫触: {ex.Message}", isError: true);
        }
    }

    private void SaveProviderDialog()
    {
        var name = DialogNameTextBox?.Text?.Trim() ?? "";
        var baseUrl = DialogBaseUrlTextBox?.Text?.Trim() ?? "";
        var apiKey = DialogApiKeyTextBox?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("璇疯緭鍏ヤ緵搴斿晢鍒悕锛?, isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            ShowToast("璇疯緭鍏?API 鍦板潃 (Base URL)锛?, isError: true);
            return;
        }

        if (_dialogEditingProvider == null)
        {
            var newP = new AiProviderConfig
            {
                Name = name,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                Models = _dialogModels
            };
            _settings.Providers.Add(newP);
            if (string.IsNullOrWhiteSpace(_settings.ActiveProviderId))
            {
                _settings.ActiveProviderId = newP.Id;
            }
            SaveSettingsToFile();
            CloseProviderDialog();
            RenderProvidersList();
            ShowToast($"宸叉垚鍔熸坊鍔犱緵搴斿晢銆寋name}銆嶏紒");
        }
        else
        {
            _dialogEditingProvider.Name = name;
            _dialogEditingProvider.BaseUrl = baseUrl;
            _dialogEditingProvider.ApiKey = apiKey;
            _dialogEditingProvider.Models = _dialogModels;

            SaveSettingsToFile();
            CloseProviderDialog();
            RenderProvidersList();
            ShowToast($"渚涘簲鍟嗐€寋name}銆嶄慨鏀瑰凡淇濆瓨锛?);
        }
    }

    private void CloseProviderDialog()
    {
        if (ProviderDialogOverlay != null)
        {
            ProviderDialogOverlay.IsVisible = false;
        }
    }

    private void SyncFromInputs()
    {
        _settings.IsMemoryEnabled = IsMemoryEnabledCheckBox?.IsChecked ?? true;
        _settings.IsCommandExecutionEnabled = IsCommandExecutionEnabledCheckBox?.IsChecked ?? false;
        var iterations = MaxToolIterationsNumeric?.Value;
        _settings.MaxToolIterations = iterations.HasValue
            ? (int)Math.Clamp(iterations.Value, 1m, 50m)
            : 8;
        _settings.CustomPrompt = CustomPromptTextBox?.Text?.Trim() ?? "";
        _settings.CustomMemory = CustomMemoryTextBox?.Text?.Trim() ?? "";
    }

    private void InitSecurityControls()
    {
        UpdateSecurityStateUi();

        if (SavePasswordButton != null)
        {
            SavePasswordButton.Click += (_, _) =>
            {
                var newPwd = NewPasswordBox?.Text?.Trim() ?? "";
                SecurityService.SetPassword(_settings, newPwd);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (NewPasswordBox != null) NewPasswordBox.Text = "";
                ShowToast(string.IsNullOrEmpty(newPwd) ? "鍥哄畾瀵嗙爜宸叉竻闄わ紒" : "鍥哄畾瀵嗙爜宸叉洿鏂帮紒");
            };
        }

        if (ClearPasswordButton != null)
        {
            ClearPasswordButton.Click += (_, _) =>
            {
                SecurityService.SetPassword(_settings, "");
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (NewPasswordBox != null) NewPasswordBox.Text = "";
                ShowToast("鍥哄畾瀵嗙爜宸叉竻闄わ紙鎭㈠涓烘棤瀵嗙爜锛夛紒");
            };
        }

        if (SetupTotpButton != null)
        {
            SetupTotpButton.Click += (_, _) =>
            {
                var secret = SecurityService.StartSetupTotp(_settings);
                SaveSettingsToFile();
                DisplayTotpSetupCard(secret);
                UpdateSecurityStateUi();
                ShowToast("宸茬敓鎴愭柊鐨?TOTP 瀵嗛挜锛?);
            };
        }

        if (CopyTotpSecretButton != null)
        {
            CopyTotpSecretButton.Click += async (_, _) =>
            {
                var rawSecret = TotpService.CleanSecret(_settings.PendingTotpSecret.Length > 0 ? _settings.PendingTotpSecret : _settings.TotpSecret);
                if (!string.IsNullOrEmpty(rawSecret))
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(rawSecret);
                    }
                    ShowToast("TOTP 瀵嗛挜瀛楃涓插凡澶嶅埗鍒板壀璐存澘锛?);
                }
            };
        }

        if (VerifyAndActivateTotpButton != null)
        {
            VerifyAndActivateTotpButton.Click += (_, _) =>
            {
                var code = TotpVerifyCodeTextBox?.Text?.Trim() ?? "";
                if (SecurityService.VerifyAndActivatePendingTotp(_settings, code))
                {
                    SaveSettingsToFile();
                    UpdateSecurityStateUi();
                    if (TotpVerifyResultTextBlock != null)
                    {
                        TotpVerifyResultTextBlock.Text = "楠岃瘉閫氳繃锛乀OTP 鍔ㄦ€佸彛浠ゅ凡姝ｅ紡鐢熸晥淇濇姢銆?;
                        TotpVerifyResultTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    ShowToast("TOTP 鍔ㄦ€佸彛浠ゅ凡姝ｅ紡婵€娲伙紒");
                }
                else
                {
                    if (TotpVerifyResultTextBlock != null)
                    {
                        TotpVerifyResultTextBlock.Text = "楠岃瘉鐮侀敊璇垨宸插け鏁堬紝璇风‘璁ゆ墜鐜?鎵嬫満褰撳墠鏃堕棿骞堕噸璇曘€?;
                        TotpVerifyResultTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    ShowToast("TOTP 楠岃瘉澶辫触锛岃閲嶈瘯锛?, isError: true);
                }
            };
        }

        if (CancelPendingTotpButton != null)
        {
            CancelPendingTotpButton.Click += (_, _) =>
            {
                SecurityService.CancelPendingTotp(_settings);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                ShowToast("宸叉斁寮冩殏瀛樼殑 TOTP 閰嶇疆锛?);
            };
        }

        if (DisableTotpButton != null)
        {
            DisableTotpButton.Click += (_, _) =>
            {
                SecurityService.DisableTotp(_settings);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                ShowToast("TOTP 鍔ㄦ€佸彛浠ゅ凡鍏抽棴锛?);
            };
        }
    }

    private void UpdateSecurityStateUi()
    {
        if (PasswordStatusTextBlock != null)
        {
            bool hasPwd = SecurityService.HasPassword(_settings);
            PasswordStatusTextBlock.Text = hasPwd ? "褰撳墠鐘舵€侊細宸茶缃浐瀹氬瘑鐮? : "褰撳墠鐘舵€侊細鏈缃浐瀹氬瘑鐮侊紙榛樿鏃犲瘑鐮侊級";
            if (ClearPasswordButton != null) ClearPasswordButton.IsVisible = hasPwd;
        }

        if (TotpStatusTextBlock != null)
        {
            if (SecurityService.HasActiveTotp(_settings))
            {
                TotpStatusTextBlock.Text = "褰撳墠鐘舵€侊細TOTP 鍔ㄦ€佸彛浠ゅ凡姝ｅ紡婵€娲荤敓鏁堜繚鎶?;
                if (SetupTotpButton != null) SetupTotpButton.Content = "閲嶆柊閰嶇疆 TOTP";
                if (DisableTotpButton != null) DisableTotpButton.IsVisible = true;
                if (TotpSetupCard != null) TotpSetupCard.IsVisible = false;
            }
            else if (SecurityService.HasPendingTotp(_settings))
            {
                TotpStatusTextBlock.Text = "褰撳墠鐘舵€侊細鍙戠幇鏆傚瓨鐨勫緟楠岃瘉 TOTP锛堝湪杈撳叆姝ｇ‘鍔ㄦ€佺爜鍓嶇粷涓嶇敓鏁堬級";
                if (SetupTotpButton != null) SetupTotpButton.Content = "閲嶆柊鐢熸垚 TOTP";
                if (DisableTotpButton != null) DisableTotpButton.IsVisible = false;
                DisplayTotpSetupCard(_settings.PendingTotpSecret);
            }
            else
            {
                TotpStatusTextBlock.Text = "褰撳墠鐘舵€侊細鏈惎鐢?TOTP 鍔ㄦ€佸彛浠?;
                if (SetupTotpButton != null) SetupTotpButton.Content = "鐢熸垚骞堕厤缃?TOTP";
                if (DisableTotpButton != null) DisableTotpButton.IsVisible = false;
                if (TotpSetupCard != null) TotpSetupCard.IsVisible = false;
            }
        }
    }

    private void DisplayTotpSetupCard(string secret)
    {
        if (TotpSetupCard == null) return;
        TotpSetupCard.IsVisible = true;

        if (TotpSecretStringTextBox != null)
        {
            TotpSecretStringTextBox.Text = TotpService.FormatSecretForDisplay(secret);
        }

        if (TotpVerifyResultTextBlock != null)
        {
            TotpVerifyResultTextBlock.Text = "";
        }

        if (TotpVerifyCodeTextBox != null)
        {
            TotpVerifyCodeTextBox.Text = "";
        }

        try
        {
            string uri = TotpService.GenerateOtpAuthUri(secret);
            byte[] pngBytes = QrCodeService.GeneratePngBytes(uri);
            if (pngBytes.Length > 0 && TotpQrCodeImage != null)
            {
                using var ms = new MemoryStream(pngBytes);
                TotpQrCodeImage.Source = new Bitmap(ms);
            }
        }
        catch { }
    }
}
