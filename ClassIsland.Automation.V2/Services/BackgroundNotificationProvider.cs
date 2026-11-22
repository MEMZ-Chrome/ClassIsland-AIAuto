using Avalonia.Threading;
using ClassIsland.Automation.Core.Services;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;

namespace ClassIsland.Automation.V2.Services;

/// <summary>
/// 通过 ClassIsland 提醒系统发送后台任务完成通知。
/// 走宿主管线可自动尊重用户的通知总开关与免打扰设置。
/// </summary>
[NotificationProviderInfo("6B5F4C31-8A2D-4E17-9C43-2F8D5A0E7B92", "IslandAgent 后台任务", "\uE276", "Agent 对话任务在后台完成时提醒")]
public class BackgroundNotificationProvider : NotificationProviderBase, IDisposable
{
    public BackgroundNotificationProvider()
    {
        ChatSessionManager.Instance.BackgroundNotificationRequested += OnBackgroundNotificationRequested;
    }

    private void OnBackgroundNotificationRequested(string title, string message)
    {
        Dispatcher.UIThread.Post(() => Notify(title, message));
    }

    public void Notify(string title, string message)
    {
        try
        {
            var singleLineTitle = AiClientService.SanitizeSingleLineNotification(title, maxLength: 24);
            if (string.IsNullOrWhiteSpace(singleLineTitle))
            {
                singleLineTitle = "IslandAgent 后台任务";
            }
            var singleLineMessage = AiClientService.SanitizeSingleLineNotification(message, maxLength: 24);

            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    singleLineTitle,
                    leftIcon: "\uE276",
                    hasRightIcon: false,
                    factory: x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(2);
                    }),
                OverlayContent = string.IsNullOrWhiteSpace(singleLineMessage)
                    ? null
                    : NotificationContent.CreateSimpleTextContent(
                        singleLineMessage,
                        factory: x =>
                        {
                            x.Duration = TimeSpan.FromSeconds(5);
                        })
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CI自动化] 后台通知发送失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        ChatSessionManager.Instance.BackgroundNotificationRequested -= OnBackgroundNotificationRequested;
    }
}
