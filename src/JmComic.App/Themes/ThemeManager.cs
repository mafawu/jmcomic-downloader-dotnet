using System.IO;
using System.Text.Json;
using System.Windows;
using JmComic.Core;

namespace JmComic.App.Themes;

/// <summary>
/// 主题管理：动态切换浅色/深色配色字典（Colors.xaml ↔ DarkColors.xaml），
/// 并将偏好持久化到 AppDataDir/theme.json。
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.AppDataDir, "theme.json");

    public static bool IsDark { get; private set; }

    public static void Initialize()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                IsDark = JsonSerializer.Deserialize<bool>(File.ReadAllText(SettingsPath));
            }
        }
        catch
        {
            IsDark = false;
        }
        Apply(IsDark);
    }

    public static void Toggle() => Apply(!IsDark);

    public static void Apply(bool isDark)
    {
        IsDark = isDark;
        var app = Application.Current;
        if (app is not null)
        {
            var merged = app.Resources.MergedDictionaries;
            for (var i = 0; i < merged.Count; i++)
            {
                var source = merged[i].Source?.OriginalString;
                if (source is not null &&
                    (source.EndsWith("Colors.xaml") || source.EndsWith("DarkColors.xaml")))
                {
                    merged[i] = new ResourceDictionary
                    {
                        Source = new Uri("Themes/" + (isDark ? "DarkColors.xaml" : "Colors.xaml"), UriKind.Relative),
                    };
                    break;
                }
            }
        }
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(IsDark));
        }
        catch
        {
            // 忽略持久化失败
        }
    }
}
