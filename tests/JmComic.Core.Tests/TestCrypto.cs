using System.Security.Cryptography;
using System.Text;
using JmComic.Core;

namespace JmComic.Core.Tests;

/// <summary>测试用加密工具：与服务端一致的 AES-256-ECB 加密，用于构造可解密的 API 响应。</summary>
internal static class TestCrypto
{
    public static string Md5Hex(string input)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(input)));

    public static string EncryptData(long ts, string plaintext)
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
}