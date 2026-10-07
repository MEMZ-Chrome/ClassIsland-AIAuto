using System.Text.Json.Serialization;

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
    public string MiMoApiKey { get; set; } = "";
    public string MiMoModel { get; set; } = "";

    // DeepSeek 配置
    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
    public string DeepSeekApiKey { get; set; } = "";
    public string DeepSeekModel { get; set; } = "";

    // 自定义提供商配置
    public string CustomBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string CustomApiKey { get; set; } = "";
    public string CustomModel { get; set; } = "";

    // 常用提示词/系统偏好
    public string CustomPrompt { get; set; } = "";

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
