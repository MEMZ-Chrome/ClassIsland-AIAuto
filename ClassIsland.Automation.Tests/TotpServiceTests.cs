using ClassIsland.Automation.Core.Services;
using Xunit;

namespace ClassIsland.Automation.Tests;

public class TotpServiceTests
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    [Fact]
    public void GenerateSecret_Returns32CharBase32()
    {
        var secret = TotpService.GenerateSecret();

        Assert.Equal(32, secret.Length);
        Assert.All(secret, c => Assert.Contains(c, Base32Alphabet));
    }

    [Fact]
    public void Base32_Roundtrip()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        Assert.Equal(bytes, TotpService.FromBase32(TotpService.ToBase32(bytes)));
    }

    [Fact]
    public void VerifyTotp_AcceptsCurrentCode()
    {
        var secret = TotpService.GenerateSecret();

        Assert.True(TotpService.VerifyTotp(secret, TotpService.ComputeCurrentTotp(secret)));
    }

    [Fact]
    public void VerifyTotp_RejectsWrongCode()
    {
        var secret = TotpService.GenerateSecret();
        var code = TotpService.ComputeCurrentTotp(secret);
        var wrong = code == "000000" ? "000001" : "000000";

        Assert.False(TotpService.VerifyTotp(secret, wrong));
    }

    [Fact]
    public void VerifyTotp_InvalidSecret_FailsClosed()
    {
        // 修复前非法 Base32 会解出空 key，ComputeHotp 返回 "000000"，
        // 于是 000000 会被误判为正确验证码。
        Assert.False(TotpService.VerifyTotp("!!!!!!", "000000"));
    }

    [Fact]
    public void FormatSecretForDisplay_GroupsByFour()
    {
        Assert.Equal("ABCD EFGH IJ", TotpService.FormatSecretForDisplay("abcdefghij"));
    }
}
