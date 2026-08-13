using JmComic.Core.Http;
using JmComic.Core.Services;

namespace JmComic.Core.Tests;

public class ApiDomainPoolTests
{
    private static ConfigService ConfigWith(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-domain-pool-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, json);
        return new ConfigService(path);
    }

    [Fact]
    public void Defaults_To_Builtin_Domains_When_Config_Empty()
    {
        var pool = new ApiDomainPool(ConfigWith("{}"));
        Assert.Equal(JmConstants.ApiDomains, pool.GetDomains());
    }

    [Fact]
    public void Falls_Back_To_Legacy_Single_ApiDomain()
    {
        var pool = new ApiDomainPool(ConfigWith("{\"apiDomain\":\"legacy.example.com\"}"));
        Assert.Equal(new[] { "legacy.example.com" }, pool.GetDomains());
    }

    [Fact]
    public void ApiDomains_Takes_Precedence_Over_Legacy_ApiDomain()
    {
        var pool = new ApiDomainPool(ConfigWith(
            "{\"apiDomain\":\"legacy.example.com\",\"apiDomains\":[\"a.example.com\",\"b.example.com\"]}"));
        Assert.Equal(new[] { "a.example.com", "b.example.com" }, pool.GetDomains());
    }

    [Fact]
    public void Normalizes_And_Deduplicates_Domains()
    {
        var pool = new ApiDomainPool(ConfigWith(
            "{\"apiDomains\":[\"https://A.example.com/\",\"a.example.com\",\" b.example.com \",\"\",\"b.example.com\"]}"));
        Assert.Equal(new[] { "a.example.com", "b.example.com" }, pool.GetDomains());
    }

    [Fact]
    public void Next_Rotates_And_Remembers_Last_Success()
    {
        var pool = new ApiDomainPool(ConfigWith(
            "{\"apiDomains\":[\"a.example.com\",\"b.example.com\",\"c.example.com\"]}"));

        Assert.Equal("a.example.com", pool.Next());
        pool.MarkSuccess("a.example.com");
        Assert.Equal("b.example.com", pool.Next());
        pool.MarkSuccess("b.example.com");
        Assert.Equal("c.example.com", pool.Next());
        pool.MarkSuccess("c.example.com");
        Assert.Equal("a.example.com", pool.Next());
    }

    [Fact]
    public void Failed_Domain_Is_Skipped_Until_Cooldown_Expires()
    {
        var pool = new ApiDomainPool(
            ConfigWith("{\"apiDomains\":[\"a.example.com\",\"b.example.com\"]}"),
            cooldown: TimeSpan.FromMilliseconds(80));

        Assert.Equal("a.example.com", pool.Next());
        pool.MarkFailed("a.example.com");

        Assert.Equal("b.example.com", pool.Next());
        Assert.Equal("b.example.com", pool.Next());

        Thread.Sleep(120);
        Assert.Equal("a.example.com", pool.Next());
    }

    [Fact]
    public void Config_Change_Resets_Pointer_And_Cooldown()
    {
        var config = ConfigWith("{\"apiDomains\":[\"a.example.com\",\"b.example.com\"]}");
        var pool = new ApiDomainPool(config);

        Assert.Equal("a.example.com", pool.Next());
        pool.MarkFailed("a.example.com");
        Assert.Equal("b.example.com", pool.Next());

        config.Current.ApiDomains = new List<string> { "c.example.com" };

        Assert.Equal(new[] { "c.example.com" }, pool.GetDomains());
        Assert.Equal("c.example.com", pool.Next());
    }
}