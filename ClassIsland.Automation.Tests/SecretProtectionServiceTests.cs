using System.Text.Json;
using ClassIsland.Automation.Core.Models;
using ClassIsland.Automation.Core.Services;
using Xunit;

namespace ClassIsland.Automation.Tests;

public class SecretProtectionServiceTests
{
    [Fact]
    public void EncryptDecrypt_Roundtrip()
    {
        var encrypted = SecretProtectionService.Encrypt("sk-test-1234567890");

        Assert.StartsWith("enc:v1:", encrypted);
        Assert.Equal("sk-test-1234567890", SecretProtectionService.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_DoesNotDoubleEncrypt()
    {
        var encrypted = SecretProtectionService.Encrypt("secret");

        Assert.Equal(encrypted, SecretProtectionService.Encrypt(encrypted));
    }

    [Fact]
    public void Decrypt_PlaintextPassesThrough()
    {
        Assert.Equal("plain", SecretProtectionService.Decrypt("plain"));
    }

    [Fact]
    public void Decrypt_CorruptedCiphertext_IsPreserved()
    {
        // 修复前返回空串，下一次保存会用空值覆盖掉原密文，造成密钥静默永久丢失。
        const string corrupted = "enc:v1:not-base64!!!";

        Assert.Equal(corrupted, SecretProtectionService.Decrypt(corrupted));
    }

    [Fact]
    public void ApiKey_IsEncryptedWhenSerialized()
    {
        var provider = new AiProviderConfig { ApiKey = "sk-plain" };

        var json = JsonSerializer.Serialize(provider);

        Assert.Contains("enc:v1:", json);
        Assert.DoesNotContain("sk-plain", json);
    }

    [Fact]
    public void ApiKey_RoundtripsThroughSerialization()
    {
        var provider = new AiProviderConfig { ApiKey = "sk-plain" };

        var restored = JsonSerializer.Deserialize<AiProviderConfig>(JsonSerializer.Serialize(provider));

        Assert.NotNull(restored);
        Assert.Equal("sk-plain", restored!.ApiKey);
    }
}
