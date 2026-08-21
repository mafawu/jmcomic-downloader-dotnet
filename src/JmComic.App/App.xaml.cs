using System.Text;
using System.Windows;
using JmComic.App.Services;
using JmComic.App.Themes;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Services;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Copymanga;
using JmComic.Core.Sources.Hitomi;
using JmComic.Core.Sources.Jm;
using JmComic.Core.Sources.Baozimh;
using JmComic.Core.Sources.Wnacg;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App;

/// <summary>
/// 应用入口：注册 DI 服务（多内容源 / ConfigService / DownloadManager / Session / 下载面板），
/// 启动即恢复主题偏好，退出时释放下载资源。
/// 派生应用（如 Copymanga.App）可覆盖 <see cref="DataDirName"/> 与 <see cref="ConfigureSources"/>
/// 以产出独立的数据目录与独立的源集合。
/// </summary>
public partial class App : Application
{
    /// <summary>全局服务容器：主程序与派生 exe（copymanga 版）共用此入口，供各视图/服务解析依赖。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>供派生 exe（不继承本类的组合式应用）注入服务容器。</summary>
    public static void SetServices(IServiceProvider services) => Services = services;

    /// <summary>数据目录名（派生应用覆盖为独立目录，避免与主程序共享本地库）。</summary>
    protected virtual string DataDirName => "config";

    /// <summary>是否注册禁漫登录会话服务（免登录的 copymanga 版覆盖为 false）。</summary>
    protected virtual bool RegisterSessionService => true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RunStartup(e);
    }

    /// <summary>
    /// 实际启动逻辑：初始化主题、注册 DI、显示主窗口。
    /// 供派生应用（不继承本类、改用组合方式的独立 exe）复用。
    /// </summary>
    protected void RunStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { try { var ex = e.ExceptionObject as Exception; System.IO.File.AppendAllText(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "jm_crash.log"), "[" + DateTime.Now + "] AppDomain " + ex + "\r\n"); } catch { } };
        DispatcherUnhandledException += (s, e) => { try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "jm_crash.log"), "[" + DateTime.Now + "] Dispatcher " + e.Exception + "\r\n"); } catch { } e.Handled = true; System.Windows.MessageBox.Show("发生未处理异常：" + e.Exception.Message + "\r\n\r\n已记录到桌面 jm_crash.log", "崩溃", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); };
        AppPaths.DataDirName = DataDirName;

        // 把旧版 %APPDATA% 数据迁移到程序同目录数据文件夹
        AppPaths.MigrateLegacyData();

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ThemeManager.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton(new ConfigService(AppPaths.ConfigPath));

        // 内容源：派生应用可覆盖此方法以注册不同的源集合
        ConfigureSources(services);

        services.AddSingleton<SourceManager>();
        services.AddSingleton<AggregateSearchService>();
        services.AddSingleton<OnlineReaderService>();

        services.AddSingleton<DownloadManager>(sp => new DownloadManager(
            sp.GetServices<IComicSource>(), sp.GetRequiredService<ConfigService>()));
        if (RegisterSessionService)
        {
            services.AddSingleton<SessionService>();
        }
        services.AddSingleton<LocalLibraryService>();
        services.AddSingleton<NovelIndexService>();
        services.AddSingleton<NovelReadingHistoryService>();
        services.AddSingleton<NovelReaderSettingsService>();
        services.AddSingleton<AlbumUpdateService>();
        services.AddSingleton<DownloadPanelViewModel>();
        Services = services.BuildServiceProvider();

        // 用配置中保存的凭据静默恢复登录态（仅注册了会话服务的应用）
        _ = Task.Run(async () =>
        {
            try
            {
                if (Services.GetService<SessionService>() is { } session)
                {
                    await session.TryRestoreAsync();
                }
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

    /// <summary>注册全部内容源。派生应用可覆盖为只注册自己的源。</summary>
    protected virtual void ConfigureSources(IServiceCollection services)
    {
        services.AddSingleton<JmHttpClient>();
        services.AddSingleton<JmSource>();
        services.AddSingleton<WnacgHttpClient>();
        services.AddSingleton<WnacgSource>();
        services.AddSingleton<HitomiHttpClient>();
        services.AddSingleton<HitomiGgResolver>();
        services.AddSingleton<HitomiGalleryClient>();
        services.AddSingleton<HitomiSource>();
        services.AddSingleton<BaozimhHttpClient>();
        services.AddSingleton<BaozimhSource>();
        services.AddSingleton<CopymangaHttpClient>();
        services.AddSingleton<CopymangaSource>();
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<JmSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<WnacgSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<HitomiSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<BaozimhSource>());
        services.AddSingleton<IComicSource>(sp => sp.GetRequiredService<CopymangaSource>());
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



