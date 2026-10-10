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

[SettingsPageInfo("memz.ci.automation.settings", "CI自动化", SettingsPageCategory.External)]
public partial class AutomationSettingsPage : SettingsPageBase
{
    private PluginSettings _settings;
    private readonly string _settingsFilePath;
    private readonly AiClientService _aiClient = new();
    private bool _isUpdatingUi = false;
    private bool _isUnlocked = false;

    public AutomationSettingsPage()
    {
        InitializeComponent();

        var configDir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(configDir);
        _settingsFilePath = Path.Combine(configDir, "settings.json");
        _settings = SettingsManager.Load(_settingsFilePath);

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
                UnlockErrorTextBlock.Text = "密码或 6 位 TOTP 动态码错误，请重试！";
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
        if (ProviderComboBox != null)
        {
            ProviderComboBox.SelectedIndex = (int)_settings.Provider;
            ProviderComboBox.SelectionChanged += OnProviderChanged;
        }

        UpdateInputsForProvider();

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
                    try
                    {
                        Clipboard.SetText(rawSecret);
                        if (StatusTextBlock != null) StatusTextBlock.Text = "TOTP 密钥字符串已复制到剪贴板！";
                    }
                    catch { }
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
                        TotpVerifyResultTextBlock.Foreground = Brushes.Green;
                    }
                }
                else
                {
                    if (TotpVerifyResultTextBlock != null)
                    {
                        TotpVerifyResultTextBlock.Text = "❌ 验证码错误或已失效，请确认手环/手机当前时间并重试。";
                        TotpVerifyResultTextBlock.Foreground = Brushes.Red;
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
                if (StatusTextBlock != null) StatusTextBlock.Text = "已取消待验证的 TOTP 暂存。";
            };
        }

        if (DisableTotpButton != null)
        {
            DisableTotpButton.Click += (_, _) =>
            {
                SecurityService.DisableTotp(_settings);
                SaveSettingsToFile();
                UpdateSecurityStateUi();
                if (StatusTextBlock != null) StatusTextBlock.Text = "TOTP 动态口令保护已完全关闭。";
            };
        }
    }

    private void UpdateSecurityStateUi()
    {
        // 1. 固定密码状态
        if (PasswordStatusTextBlock != null)
        {
            bool hasPwd = SecurityService.HasPassword(_settings);
            PasswordStatusTextBlock.Text = hasPwd ? "当前状态：已启用固定密码保护" : "当前状态：未设置固定密码（默认无密码）";
            if (ClearPasswordButton != null) ClearPasswordButton.Visibility = hasPwd ? Visibility.Visible : Visibility.Collapsed;
        }

        // 2. TOTP 状态
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
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(pngBytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                TotpQrCodeImage.Source = bitmap;
            }
        }
        catch { }
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
                _settings.MiMoApiKey = ApiKeyPasswordBox?.Password?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.MiMoModel = model;
                break;
            case AiProviderType.DeepSeek:
                _settings.DeepSeekBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.DeepSeekApiKey = ApiKeyPasswordBox?.Password?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.DeepSeekModel = model;
                break;
            default:
                _settings.CustomBaseUrl = BaseUrlTextBox?.Text?.Trim() ?? "";
                _settings.CustomApiKey = ApiKeyPasswordBox?.Password?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(model)) _settings.CustomModel = model;
                break;
        }

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
