using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Automation.V1.Views.SettingsPages;

[SettingsPageInfo("memzchrome.islandagent.settings", "IslandAgent设置", SettingsPageCategory.External)]
public partial class AutomationSettingsPage : SettingsPageBase
{
    private PluginSettings _settings;
    private readonly string _settingsFilePath;
    private readonly AiClientService _aiClient = new();
    private bool _isUpdatingUi = false;
    private bool _isUnlocked = false;
    private AiProviderConfig? _currentEditingProvider;

    public AutomationSettingsPage()
    {
        InitializeComponent();

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "settings.json");
        _settings = SettingsManager.Load(_settingsFilePath);
        _settings.EnsureProvidersInitialized();

        _currentEditingProvider = _settings.CurrentProvider ?? _settings.Providers.FirstOrDefault();

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

    private void OnExternalSettingsChanged(PluginSettings updated)
    {
        Dispatcher.Invoke(() =>
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
        // 关键安全设计：待验证的暂存 TOTP 绝不能使页面进入锁定状态，防止用户因手环未同步而卡死！
        if (SecurityService.IsActiveProtectionEnabled(_settings) && !_isUnlocked)
        {
            if (LockOverlayBorder != null) LockOverlayBorder.Visibility = Visibility.Visible;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            _isUnlocked = true;
            if (LockOverlayBorder != null) LockOverlayBorder.Visibility = Visibility.Collapsed;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.Visibility = Visibility.Visible;
        }

        if (UnlockButton != null)
        {
            UnlockButton.Click += (_, _) => AttemptUnlock();
        }

        if (UnlockPasswordBox != null)
        {
            UnlockPasswordBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AttemptUnlock();
                }
            };
        }
    }

    private void AttemptUnlock()
    {
        var input = UnlockPasswordBox?.Password?.Trim() ?? "";
        var result = SecurityService.VerifyAccess(_settings, input);

        if (result != AccessAuthResult.Failed)
        {
            _isUnlocked = true;
            if (UnlockErrorTextBlock != null) UnlockErrorTextBlock.Visibility = Visibility.Collapsed;
            if (LockOverlayBorder != null) LockOverlayBorder.Visibility = Visibility.Collapsed;
            if (MainSettingsScrollViewer != null) MainSettingsScrollViewer.Visibility = Visibility.Visible;
            if (UnlockPasswordBox != null) UnlockPasswordBox.Password = "";
        }
        else
        {
            if (UnlockErrorTextBlock != null)
            {
                UnlockErrorTextBlock.Text = "密码或 TOTP 动态验证码错误，请重新输入！";
                UnlockErrorTextBlock.Visibility = Visibility.Visible;
            }
        }
    }

    private void SaveSettingsToFile()
    {
        try
        {
            SettingsManager.Save(_settings);
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
        UpdateProviderComboBox();
        UpdateInputsForCurrentProvider();

        if (ProviderComboBox != null)
        {
            ProviderComboBox.SelectionChanged += OnProviderSelectionChanged;
        }

        if (AddProviderButton != null)
        {
            AddProviderButton.Click += OnAddProviderClicked;
        }

        if (DeleteProviderButton != null)
        {
            DeleteProviderButton.Click += OnDeleteProviderClicked;
        }

        if (ProviderNameTextBox != null)
        {
            ProviderNameTextBox.LostFocus += (_, _) =>
            {
                SyncFromCurrentProviderInputs();
                AutoSave();
            };
        }

        if (BaseUrlTextBox != null)
        {
            BaseUrlTextBox.LostFocus += (_, _) =>
            {
                SyncFromCurrentProviderInputs();
                AutoSave();
            };
        }

        if (ApiKeyPasswordBox != null)
        {
            ApiKeyPasswordBox.LostFocus += (_, _) =>
            {
                SyncFromCurrentProviderInputs();
                AutoSave();
            };
        }

        if (FetchModelsButton != null)
        {
            FetchModelsButton.Click += OnFetchModelsClicked;
        }

        if (AddModelButton != null)
        {
            AddModelButton.Click += OnAddModelClicked;
        }

        if (SaveButton != null)
        {
            SaveButton.Click += (_, _) =>
            {
                SyncFromInputs();
                SaveSettingsToFile();
                if (StatusTextBlock != null) StatusTextBlock.Text = "设置已保存成功！";
            };
        }

        if (IsMemoryEnabledCheckBox != null)
        {
            IsMemoryEnabledCheckBox.IsChecked = _settings.IsMemoryEnabled;
            IsMemoryEnabledCheckBox.Click += (_, _) => AutoSave();
        }

        if (IsCommandExecutionEnabledCheckBox != null)
        {
            IsCommandExecutionEnabledCheckBox.IsChecked = _settings.IsCommandExecutionEnabled;
            IsCommandExecutionEnabledCheckBox.Click += (_, _) => AutoSave();
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

    private void UpdateProviderComboBox(bool preserveIndex = false)
    {
        if (ProviderComboBox == null) return;
        _isUpdatingUi = true;
        try
        {
            var oldIdx = preserveIndex ? ProviderComboBox.SelectedIndex : -1;
            var names = _settings.Providers.Select(p => string.IsNullOrWhiteSpace(p.Name) ? "(未命名供应商)" : p.Name).ToList();
            ProviderComboBox.ItemsSource = names;
            if (oldIdx >= 0 && oldIdx < names.Count)
            {
                ProviderComboBox.SelectedIndex = oldIdx;
            }
            else if (_currentEditingProvider != null)
            {
                var idx = _settings.Providers.IndexOf(_currentEditingProvider);
                ProviderComboBox.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateInputsForCurrentProvider()
    {
        _isUpdatingUi = true;
        try
        {
            if (_currentEditingProvider == null) return;
            if (ProviderNameTextBox != null) ProviderNameTextBox.Text = _currentEditingProvider.Name;
            if (BaseUrlTextBox != null) BaseUrlTextBox.Text = _currentEditingProvider.BaseUrl;
            if (ApiKeyPasswordBox != null) ApiKeyPasswordBox.Password = _currentEditingProvider.ApiKey;
            RenderModelsList();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void RenderModelsList()
    {
        if (ModelsContainer == null || _currentEditingProvider == null) return;
        ModelsContainer.Children.Clear();

        if (_currentEditingProvider.Models.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "当前供应商暂无模型。请点击「自动获取模型列表」或在上方手动输入添加。",
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(4)
            };
            ModelsContainer.Children.Add(hint);
            return;
        }

        foreach (var model in _currentEditingProvider.Models)
        {
            var rowGrid = new Grid
            {
                Margin = new Thickness(0, 2)
            };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkBox = new CheckBox
            {
                Content = model.Name,
                IsChecked = model.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            checkBox.Click += (_, _) =>
            {
                model.IsEnabled = checkBox.IsChecked == true;
                AutoSave();
            };

            var delBtn = new Button
            {
                Content = "✕",
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            delBtn.Click += (_, _) =>
            {
                _currentEditingProvider.Models.Remove(model);
                RenderModelsList();
                AutoSave();
            };
            Grid.SetColumn(delBtn, 1);

            rowGrid.Children.Add(checkBox);
            rowGrid.Children.Add(delBtn);
            ModelsContainer.Children.Add(rowGrid);
        }
    }

    private void OnProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        SyncFromCurrentProviderInputs();

        var idx = ProviderComboBox?.SelectedIndex ?? -1;
        if (idx >= 0 && idx < _settings.Providers.Count)
        {
            _currentEditingProvider = _settings.Providers[idx];
            _settings.ActiveProviderId = _currentEditingProvider.Id;
            UpdateInputsForCurrentProvider();
            SaveSettingsToFile();
        }
    }

    private void OnAddProviderClicked(object? sender, RoutedEventArgs e)
    {
        SyncFromCurrentProviderInputs();
        var newP = new AiProviderConfig
        {
            Name = $"供应商 {_settings.Providers.Count + 1}",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "",
            Models = new List<AiModelItem>()
        };
        _settings.Providers.Add(newP);
        _currentEditingProvider = newP;
        _settings.ActiveProviderId = newP.Id;

        UpdateProviderComboBox();
        ProviderComboBox.SelectedIndex = _settings.Providers.Count - 1;
        UpdateInputsForCurrentProvider();
        SaveSettingsToFile();
    }

    private void OnDeleteProviderClicked(object? sender, RoutedEventArgs e)
    {
        if (_settings.Providers.Count <= 1)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = "至少需保留一个供应商！";
            return;
        }

        if (_currentEditingProvider != null)
        {
            _settings.Providers.Remove(_currentEditingProvider);
            _currentEditingProvider = _settings.Providers.FirstOrDefault();
            _settings.ActiveProviderId = _currentEditingProvider?.Id ?? "";
            UpdateProviderComboBox();
            ProviderComboBox.SelectedIndex = 0;
            UpdateInputsForCurrentProvider();
            SaveSettingsToFile();
        }
    }

    private void OnAddModelClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentEditingProvider == null) return;
        var modelName = NewModelTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(modelName)) return;

        if (!_currentEditingProvider.Models.Any(m => m.Name == modelName))
        {
            _currentEditingProvider.Models.Add(new AiModelItem(modelName, true));
            if (NewModelTextBox != null) NewModelTextBox.Text = "";
            RenderModelsList();
            AutoSave();
        }
    }

    private async void OnFetchModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentEditingProvider == null) return;
        SyncFromCurrentProviderInputs();

        if (StatusTextBlock != null) StatusTextBlock.Text = "正在获取模型列表...";

        try
        {
            var models = await _aiClient.FetchModelsAsync(_currentEditingProvider.BaseUrl, _currentEditingProvider.ApiKey);
            if (models.Count > 0)
            {
                foreach (var m in models)
                {
                    if (!_currentEditingProvider.Models.Any(existing => existing.Name == m))
                    {
                        _currentEditingProvider.Models.Add(new AiModelItem(m, true));
                    }
                }
                RenderModelsList();
                AutoSave();
                if (StatusTextBlock != null) StatusTextBlock.Text = $"成功获取 {models.Count} 个模型并已加入列表！";
            }
            else
            {
                if (StatusTextBlock != null) StatusTextBlock.Text = "未能获取到任何模型，请检查地址与密钥。";
            }
        }
        catch (Exception ex)
        {
            if (StatusTextBlock != null) StatusTextBlock.Text = $"获取模型失败: {ex.Message}";
        }
    }

    private void SyncFromInputs()
    {
        SyncFromCurrentProviderInputs();
        _settings.IsMemoryEnabled = IsMemoryEnabledCheckBox?.IsChecked ?? true;
        _settings.IsCommandExecutionEnabled = IsCommandExecutionEnabledCheckBox?.IsChecked ?? false;
        _settings.CustomPrompt = CustomPromptTextBox?.Text?.Trim() ?? "";
        _settings.CustomMemory = CustomMemoryTextBox?.Text?.Trim() ?? "";
    }

    private void SyncFromCurrentProviderInputs()
    {
        if (_currentEditingProvider == null) return;
        var name = ProviderNameTextBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && _currentEditingProvider.Name != name)
        {
            _currentEditingProvider.Name = name;
            UpdateProviderComboBox(preserveIndex: true);
        }
        _currentEditingProvider.BaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
        _currentEditingProvider.ApiKey = ApiKeyPasswordBox?.Password?.Trim() ?? "";
    }

    private void InitSecurityControls()
    {
        UpdateSecurityStateUi();

        if (SavePasswordButton != null)
        {
            SavePasswordButton.Click += (_, _) =>
            {
                var newPwd = NewPasswordBox?.Password?.Trim() ?? "";
                SecurityService.SetPassword(_settings, newPwd);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (NewPasswordBox != null) NewPasswordBox.Password = "";
                if (StatusTextBlock != null) StatusTextBlock.Text = string.IsNullOrEmpty(newPwd) ? "固定密码已清除！" : "固定密码已更新！";
            };
        }

        if (ClearPasswordButton != null)
        {
            ClearPasswordButton.Click += (_, _) =>
            {
                SecurityService.SetPassword(_settings, "");
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (NewPasswordBox != null) NewPasswordBox.Password = "";
                if (StatusTextBlock != null) StatusTextBlock.Text = "固定密码已清除（恢复为无密码）！";
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
            };
        }

        if (CopyTotpSecretButton != null)
        {
            CopyTotpSecretButton.Click += (_, _) =>
            {
                var rawSecret = TotpService.CleanSecret(_settings.PendingTotpSecret.Length > 0 ? _settings.PendingTotpSecret : _settings.TotpSecret);
                if (!string.IsNullOrEmpty(rawSecret))
                {
                    Clipboard.SetText(rawSecret);
                    if (StatusTextBlock != null) StatusTextBlock.Text = "TOTP 密钥字符串已复制到剪贴板！";
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
                        TotpVerifyResultTextBlock.Text = "✅ 验证通过！TOTP 动态口令已正式生效保护。";
                        TotpVerifyResultTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                }
                else
                {
                    if (TotpVerifyResultTextBlock != null)
                    {
                        TotpVerifyResultTextBlock.Text = "❌ 验证码错误或已失效，请确认手环/手机当前时间并重试。";
                        TotpVerifyResultTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    }
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
                if (StatusTextBlock != null) StatusTextBlock.Text = "已放弃暂存的 TOTP 配置！";
            };
        }

        if (DisableTotpButton != null)
        {
            DisableTotpButton.Click += (_, _) =>
            {
                SecurityService.DisableTotp(_settings);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (StatusTextBlock != null) StatusTextBlock.Text = "TOTP 动态口令已关闭！";
            };
        }
    }

    private void UpdateSecurityStateUi()
    {
        if (PasswordStatusTextBlock != null)
        {
            bool hasPwd = SecurityService.HasPassword(_settings);
            PasswordStatusTextBlock.Text = hasPwd ? "当前状态：已设置固定密码 🔒" : "当前状态：未设置固定密码（默认无密码）";
            if (ClearPasswordButton != null) ClearPasswordButton.Visibility = hasPwd ? Visibility.Visible : Visibility.Collapsed;
        }

        if (TotpStatusTextBlock != null)
        {
            if (SecurityService.HasActiveTotp(_settings))
            {
                TotpStatusTextBlock.Text = "当前状态：✅ TOTP 动态口令已正式激活生效保护";
                if (SetupTotpButton != null) SetupTotpButton.Content = "重新配置 TOTP";
                if (DisableTotpButton != null) DisableTotpButton.Visibility = Visibility.Visible;
                if (TotpSetupCard != null) TotpSetupCard.Visibility = Visibility.Collapsed;
            }
            else if (SecurityService.HasPendingTotp(_settings))
            {
                TotpStatusTextBlock.Text = "当前状态：⚠️ 发现暂存的待验证 TOTP（在输入正确动态码前绝不生效）";
                if (SetupTotpButton != null) SetupTotpButton.Content = "重新生成 TOTP";
                if (DisableTotpButton != null) DisableTotpButton.Visibility = Visibility.Collapsed;
                DisplayTotpSetupCard(_settings.PendingTotpSecret);
            }
            else
            {
                TotpStatusTextBlock.Text = "当前状态：未启用 TOTP 动态口令";
                if (SetupTotpButton != null) SetupTotpButton.Content = "生成并配置 TOTP";
                if (DisableTotpButton != null) DisableTotpButton.Visibility = Visibility.Collapsed;
                if (TotpSetupCard != null) TotpSetupCard.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void DisplayTotpSetupCard(string secret)
    {
        if (TotpSetupCard == null) return;
        TotpSetupCard.Visibility = Visibility.Visible;

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
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.CacheOption = BitmapCreateOptions.None;
                bi.EndInit();
                TotpQrCodeImage.Source = bi;
            }
        }
        catch { }
    }
}
