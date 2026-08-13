using System.Net;
using System.Text;
using JmComic.Core.Downloading;
using JmComic.Core.Services;
using JmComic.Core.Sources;

namespace JmComic.Core.Tests;

/// <summary>DownloadManager 多源路由：按 Chapter.SourceId 解析内容源，未知 id 回退首源。</summary>
public class DownloadManagerSourceRoutingTests
{
    private sealed class FakeSource : IComicSource
    {
        public FakeSource(string id, IReadOnlyList<ImagePage> pages)
        {
            Info = new ComicSourceInfo { Id = id, DisplayName = id };
            Pages = pages;
        }

        public ComicSourceInfo Info { get; }
        public IReadOnlyList<ImagePage> Pages { get; }

        /// <summary>GetChapterImagesAsync 被调用次数（验证路由是否落到本源）。</summary>
        public int ChapterImagesCalls;

        public Task<SearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
            => Task.FromResult(new SearchResult());

        public Task<ComicDetail> GetComicAsync(string comicId, CancellationToken ct = default)
            => Task.FromResult(new ComicDetail());

        public Task<IReadOnlyList<ImagePage>> GetChapterImagesAsync(Chapter chapter, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ChapterImagesCalls);
            return Task.FromResult(Pages);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<string> Paths = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("page-bytes")),
            });
        }
    }

    private static Chapter Chapter(long id, string sourceId, string comicTitle) => new()
    {
        Id = id.ToString(),
        NumericId = id,
        Title = "第1话",
        ComicId = id.ToString(),
        ComicTitle = comicTitle,
        SourceId = sourceId,
    };

    private static async Task<DownloadManager> CreateAndRunAsync(
        RecordingHandler handler, FakeSource a, FakeSource b, params Chapter[] chapters)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-dl-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, $"{{\"downloadDir\":\"{dir.Replace("\\", "\\\\")}\"}}");
        var config = new ConfigService(configPath);
        var manager = new DownloadManager(new IComicSource[] { a, b }, config, new HttpClient(handler));

        var tcs = new TaskCompletionSource<bool>();
        var remaining = chapters.Length;
        manager.ChapterEnd += (_, _) =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                tcs.TrySetResult(true);
            }
        };
        try
        {
            foreach (var chapter in chapters)
            {
                await manager.SubmitChapterAsync(chapter);
            }
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(dir, true);
        }
        return manager;
    }

    [Fact]
    public async Task Routes_Chapters_To_Their_Own_Source()
    {
        var handler = new RecordingHandler();
        var sourceA = new FakeSource("a", new List<ImagePage>
        {
            new() { Url = "https://a.example.com/1.webp" },
        });
        var sourceB = new FakeSource("b", new List<ImagePage>
        {
            new() { Url = "https://b.example.com/1.webp" },
        });

        await CreateAndRunAsync(handler, sourceA, sourceB,
            Chapter(1, "a", "漫画A"),
            Chapter(2, "b", "漫画B"));

        Assert.Equal(1, sourceA.ChapterImagesCalls);
        Assert.Equal(1, sourceB.ChapterImagesCalls);
        Assert.Contains("/1.webp", handler.Paths);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Unknown_SourceId_Falls_Back_To_First_Source()
    {
        var handler = new RecordingHandler();
        var sourceA = new FakeSource("a", new List<ImagePage>
        {
            new() { Url = "https://a.example.com/1.webp" },
        });
        var sourceB = new FakeSource("b", new List<ImagePage>
        {
            new() { Url = "https://b.example.com/1.webp" },
        });

        await CreateAndRunAsync(handler, sourceA, sourceB,
            Chapter(1, "unknown", "漫画C"));

        Assert.Equal(1, sourceA.ChapterImagesCalls);
        Assert.Equal(0, sourceB.ChapterImagesCalls);
        Assert.Single(handler.Paths);
    }
}
