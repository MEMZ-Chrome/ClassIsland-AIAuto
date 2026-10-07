using Avalonia.Controls;
using ClassIsland.Automation.V2.Views;
using ClassIsland.Automation.V2.Views.SettingsPages;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.Automation.V2;

[PluginEntrance]
public class Plugin : PluginBase
{
    public static Plugin? Instance { get; private set; }
    private ChatWindow? _chatWindow;

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        Instance = this;

        // 1. 注册设置页面
        services.AddSettingsPage<AutomationSettingsPage>();

        // 2. 注册托盘右键菜单项 “AI助手”
        AppBase.Current.AppStarted += (_, _) =>
        {
            RegisterTrayMenu();
        };
    }

    private void RegisterTrayMenu()
    {
        try
        {
            var taskBarIconService = AppBase.Current.Services.GetService<ITaskBarIconService>();
            var profileService = AppBase.Current.Services.GetService<IProfileService>();
            var uriService = AppBase.Current.Services.GetService<IUriNavigationService>();

            if (taskBarIconService == null) return;

            var menuItem = new NativeMenuItem("AI助手");
            menuItem.Click += (_, _) =>
            {
                if (_chatWindow == null || !_chatWindow.IsVisible)
                {
                    _chatWindow = new ChatWindow(profileService, uriService);
                    _chatWindow.Show();
                }
                else
                {
                    _chatWindow.Activate();
                }
            };

            taskBarIconService.MoreOptionsMenuItems.Add(menuItem);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CI自动化] 托盘菜单注册异常: {ex.Message}");
        }
    }
}
