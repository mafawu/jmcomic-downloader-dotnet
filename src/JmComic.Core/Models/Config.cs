using System.Text.Json.Serialization;

namespace JmComic.Core.Models;

public class Config
{
    [JsonPropertyName("apiDomain")] public string ApiDomain { get; set; } = "";

    /// <summary>接口域名列表（优先于 apiDomain 使用）；请求失败时自动轮换到下一个可用域名。</summary>
    [JsonPropertyName("apiDomains")] public List<string> ApiDomains { get; set; } = new();
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("downloadDir")] public string DownloadDir { get; set; } = "";
    [JsonPropertyName("downloadFormat")] public DownloadFormat DownloadFormat { get; set; } = DownloadFormat.Jpeg;
    [JsonPropertyName("localDirs")] public List<string> LocalDirs { get; set; } = new();
    [JsonPropertyName("titleTranslate")] public TitleTranslateOptions TitleTranslate { get; set; } = new();
}

/// <summary>标题中文名翻译配置（OpenAI 兼容 Chat Completions 接口，如 OpenAI / DeepSeek / 通义等）。</summary>
public class TitleTranslateOptions
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    [JsonPropertyName("apiKey")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "gpt-4o-mini";
}