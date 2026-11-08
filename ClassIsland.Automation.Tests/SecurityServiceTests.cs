using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using Xunit;

namespace ClassIsland.Automation.Tests;

public class SecurityServiceTests
{
    [Fact]
    public void Password_Roundtrip()
    {
        var settings = new PluginSettings();

        SecurityService.SetPassword(settings, "1234");
        Assert.True(SecurityService.HasPassword(settings));
        Assert.True(SecurityService.VerifyPassword(settings, "1234"));
        Assert.False(SecurityService.VerifyPassword(settings, "4321"));

        SecurityService.SetPassword(settings, "");
        Assert.False(SecurityService.HasPassword(settings));
    }

    [Fact]
    public void PendingTotp_DoesNotLockUntilActivated()
    {
        var settings = new PluginSettings();

        SecurityService.StartSetupTotp(settings);
        Assert.True(SecurityService.HasPendingTotp(settings));
        // 关键安全设计：暂存密钥在验证通过前绝不能让页面进入锁定状态。
        Assert.False(SecurityService.IsActiveProtectionEnabled(settings));

        var code = TotpService.ComputeCurrentTotp(settings.PendingTotpSecret);
        Assert.True(SecurityService.VerifyAndActivatePendingTotp(settings, code));
        Assert.True(SecurityService.IsActiveProtectionEnabled(settings));
        Assert.False(SecurityService.HasPendingTotp(settings));

        var activeCode = TotpService.ComputeCurrentTotp(settings.TotpSecret);
        Assert.Equal(AccessAuthResult.SuccessActiveTotp, SecurityService.VerifyAccess(settings, activeCode));
    }

    [Fact]
    public void VerifyAccess_WithoutProtection_AlwaysSucceeds()
    {
        var settings = new PluginSettings();

        Assert.Equal(AccessAuthResult.SuccessPassword, SecurityService.VerifyAccess(settings, ""));
        Assert.Equal(AccessAuthResult.SuccessPassword, SecurityService.VerifyAccess(settings, "whatever"));
    }

    [Fact]
    public void VerifyAccess_WrongPassword_Fails()
    {
        var settings = new PluginSettings();
        SecurityService.SetPassword(settings, "correct");

        Assert.Equal(AccessAuthResult.Failed, SecurityService.VerifyAccess(settings, "wrong"));
    }
}
