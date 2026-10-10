using System.Text.Json;
using ClassIsland.Automation.Core.Models;

namespace ClassIsland.Automation.Core.Services;

/// <summary>
/// 集中式配置管理器，确保所有窗口和工具执行器共享同一配置实例并实现配置变更全应用实时同步。
/// </summary>
public static class SettingsManager
{
    private static readonly object _lock = new();
    private static PluginSettings _settings = new();
    private static string? _settingsFilePath;

    public static event Action<PluginSettings>? SettingsChanged;

    public static PluginSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public static PluginSettings Load(string filePath)
    {
        lock (_lock)
        {
            _settingsFilePath = filePath;
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    _settings = JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
                    return _settings;
                }
            }
            catch { }

            _settings = new PluginSettings();
            return _settings;
        }
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
