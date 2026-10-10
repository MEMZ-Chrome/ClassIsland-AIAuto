using System.Text.Json.Serialization;
using ClassIsland.Automation.Core.Services;

namespace ClassIsland.Automation.Core.Models;

public enum AiProviderType
{
    MiMo,
    DeepSeek,
    Custom
}

public class PluginSettings
{
    public AiProviderType Provider { get; set; } = AiProviderType.MiMo;

    // 小米 MiMo 配置
    public string MiMoBaseUrl { get; set; } = "https://api.xiaomimimo.com/v1";
    
    [JsonIgnore]
    public string MiMoApiKey { get; set; } = "";

    [JsonPropertyName("MiMoApiKey")]
    public string MiMoApiKeyEncrypted
    {
        get => SecretProtectionService.Encrypt(MiMoApiKey);
        set => MiMoApiKey = SecretProtectionService.Decrypt(value);
    }

    public string MiMoModel { get; set; } = "";

    // DeepSeek 配置
    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";

    [JsonIgnore]
    public string DeepSeekApiKey { get; set; } = "";

    [JsonPropertyName("DeepSeekApiKey")]
    public string DeepSeekApiKeyEncrypted
    {
        get => SecretProtectionService.Encrypt(DeepSeekApiKey);
        set => DeepSeekApiKey = SecretProtectionService.Decrypt(value);
    }

    public string DeepSeekModel { get; set; } = "";

    // 自定义提供商配置
    public string CustomBaseUrl { get; set; } = "https://api.openai.com/v1";

    [JsonIgnore]
    public string CustomApiKey { get; set; } = "";

    [JsonPropertyName("CustomApiKey")]
    public string CustomApiKeyEncrypted
    {
        get => SecretProtectionService.Encrypt(CustomApiKey);
        set => CustomApiKey = SecretProtectionService.Decrypt(value);
    }

    public string CustomModel { get; set; } = "";

    // 常用提示词/自定义系统提示词
    public string CustomPrompt { get; set; } = "";

    // 长期记忆系统（AI 自动记录或用户自定义的偏好、习惯与背景事实）
    public string CustomMemory { get; set; } = "";

    // 是否启用记忆系统
    public bool IsMemoryEnabled { get; set; } = true;

    // 是否允许 AI 执行 CMD 命令行命令
    public bool IsCommandExecutionEnabled { get; set; } = false;

    // 固定密码散列与盐（空表示未设置固定密码）
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";

    // 正式激活生效的 TOTP 密钥（加密保存，空表示未启用 TOTP）
    [JsonIgnore]
    public string TotpSecret { get; set; } = "";

    [JsonPropertyName("TotpSecret")]
    public string TotpSecretEncrypted
    {
        get => SecretProtectionService.Encrypt(TotpSecret);
        set => TotpSecret = SecretProtectionService.Decrypt(value);
    }

    // 待验证暂存的 TOTP 密钥（加密保存，在验证可用前绝对不生效，防止因手环未同步而锁死用户）
    [JsonIgnore]
    public string PendingTotpSecret { get; set; } = "";

    [JsonPropertyName("PendingTotpSecret")]
    public string PendingTotpSecretEncrypted
    {
        get => SecretProtectionService.Encrypt(PendingTotpSecret);
        set => PendingTotpSecret = SecretProtectionService.Decrypt(value);
    }

    [JsonIgnore]
    public string CurrentBaseUrl => Provider switch
    {
        AiProviderType.MiMo => string.IsNullOrWhiteSpace(MiMoBaseUrl) ? "https://api.xiaomimimo.com/v1" : MiMoBaseUrl,
        AiProviderType.DeepSeek => string.IsNullOrWhiteSpace(DeepSeekBaseUrl) ? "https://api.deepseek.com" : DeepSeekBaseUrl,
        _ => CustomBaseUrl
    };

    [JsonIgnore]
    public string CurrentApiKey => Provider switch
    {
        AiProviderType.MiMo => MiMoApiKey,
        AiProviderType.DeepSeek => DeepSeekApiKey,
        _ => CustomApiKey
    };

    [JsonIgnore]
    public string CurrentModel => Provider switch
    {
        AiProviderType.MiMo => MiMoModel,
        AiProviderType.DeepSeek => DeepSeekModel,
        _ => CustomModel
    };

    public void SetModelForCurrentProvider(string model)
    {
        switch (Provider)
        {
            case AiProviderType.MiMo:
                MiMoModel = model;
                break;
            case AiProviderType.DeepSeek:
                DeepSeekModel = model;
                break;
            default:
                CustomModel = model;
                break;
        }
    }
}
