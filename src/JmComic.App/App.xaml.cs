using System.Reflection;
using System.Windows;
using JmComic.App.Services;
using JmComic.App.Themes;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Logging;
using JmComic.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App;

/// <summary>
/// 应用入口：注册 DI 服务（JmHttpClient / ConfigService / DownloadManager / Session / 下载面板），
/// 启动即恢复主题偏好、注册全局异常钩子并写入滚动日志，退出时释放下载资源。
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

        // 滚动文件日志：config\logs 目录，按天一个文件，保留 7 天
        var logger = new FileLogger(System.IO.Path.Combine(AppPaths.AppDataDir, "logs"));

        var services = new ServiceCollection();
        services.AddSingleton(new ConfigService(AppPaths.ConfigPath));
        services.AddSingleton<JmHttpClient>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<LocalLibraryService>();
        services.AddSingleton<AlbumUpdateService>();
        services.AddSingleton<DownloadPanelViewModel>();
        services.AddSingleton<ILogger>(logger);
        Services = services.BuildServiceProvider();

        // 全局异常钩子必须在创建窗口前注册
        RegisterGlobalExceptionHandlers(logger);
        logger.Info($"应用启动，运行时 {Environment.Version}，程序集版本 {Assembly.GetExecutingAssembly().GetName().Version}，日志目录 {logger.LogDirectory}");

        // 用配置中保存的凭据静默恢复登录态
        _ = Task.Run(async () =>
        {
            try
            {
                var ok = await Services.GetRequiredService<SessionService>().TryRestoreAsync();
                logger.Info(ok ? "登录状态恢复成功" : "登录状态恢复失败（未保存凭据或凭据无效）");
            }
            catch (Exception ex)
            {
                logger.Warn($"登录状态恢复异常: {ex.Message}");
            }
        });

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 全局异常钩子：UI 线程未处理异常弹窗提示并可选继续运行；
    /// 后台任务未观察异常只记日志不崩进程；致命异常尽力留痕后退出。
    /// </summary>
    private void RegisterGlobalExceptionHandlers(ILogger logger)
    {
        DispatcherUnhandledException += (_, e) =>
        {
            logger.Error("UI 线程未处理异常", e.Exception);
            TryCopyExceptionToClipboard(e.Exception);

            var message = $"程序发生未处理异常：\n{e.Exception.GetBaseException().Message}\n\n" +
                          $"错误详情已复制到剪贴板。\n日志目录：{logger.LogDirectory}\n\n是否继续运行？";
            var result = MessageBox.Show(message, "禁漫天堂下载器 - 错误",
                MessageBoxButton.YesNo, MessageBoxImage.Error);
            e.Handled = true;
            if (result != MessageBoxResult.Yes)
            {
                Shutdown(1);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error("后台任务未观察异常", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.Error("致命未处理异常", e.ExceptionObject as Exception);
            MessageBox.Show($"程序发生致命错误，即将退出。\n日志目录：{logger.LogDirectory}",
                "禁漫天堂下载器 - 致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    private static void TryCopyExceptionToClipboard(Exception ex)
    {
        try
        {
            Clipboard.SetText(ex.ToString());
        }
        catch
        {
            // 剪贴板不可用时忽略
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        if (Services.GetService<DownloadManager>() is { } downloadManager)
        {
            downloadManager.Dispose();
        }
        if (Services.GetService<ILogger>() is { } logger)
        {
            logger.Info("应用退出");
            (logger as IDisposable)?.Dispose();
        }
    }
}
