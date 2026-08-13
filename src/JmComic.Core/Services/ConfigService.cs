using System.Text.Json;
using JmComic.Core.Models;

namespace JmComic.Core.Services;

/// <summary>
/// 配置文件读写服务。config.json 结构与原 Tauri 版保持一致，
/// 老用户可直接沿用已有配置。
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;

    public ConfigService(string configPath)
    {
        _configPath = configPath;
        Current = Load();
    }

    public Config Current { get; private set; }

    private Config Load()
    {
        var defaultConfig = new Config
        {
            DownloadDir = Path.Combine(AppPaths.AppDataDir, "漫画下载"),
            DownloadFormat = DownloadFormat.Jpeg,
            LocalDirs = new List<string> { Path.Combine(AppPaths.AppDataDir, "漫画下载") },
        };
        if (!File.Exists(_configPath))
        {
            Save(defaultConfig);
            return defaultConfig;
        }
        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<Config>(json);
            if (config is null)
            {
                return defaultConfig;
            }
            if (string.IsNullOrEmpty(config.DownloadDir))
            {
                config.DownloadDir = defaultConfig.DownloadDir;
            }
            if (config.LocalDirs is null || config.LocalDirs.Count == 0)
            {
                config.LocalDirs = new List<string> { config.DownloadDir };
            }
            return config;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取配置失败，使用默认配置: {ex.Message}");
            return defaultConfig;
        }
    }

    public void Save()
    {
        Save(Current);
    }

    public void Save(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}


