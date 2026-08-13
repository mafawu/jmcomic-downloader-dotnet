using System.Text.Json;
using JmComic.Core.Services;

namespace JmComic.Core.Tests;

public class ConfigServiceTests
{
    private static string NewConfigPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    private static void DeleteConfig(string path)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    [Fact]
    public void Save_And_Reload_RoundTrips_Credentials()
    {
        var path = NewConfigPath();
        try
        {
            var service = new ConfigService(path);
            service.Current.Username = "user";
            service.Current.Password = "p@ss";
            service.Current.TitleTranslate.ApiKey = "sk-test";
            service.Save();

            var reloaded = new ConfigService(path);
            Assert.Equal("user", reloaded.Current.Username);
            Assert.Equal("p@ss", reloaded.Current.Password);
            Assert.Equal("sk-test", reloaded.Current.TitleTranslate.ApiKey);
        }
        finally
        {
            DeleteConfig(path);
        }
    }

    [Fact]
    public void Save_Encrypts_Credentials_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var path = NewConfigPath();
        try
        {
            var service = new ConfigService(path);
            service.Current.Password = "topsecret";
            service.Current.TitleTranslate.ApiKey = "sk-topsecret";
            service.Save();

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.StartsWith("DPAPI:v1:", doc.RootElement.GetProperty("password").GetString());
            Assert.StartsWith("DPAPI:v1:", doc.RootElement.GetProperty("titleTranslate").GetProperty("apiKey").GetString());
        }
        finally
        {
            DeleteConfig(path);
        }
    }

    [Fact]
    public void Load_Migrates_Legacy_Plaintext_Credentials_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var path = NewConfigPath();
        try
        {
            File.WriteAllText(path,
                "{\"username\":\"legacy\",\"password\":\"plain\",\"titleTranslate\":{\"enabled\":true,\"apiKey\":\"sk-legacy\"}}");
            var service = new ConfigService(path);
            Assert.Equal("plain", service.Current.Password);
            Assert.Equal("sk-legacy", service.Current.TitleTranslate.ApiKey);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.StartsWith("DPAPI:v1:", doc.RootElement.GetProperty("password").GetString());
            Assert.StartsWith("DPAPI:v1:", doc.RootElement.GetProperty("titleTranslate").GetProperty("apiKey").GetString());
        }
        finally
        {
            DeleteConfig(path);
        }
    }
}
