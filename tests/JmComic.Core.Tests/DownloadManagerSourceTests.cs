using System.Net;
using System.Text;
using JmComic.Core.Downloading;
using JmComic.Core.Models;
using JmComic.Core.Services;
using JmComic.Core.Sources;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JmComic.Core.Tests;

/// <summary>DownloadManager 通用路径：通过 IComicSource 下载，不依赖任何站点实现。</summary>
public class DownloadManagerSourceTests
{
    private sealed class FakeSource : IComicSource
    {
        public FakeSource(IReadOnlyList<ImagePage> pages)
        {
            Pages = pages;
        }

        public IReadOnlyList<ImagePage> Pages { get; }

        public ComicSourceInfo Info { get; } = new() { Id = "fake", DisplayName = "测试源" };

        public Task<SearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
            => Task.FromResult(new SearchResult());

        public Task<ComicDetail> GetComicAsync(string comicId, CancellationToken ct = default)
            => Task.FromResult(new ComicDetail());

        public Task<IReadOnlyList<ImagePage>> GetChapterImagesAsync(Chapter chapter, CancellationToken ct = default)
            => Task.FromResult(Pages);
    }

    private sealed class FakeImageHandler : HttpMessageHandler
    {
        public readonly List<(string Path, string UserAgent, string? Referer)> Requests = new();

        public Func<string, byte[]>? ContentProvider { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ua = request.Headers.TryGetValues("User-Agent", out var uas) ? uas.First() : "";
            var referer = request.Headers.TryGetValues("Referer", out var refs) ? refs.First() : null;
            Requests.Add((request.RequestUri!.AbsolutePath, ua, referer));
            var payload = ContentProvider?.Invoke(request.RequestUri!.AbsolutePath)
                         ?? Encoding.UTF8.GetBytes("page-bytes");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private static (DownloadManager Manager, string DownloadDir, FakeImageHandler Handler) Create(
        IReadOnlyList<ImagePage> pages)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-dl-source-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, $"{{\"downloadDir\":\"{dir.Replace("\\", "\\\\")}\"}}");
        var config = new ConfigService(configPath);
        var handler = new FakeImageHandler();
        var manager = new DownloadManager(new FakeSource(pages), config, new HttpClient(handler));
        return (manager, dir, handler);
    }

    private static ChapterInfo Chapter() => new()
    {
        ChapterId = 100,
        ChapterTitle = "第1话",
        AlbumTitle = "测试专辑",
    };

    private static async Task<ChapterEndEventArgs?> DownloadOnceAsync(DownloadManager manager, ChapterInfo chapter)
    {
        var tcs = new TaskCompletionSource<ChapterEndEventArgs?>();
        void OnEnd(object? s, ChapterEndEventArgs e)
        {
            manager.ChapterEnd -= OnEnd;
            tcs.TrySetResult(e);
        }
        manager.ChapterEnd += OnEnd;
        await manager.SubmitChapterAsync(chapter);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Downloads_Pages_With_PerPage_Headers()
    {
        var pages = new List<ImagePage>
        {
            new() { Url = "https://img.example.com/1.webp", BlockNum = 0, Headers = new Dictionary<string, string> { ["User-Agent"] = "FakeUA", ["Referer"] = "https://site.example.com/" } },
            new() { Url = "https://img.example.com/2.webp", BlockNum = 0, Headers = new Dictionary<string, string> { ["User-Agent"] = "FakeUA" } },
        };
        var (manager, downloadDir, handler) = Create(pages);
        try
        {
            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("FakeUA", r.UserAgent));
            Assert.Equal("https://site.example.com/", handler.Requests[0].Referer);
            Assert.Null(handler.Requests[1].Referer);
            Assert.True(File.Exists(Path.Combine(downloadDir, "测试专辑", "第1话", "001.jpg")));
            Assert.True(File.Exists(Path.Combine(downloadDir, "测试专辑", "第1话", "002.jpg")));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public async Task Reassembles_Split_Images_From_Generic_Source()
    {
        using var img = new Image<Rgba32>(4, 4);
        img.Mutate(x => x.BackgroundColor(Color.Red));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        var bytes = ms.ToArray();

        var pages = new List<ImagePage>
        {
            new() { Url = "https://img.example.com/split.webp", BlockNum = 2 },
        };
        var (manager, downloadDir, handler) = Create(pages);
        handler.ContentProvider = _ => bytes;
        try
        {
            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            var savePath = Path.Combine(downloadDir, "测试专辑", "第1话", "001.jpg");
            using (var loaded = Image.Load(savePath))
            {
                Assert.Equal(4, loaded.Width);
                Assert.Equal(4, loaded.Height);
            }
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }
}



