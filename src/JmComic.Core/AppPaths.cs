namespace JmComic.Core;

/// <summary>
/// 应用数据目录：程序同目录下的数据文件夹，所有本地数据 JSON 均存放于此
/// （config.json / local-library-cache.json / reading-progress.json / theme.json）。
/// 目录名由 <see cref="DataDirName"/> 决定，便于同一代码库产出多个独立 exe（如 jmcomic / copymanga）。
/// </summary>
public static class AppPaths
{
    /// <summary>数据目录名（默认 "config"；派生应用可覆盖为 "config-copymanga" 等）。</summary>
    public static string DataDirName { get; set; } = "config";

    /// <summary>数据目录（程序所在目录 \ {DataDirName}）。</summary>
    public static string AppDataDir =>
        Path.Combine(AppContext.BaseDirectory, DataDirName);

    /// <summary>旧版数据目录（%APPDATA%\jmcomic-downloader），用于首次启动迁移。</summary>
    public static string LegacyDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jmcomic-downloader");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    /// <summary>本地漫画库缓存文件（增量扫描复用，避免每次全量枚举）。</summary>
    public static string LocalLibraryCachePath => Path.Combine(AppDataDir, "local-library-cache.json");

    /// <summary>下载历史记录文件（下载队列的持久化历史，重启后恢复显示）。</summary>
    public static string DownloadHistoryPath => Path.Combine(AppDataDir, "download-history.json");

    /// <summary>把旧版 %APPDATA%\jmcomic-downloader 下的数据文件复制到数据目录（目标已存在则跳过）。</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataDir))
            {
                return;
            }
            Directory.CreateDirectory(AppDataDir);
            foreach (var name in new[] { "config.json", "local-library-cache.json", "reading-progress.json", "theme.json" })
            {
                var source = Path.Combine(LegacyDataDir, name);
                var target = Path.Combine(AppDataDir, name);
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Copy(source, target);
                }
            }
        }
        catch
        {
            // 迁移失败不阻塞启动
        }
    }
}
