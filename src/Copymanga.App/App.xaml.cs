using System.Windows;
using JmComic.App.Services;
using JmComic.App.Themes;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Services;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Copymanga;
using Microsoft.Extensions.DependencyInjection;

namespace Copymanga.App;

/// <summary>
/// 拷贝漫画独立版：复用 JmComic.App 的全部 UI / 下载引擎 / 本地库，
/// 只注册 copymanga 一个源，并使用独立的数据目录（config-copymanga）。
/// 通过 DLL 引用 JmComic.App（避免本机 SDK workload 解析问题），XAML 资源走 pack URI。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.DataDirName = "config-copymanga";
        AppPaths.MigrateLegacyData();
        ThemeManager.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton(new ConfigService(AppPaths.ConfigPath));

        // 只注册 copymanga 源
        services.AddSingleton<CopymangaHttpClient>();
        services.AddSingleton<CopymangaSource>();
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<CopymangaSource>());

        services.AddSingleton<SourceManager>();
        services.AddSingleton<AggregateSearchService>();
        services.AddSingleton<OnlineReaderService>();

        services.AddSingleton<DownloadManager>(sp => new DownloadManager(
            sp.GetServices<IComicSource>(), sp.GetRequiredService<ConfigService>()));
        // SessionService（MainWindow 依赖）需要 JmHttpClient；copymanga 版不显示登录区，
        // 但仍注册以保持 DI 完整性（JmHttpClient 构造不联网，仅占位）。
        services.AddSingleton<JmComic.Core.Http.JmHttpClient>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<LocalLibraryService>();
        services.AddSingleton<AlbumUpdateService>();
        services.AddSingleton<DownloadPanelViewModel>();
        JmComic.App.App.SetServices(services.BuildServiceProvider());

        var window = new JmComic.App.MainWindow();
        window.Title = "拷贝漫画下载器";
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        if (JmComic.App.App.Services.GetService<DownloadManager>() is { } downloadManager)
        {
            downloadManager.Dispose();
        }
    }
}
