using System.Text.Json;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Shared;

namespace ClassIsland.Automation.Core.Services;

/// <summary>
/// 集中式配置管理器，确保所有窗口和工具执行器共享同一配置实例并实现配置变更全应用实时同步。
/// </summary>
public static class SettingsManager
{
    private static readonly object _lock = new();
    private static PluginSettings _settings = new();
    private static string? _settingsFilePath;
    private static bool _loaded;

    public static event Action<PluginSettings>? SettingsChanged;

    /// <summary>
    /// 共享配置实例。若尚未从磁盘加载过，则自动完成一次加载，
    /// 避免只打开对话窗口（未进设置页）时拿到一份空配置。
    /// </summary>
    public static PluginSettings Current
    {
        get
        {
            lock (_lock)
            {
                if (!_loaded)
                {
                    LoadCore(ResolveDefaultPath());
                }
                return _settings;
            }
        }
    }

    /// <summary>
    /// 未显式指定路径时的配置位置，与插件配置目录保持一致。
    /// </summary>
    private static string ResolveDefaultPath()
    {
        var dir = Plugin.Instance?.PluginConfigFolder ?? AppContext.BaseDirectory;
        return Path.Combine(dir, "settings.json");
    }

    public static PluginSettings Load(string filePath)
    {
        lock (_lock)
        {
            LoadCore(filePath);
            return _settings;
        }
    }

    private static void LoadCore(string filePath)
    {
        _settingsFilePath = filePath;
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                _settings = JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
                _settings.EnsureProvidersInitialized();
                _loaded = true;
                return;
            }
        }
        catch { }

        _settings = new PluginSettings();
        _settings.EnsureProvidersInitialized();
        _loaded = true;
    }

    public static void Save(PluginSettings? settings = null)
    {
        lock (_lock)
        {
            if (settings != null)
            {
                _settings = settings;
            }

            if (!string.IsNullOrEmpty(_settingsFilePath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_settingsFilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_settingsFilePath, json);
                }
                catch { }
            }

            NotifySettingsChanged(_settings);
        }
    }

    public static void NotifySettingsChanged(PluginSettings settings)
    {
        SettingsChanged?.Invoke(settings);
    }
}
