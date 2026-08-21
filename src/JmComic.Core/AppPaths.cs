namespace JmComic.Core;

public static class AppPaths
{
    public static string DataDirName { get; set; } = "config";
    public static string AppDataDir =>
        Path.Combine(AppContext.BaseDirectory, DataDirName);
    public static string LegacyDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jmcomic-downloader");
    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string LocalLibraryCachePath => Path.Combine(AppDataDir, "local-library-cache.json");
    public static string DownloadHistoryPath => Path.Combine(AppDataDir, "download-history.json");
    public static string NovelHistoryPath => Path.Combine(AppDataDir, "novel-history.json");
    public static string NovelIndexPath => Path.Combine(AppDataDir, "novel-index.json");
    public static string NovelReaderSettingsPath => Path.Combine(AppDataDir, "novel-reader-settings.json");
    public static void MigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataDir)) return;
            Directory.CreateDirectory(AppDataDir);
            foreach (var name in new[] { "config.json", "local-library-cache.json", "reading-progress.json", "theme.json", "novel-history.json", "novel-reader-settings.json" })
            {
                var source = Path.Combine(LegacyDataDir, name);
                var target = Path.Combine(AppDataDir, name);
                if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
            }
        }
        catch { }
    }
}
