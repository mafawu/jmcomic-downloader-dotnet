using System.Net;
using System.Text;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;

namespace JmComic.Core.Tests;

/// <summary>断点续传 / 已存在跳过：文件级跳过、整章跳过、临时目录清理与合并回写。</summary>
public class DownloadManagerResumeTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<string> ImageRequests = new();

        public string ApiBody(string path) => path switch
        {
            // scramble_id 默认回退值(220980) 大于章节 id → block_num 为 0，图片按原样保存
            "/chapter_view_template" => "var scramble_id = 999999999;",
            "/chapter" => "{\"id\":100,\"images\":[\"01.webp\",\"02.webp\"]}",
            _ => "{}",
        };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/media/photos/"))
            {
                ImageRequests.Add(path);
                var page = path.EndsWith("/01.webp") ? "page1" : "page2";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(page)),
                });
            }

            var tokenparam = request.Headers.TryGetValues("tokenparam", out var values) ? values.First() : "";
            var ts = long.Parse(tokenparam.Split(',')[0]);
            var encrypted = TestCrypto.EncryptData(ts, ApiBody(path));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"code\":200,\"data\":\"{encrypted}\"}}", Encoding.UTF8),
            });
        }
    }

    private static (DownloadManager Manager, string DownloadDir, FakeHandler Handler, ConfigService Config) Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-dl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, $"{{\"downloadDir\":\"{dir.Replace("\\", "\\\\")}\"}}");
        var config = new ConfigService(configPath);
        var handler = new FakeHandler();
        var jmClient = new JmHttpClient(config, handler);
        var manager = new DownloadManager(jmClient, config, new HttpClient(handler));
        return (manager, dir, handler, config);
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
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        return result;
    }

    private static string TempDir(string downloadDir) => Path.Combine(downloadDir, "测试专辑", ".下载中-第1话");
    private static string FinalDir(string downloadDir) => Path.Combine(downloadDir, "测试专辑", "第1话");

    [Fact]
    public async Task Full_Download_Renames_Temp_To_Final()
    {
        var (manager, downloadDir, handler, _) = Create();
        try
        {
            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            Assert.Equal(2, handler.ImageRequests.Count);
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "001.jpg")));
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "002.jpg")));
            Assert.False(Directory.Exists(TempDir(downloadDir)));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public async Task Interrupted_Temp_Dir_Resumes_And_Skips_Existing_Files()
    {
        var (manager, downloadDir, handler, _) = Create();
        try
        {
            // 模拟上次中断：临时目录里只有第 1 页
            Directory.CreateDirectory(TempDir(downloadDir));
            File.WriteAllText(Path.Combine(TempDir(downloadDir), "001.jpg"), "page1");

            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            // 只有第 2 页走了网络，第 1 页被跳过
            Assert.Single(handler.ImageRequests);
            Assert.Contains("/02.webp", handler.ImageRequests[0]);
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "001.jpg")));
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "002.jpg")));
            Assert.False(Directory.Exists(TempDir(downloadDir)));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public async Task Complete_Final_Dir_Skips_Whole_Chapter()
    {
        var (manager, downloadDir, handler, _) = Create();
        try
        {
            // 正式目录已完整
            Directory.CreateDirectory(FinalDir(downloadDir));
            File.WriteAllText(Path.Combine(FinalDir(downloadDir), "001.jpg"), "page1");
            File.WriteAllText(Path.Combine(FinalDir(downloadDir), "002.jpg"), "page2");

            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            Assert.Empty(handler.ImageRequests);
            Assert.False(Directory.Exists(TempDir(downloadDir)));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public async Task Incomplete_Final_Dir_Merges_Instead_Of_Failing()
    {
        var (manager, downloadDir, handler, _) = Create();
        try
        {
            // 正式目录存在但缺第 2 页（如手动删除过）→ 重新下载并合并
            Directory.CreateDirectory(FinalDir(downloadDir));
            File.WriteAllText(Path.Combine(FinalDir(downloadDir), "001.jpg"), "old-page1");

            var result = await DownloadOnceAsync(manager, Chapter());

            Assert.Null(result!.ErrMsg);
            Assert.Equal(2, handler.ImageRequests.Count);
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "001.jpg")));
            Assert.True(File.Exists(Path.Combine(FinalDir(downloadDir), "002.jpg")));
            Assert.False(Directory.Exists(TempDir(downloadDir)));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public void IsChapterComplete_Checks_All_Expected_Files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(DownloadManager.IsChapterComplete(dir, 2, "jpg"));

            File.WriteAllText(Path.Combine(dir, "001.jpg"), "a");
            Assert.False(DownloadManager.IsChapterComplete(dir, 2, "jpg"));

            File.WriteAllText(Path.Combine(dir, "002.jpg"), "b");
            Assert.True(DownloadManager.IsChapterComplete(dir, 2, "jpg"));

            // 空文件不算完成
            File.WriteAllText(Path.Combine(dir, "002.jpg"), "");
            Assert.False(DownloadManager.IsChapterComplete(dir, 2, "jpg"));

            // 扩展名不匹配不算完成
            File.WriteAllText(Path.Combine(dir, "002.jpg"), "b");
            Assert.False(DownloadManager.IsChapterComplete(dir, 2, "png"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsFileComplete_Only_Accepts_Existing_NonEmpty_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "001.jpg");
            Assert.False(DownloadManager.IsFileComplete(path));

            File.WriteAllText(path, "");
            Assert.False(DownloadManager.IsFileComplete(path));

            File.WriteAllText(path, "x");
            Assert.True(DownloadManager.IsFileComplete(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
