using System.Net;
using System.Text;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Jm;
using JmComic.Core.Services;
using JmComic.Core.Utils;

namespace JmComic.Core.Tests;

/// <summary>JmSource：验证禁漫特有的逻辑（加密、重定向、block_num、图片域名）被正确收敛到源实现。</summary>
public class JmSourceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string> OnApi { get; set; } = _ => "{}";
        public Func<HttpRequestMessage, string> OnScramble { get; set; } = _ => "var scramble_id = 999999999;";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path == "/chapter_view_template" ? OnScramble(request) : OnApi(request);
            if (path == "/chapter_view_template")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8),
                });
            }
            var tokenparam = request.Headers.TryGetValues("tokenparam", out var values) ? values.First() : "";
            var ts = long.Parse(tokenparam.Split(',')[0]);
            var encrypted = TestCrypto.EncryptData(ts, body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"code\":200,\"data\":\"{encrypted}\"}}", Encoding.UTF8),
            });
        }
    }

    private static ConfigService NewConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-source-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new ConfigService(Path.Combine(dir, "config.json"));
    }

    private static JmSource CreateSource(FakeHandler handler) => new(new JmHttpClient(NewConfig(), handler));

    [Fact]
    public void Info_Describes_Jm()
    {
        var source = CreateSource(new FakeHandler());

        Assert.Equal("jm", source.Info.Id);
        Assert.Equal("禁漫天堂", source.Info.DisplayName);
        Assert.False(source.Info.RequiresLogin);
    }

    [Fact]
    public async Task Search_Maps_List_Response()
    {
        var handler = new FakeHandler
        {
            OnApi = _ => """
                {"search_query":"x","total":2,"content":[
                  {"id":"1","author":"作者A","name":"漫画一","image":"/media/photos/1/cover.jpg","category":{"id":"1","title":"同人"}},
                  {"id":"2","author":"","name":"漫画二","image":"https://cdn.example.com/a.jpg"}
                ]}
                """,
        };
        var source = CreateSource(handler);

        var result = await source.SearchAsync("x", 1);

        Assert.False(result.IsSingleMatch);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("漫画一", result.Items[0].Title);
        Assert.Equal("作者A", result.Items[0].Author);
        Assert.Equal("同人", result.Items[0].Category);
        Assert.Equal($"https://{JmConstants.ImageDomain}/media/photos/1/cover.jpg", result.Items[0].CoverUrl);
        Assert.Equal("https://cdn.example.com/a.jpg", result.Items[1].CoverUrl);
    }

    [Fact]
    public async Task Search_Maps_Redirect_To_Single_Match()
    {
        var handler = new FakeHandler
        {
            OnApi = request => request.RequestUri!.AbsolutePath == "/album"
                ? """{"id":123,"name":"唯一漫画","series":[]}"""
                : """{"search_query":"x","total":1,"redirect_aid":"123"}""",
        };
        var source = CreateSource(handler);

        var result = await source.SearchAsync("x", 1);

        Assert.True(result.IsSingleMatch);
        Assert.Equal("123", result.SingleComicId);
    }

    [Fact]
    public async Task GetComic_Maps_Album_With_Chapters()
    {
        var handler = new FakeHandler
        {
            OnApi = _ => """
                {"id":1001,"name":"测试/漫画","description":"简介","author":["作者A"],"tags":["tag1"],
                 "series":[{"id":"2001","name":"","sort":"1"},{"id":"2002","name":"特别篇","sort":"2"}]}
                """,
        };
        var source = CreateSource(handler);

        var comic = await source.GetComicAsync("1001");

        Assert.Equal("1001", comic.Id);
        Assert.Equal("测试 漫画", comic.Title);
        Assert.Equal("简介", comic.Description);
        Assert.Equal(["作者A"], comic.Authors);
        Assert.Equal(["tag1"], comic.Tags);
        Assert.Equal(2, comic.Chapters.Count);
        Assert.Equal("第1话", comic.Chapters[0].Title);
        Assert.Equal("第2话 特别篇", comic.Chapters[1].Title);
        Assert.Equal(2001, comic.Chapters[0].NumericId);
        Assert.Equal("1001", comic.Chapters[0].ComicId);
        Assert.Equal("测试 漫画", comic.Chapters[0].ComicTitle);
    }

    [Fact]
    public async Task GetComic_Adds_Default_Chapter_When_Series_Empty()
    {
        var handler = new FakeHandler
        {
            OnApi = _ => """{"id":1001,"name":"单话","series":[]}""",
        };
        var source = CreateSource(handler);

        var comic = await source.GetComicAsync("1001");

        Assert.Single(comic.Chapters);
        Assert.Equal("第1话", comic.Chapters[0].Title);
        Assert.Equal(1001, comic.Chapters[0].NumericId);
    }

    [Fact]
    public async Task GetChapterImages_Filters_NonWebp_And_Sets_Ua_Header()
    {
        var handler = new FakeHandler
        {
            OnApi = request => request.RequestUri!.AbsolutePath == "/chapter"
                ? """{"id":2001,"images":["01.webp","02.webp","03.jpg"]}"""
                : "{}",
        };
        var source = CreateSource(handler);
        var chapter = new Chapter { Id = "2001", NumericId = 2001 };

        var pages = await source.GetChapterImagesAsync(chapter);

        Assert.Equal(2, pages.Count);
        Assert.Equal($"https://{JmConstants.ImageDomain}/media/photos/2001/01.webp", pages[0].Url);
        Assert.Equal($"https://{JmConstants.ImageDomain}/media/photos/2001/02.webp", pages[1].Url);
        Assert.Equal(JmConstants.UserAgent, pages[0].Headers["User-Agent"]);
    }

    [Fact]
    public async Task GetChapterImages_Computes_NonZero_BlockNum()
    {
        const long scrambleId = 100;
        const long chapterId = 500_000;
        const string filename = "01";
        var handler = new FakeHandler
        {
            OnScramble = _ => $"var scramble_id = {scrambleId};",
            OnApi = request => request.RequestUri!.AbsolutePath == "/chapter"
                ? $$"""{"id":{{chapterId}},"images":["01.webp"]}"""
                : "{}",
        };
        var source = CreateSource(handler);
        var chapter = new Chapter { Id = chapterId.ToString(), NumericId = chapterId };

        var pages = await source.GetChapterImagesAsync(chapter);

        var expected = BlockNumCalculator.Calculate(scrambleId, chapterId, filename);
        Assert.True(expected > 0);
        Assert.Equal(expected, pages[0].BlockNum);
    }
}


