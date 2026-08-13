using System.Windows;
using JmComic.App.Services;
using JmComic.App.Themes;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Services;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Hitomi;
using JmComic.Core.Sources.Jm;
using JmComic.Core.Sources.Wnacg;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App;

/// <summary>
/// 应用入口：注册 DI 服务（多内容源 / ConfigService / DownloadManager / Session / 下载面板），
/// 启动即恢复主题偏好，退出时释放下载资源。
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 把旧版 %APPDATA% 数据迁移到程序同目录 config 文件夹
        AppPaths.MigrateLegacyData();

        ThemeManager.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton(new ConfigService(AppPaths.ConfigPath));

        // 内容源：枚举 IComicSource 即可拿到全部源
        services.AddSingleton<JmHttpClient>();
        services.AddSingleton<JmSource>();
        services.AddSingleton<WnacgHttpClient>();
        services.AddSingleton<WnacgSource>();
        services.AddSingleton<HitomiHttpClient>();
        services.AddSingleton<HitomiGgResolver>();
        services.AddSingleton<HitomiGalleryClient>();
        services.AddSingleton<HitomiSource>();
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<JmSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<WnacgSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<HitomiSource>());
        services.AddSingleton<SourceManager>();
        services.AddSingleton<AggregateSearchService>();
        services.AddSingleton<OnlineReaderService>();

        services.AddSingleton<DownloadManager>(sp => new DownloadManager(
            sp.GetServices<IComicSource>(), sp.GetRequiredService<ConfigService>()));
        services.AddSingleton<SessionService>();
        services.AddSingleton<LocalLibraryService>();
        services.AddSingleton<AlbumUpdateService>();
        services.AddSingleton<DownloadPanelViewModel>();
        Services = services.BuildServiceProvider();

        // 用配置中保存的凭据静默恢复登录态
        _ = Task.Run(async () =>
        {
            try
            {
                await Services.GetRequiredService<SessionService>().TryRestoreAsync();
            }
            catch
            {
                // 静默失败，不打扰用户
            }
        });

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        if (Services.GetService<DownloadManager>() is { } downloadManager)
        {
            downloadManager.Dispose();
        }
    }
}







