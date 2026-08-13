using System.Security.Cryptography;
using System.Text;
using JmComic.Core.Http;
using Xunit;

namespace JmComic.Core.Tests;

/// <summary>
/// AES-256-ECB 解密黄金测试。
/// 用标准 PKCS7 加密生成密文（与服务端加密方式一致），
/// 断言 DecryptData 能解出原文（手动去填充逻辑）。
/// </summary>
public class DecryptDataTests
{
    private static string EncryptData(long ts, string plaintext)
    {
        var key = Encoding.UTF8.GetBytes(Md5Hex($"{ts}{JmConstants.AppDataSecret}"));
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(encrypted);
    }

    private static string Md5Hex(string input)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void Decrypt_Returns_OriginalJson()
    {
        var ts = 1_700_000_000L;
        const string plaintext = "{\"code\":200,\"name\":\"测试漫画\",\"id\":12345}";
        var ciphertext = EncryptData(ts, plaintext);

        var decrypted = JmHttpClient.DecryptData(ts, ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_Handles_EmptyJsonObject()
    {
        var ts = 1_700_000_000L;
        const string plaintext = "{}";
        var ciphertext = EncryptData(ts, plaintext);

        var decrypted = JmHttpClient.DecryptData(ts, ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_Handles_BlockAlignedData()
    {
        // 48 字节 = 3 个 AES 块，PKCS7 仍会补一整块
        var ts = 1_700_000_001L;
        var plaintext = "1234567890abcdef1234567890abcdef1234567890abcdef";
        var ciphertext = EncryptData(ts, plaintext);

        var decrypted = JmHttpClient.DecryptData(ts, ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_Key_Uses_Md5Hex_Of_TsPlusSecret()
    {
        // 验证密钥生成与 Rust 一致：md5_hex(ts + "185Hcomic3PAPP7R")
        var ts = 1_700_000_000L;
        var expectedKey = Md5Hex($"{ts}185Hcomic3PAPP7R");
        Assert.Equal(32, expectedKey.Length);
        Assert.Equal(expectedKey, Md5Hex($"{ts}{JmConstants.AppDataSecret}"));
    }
}
