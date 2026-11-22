using ClassIsland.Automation.Core.Services;
using Xunit;

namespace ClassIsland.Automation.Tests;

public class NotificationSummaryTests
{
    [Fact]
    public void SanitizeSingleLineNotification_StripsMarkdownQuotesAndNewlines()
    {
        var input = "```text\n“已将明天下午数学与物理互换。”\n如需继续调整请告诉我。\n```";
        var result = AiClientService.SanitizeSingleLineNotification(input);

        Assert.Equal("已将明天下午数学与物理互换", result);
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("\r", result);
    }

    [Fact]
    public void SanitizeSingleLineNotification_SkipsDividerAndStripsHeading()
    {
        var input = "---\n### **调课已完成**\n1. 数学与物理已互换";
        var result = AiClientService.SanitizeSingleLineNotification(input);

        Assert.Equal("调课已完成", result);
    }

    [Fact]
    public void SanitizeSingleLineNotification_TruncatesOverlongSingleLine()
    {
        var input = "这是一段明显超过顶部状态栏单行完整显示长度限制的超长通知结果说明文本";
        var result = AiClientService.SanitizeSingleLineNotification(input, maxLength: 20);

        Assert.Equal(20, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void SanitizeSingleLineNotification_HandlesNullOrWhitespace()
    {
        Assert.Equal("", AiClientService.SanitizeSingleLineNotification(null));
        Assert.Equal("", AiClientService.SanitizeSingleLineNotification("   \r\n\t  "));
    }
}
