using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JmComic.App;
using JmComic.App.Services;
using JmComic.App.Views;
using JmComic.Core.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace ReproReader;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // WPF 正常启动时由 Application 安装 Dispatcher 同步上下文；手动宿主需自行安装
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var tmp = Path.Combine(Path.GetTempPath(), "jm-repro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        // 1) 本地 HTTP 服务提供测试图片
        var port = RandomPort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(() => ServeLoop(listener, tmp));

        var urls = new List<string>();
        var sizes = new[] { (1600, 2400), (1600, 2400), (1600, 2400), (800, 1200) };
        for (var i = 0; i < sizes.Length; i++)
        {
            var (w, h) = sizes[i];
            var path = Path.Combine(tmp, $"img{i}.png");
            using (var bmp = new System.Drawing.Bitmap(w, h))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var bg = i switch { 0 => System.Drawing.Color.LightCoral, 1 => System.Drawing.Color.LightGreen, 2 => System.Drawing.Color.LightBlue, _ => System.Drawing.Color.LightGoldenrodYellow };
                g.Clear(bg);
                using var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 12f);
                g.DrawRectangle(pen, 30, 30, w - 60, h - 60);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            urls.Add($"http://localhost:{port}/img{i}.png");
        }

        // 2) 资源 + DI（不实例化 JmComic.App.App，避免其 OnStartup 以相对 pack URI 加载主题）
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/JmComic.App;component/Themes/Colors.xaml"),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/JmComic.App;component/Themes/Styles.xaml"),
        });
        var services = new ServiceCollection();
        services.AddSingleton<OnlineReaderService>();
        typeof(App).GetProperty(nameof(App.Services))!.SetValue(null, services.BuildServiceProvider());

        // 3) 假源 + 视图
        var fake = new FakeSource(urls.ToArray());
        var window = new Window { Title = "Repro", Width = 1100, Height = 900, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var view = new OnlineReaderView(fake, new[]
        {
            new Chapter { Id = "c1", Title = "第1话", ComicId = "1", ComicTitle = "测试漫画", SourceId = "fake" },
        }, 0);
        window.Content = view;
        window.Show();
        Pump(window);

        // 4) 等图片全部加载
        for (var i = 0; i < 200; i++)
        {
            Pump(window);
            Thread.Sleep(100);
            var hosts = GetField(view, "_hosts") as IList;
            if (hosts is { Count: > 0 } && AllLoaded(hosts)) break;
        }

        Console.WriteLine("== initial (FitWidth, pageMode) ==");
        DumpState(view);
        Save(window, tmp, "01_fitwidth");

        Click(view, "FitHeight_Click");
        Pump(window);
        Console.WriteLine("== FitHeight ==");
        DumpState(view);
        Save(window, tmp, "02_fitheight");

        Click(view, "FitPage_Click");
        Pump(window);
        Console.WriteLine("== FitPage ==");
        DumpState(view);
        Save(window, tmp, "03_fitpage");

        Click(view, "ActualSize_Click");
        Pump(window);
        Console.WriteLine("== Actual ==");
        DumpState(view);
        Save(window, tmp, "04_actual");

        Console.WriteLine($"OUTPUT_DIR={tmp}");
        Thread.Sleep(300);
        listener.Stop();
        app.Shutdown();
    }

    private static void ServeLoop(HttpListener listener, string dir)
    {
        while (listener.IsListening)
        {
            try
            {
                var ctx = listener.GetContext();
                var file = Path.Combine(dir, ctx.Request.Url!.AbsolutePath.TrimStart('/'));
                if (File.Exists(file))
                {
                    var bytes = File.ReadAllBytes(file);
                    ctx.Response.ContentType = "image/png";
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static bool AllLoaded(IList hosts)
    {
        foreach (var h in hosts)
        {
            if (GetField(h, "IsLoaded") is not true) return false;
        }
        return true;
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(30);
        }
    }

    private static void DumpState(OnlineReaderView view)
    {
        var hosts = GetField(view, "_hosts") as IList;
        var scroller = GetField(view, "Scroller");
        var vw = scroller?.GetType().GetProperty("ViewportWidth")?.GetValue(scroller);
        var vh = scroller?.GetType().GetProperty("ViewportHeight")?.GetValue(scroller);
        Console.WriteLine($"  viewWidth={GetField(view, "_viewWidth")} viewHeight={GetField(view, "_viewHeight")} scrollViewportW={vw} scrollViewportH={vh} zoom={GetField(view, "_zoom")} fitMode={GetField(view, "_fitMode")} pageMode={GetField(view, "_pageMode")}");
        var statePanel = GetField(view, "ChapterStatePanel");
        var stateText = GetField(view, "ChapterStateText");
        var vis = statePanel?.GetType().GetProperty("Visibility")?.GetValue(statePanel);
        var txt = stateText?.GetType().GetProperty("Text")?.GetValue(stateText);
        Console.WriteLine($"  chapterState: vis={vis} text={txt}");
        if (hosts is not null)
        {
            for (var i = 0; i < hosts.Count; i++)
            {
                var host = hosts[i]!;
                var root = GetMember(host, "Root");
                Console.WriteLine($"  page{i}: PixelW={GetField(host, "PixelWidth")} PixelH={GetField(host, "PixelHeight")} HeightEstimate={GetField(host, "HeightEstimate")} loaded={GetField(host, "IsLoaded")} rootW={GetMember(root, "Width")} rootH={GetMember(root, "Height")} rootActualH={GetMember(root, "ActualHeight")}");
            }
        }
    }

    private static void Click(OnlineReaderView view, string method)
    {
        view.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { null!, null! });
    }

    private static void Save(Window window, string dir, string name)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var w = (int)Math.Max(1, window.ActualWidth);
        var h = (int)Math.Max(1, window.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(fs);
        Console.WriteLine($"  saved {name}.png ({w}x{h})");
    }

    private static object? GetField(object target, string name)
    {
        var t = target.GetType();
        while (t is not null)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f is not null) return f.GetValue(target);
            t = t.BaseType;
        }
        return null;
    }

    private static object? GetMember(object? target, string name)
    {
        if (target is null) return null;
        var t = target.GetType();
        while (t is not null)
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (p is not null) return p.GetValue(target);
            t = t.BaseType;
        }
        return GetField(target, name);
    }

    private static int RandomPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private sealed class FakeSource : IComicSource
    {
        private readonly string[] _urls;
        public FakeSource(string[] urls) { _urls = urls; }
        public ComicSourceInfo Info { get; } = new()
        {
            Id = "fake",
            DisplayName = "假源",
            MaxImageConcurrency = 2,
            MaxUrlFetchConcurrency = 1,
            MaxChapterConcurrency = 1,
        };
        public Task<SearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ComicDetail> GetComicAsync(string comicId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ImagePage>> GetChapterImagesAsync(Chapter chapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ImagePage>>(_urls.Select(u => new ImagePage { Url = u }).ToList());
    }
}