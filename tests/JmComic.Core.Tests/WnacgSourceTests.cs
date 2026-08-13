using System.Net;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Wnacg;

namespace JmComic.Core.Tests;

/// <summary>WnacgSource：使用录制的真实 HTML fixture 验证解析与映射（不依赖网络）。</summary>
public class WnacgSourceTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "fixtures", "wnacg");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

    /// <summary>返回固定 HTML 的假客户端。</summary>
    private sealed class FakeClient : WnacgHttpClient
    {
        private readonly Dictionary<string, string> _pages;

        public readonly List<string> RequestedPaths = new();

        public FakeClient(Dictionary<string, string> pages) : base("www.wnacg.com", new FakeHandler())
        {
            _pages = pages;
        }

        public override Task<string> GetHtmlAsync(string path, CancellationToken ct = default)
        {
            RequestedPaths.Add(path);
            var key = _pages.Keys.FirstOrDefault(k => path.StartsWith(k, StringComparison.Ordinal));
            if (key is null)
            {
                throw new InvalidOperationException($"未配置的路径: {path}");
            }
            return Task.FromResult(_pages[key]);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static WnacgSource CreateSource(Dictionary<string, string> pages) => new(new FakeClient(pages));

    private static (WnacgSource Source, FakeClient Client) CreateSourceWithClient(Dictionary<string, string> pages)
    {
        var client = new FakeClient(pages);
        return (new WnacgSource(client), client);
    }

    [Fact]
    public void Info_Describes_Wnacg()
    {
        var source = CreateSource(new Dictionary<string, string>());

        Assert.Equal("wnacg", source.Info.Id);
        Assert.Equal("绅士漫画", source.Info.DisplayName);
        Assert.False(source.Info.RequiresLogin);
        Assert.False(source.Info.SupportsSearchSort);
        Assert.True(source.Info.SupportsCategories);
        Assert.True(source.Info.SupportsRank);
        Assert.False(source.Info.SupportsWeekly);
        Assert.False(source.Info.SupportsFavorites);
        // 免登录站点：并发调低以规避 IP 限流（429）
        Assert.Equal(8, source.Info.MaxImageConcurrency);
        Assert.Equal(2, source.Info.MaxChapterConcurrency);
        Assert.Equal(2, source.Info.MaxUrlFetchConcurrency);
        Assert.Contains("Referer", source.Info.CoverHeaders.Keys);
    }

    [Fact]
    public void Rank_Periods_Are_Day_Week_Month_Year()
    {
        var source = CreateSource(new Dictionary<string, string>());

        Assert.True(source.Info.SupportsRank);
        Assert.False(source.Info.SupportsWeekly);
        var periods = source.GetRankPeriods();
        Assert.Equal(new[] { "day", "week", "month", "year" }, periods.Select(p => p.Id));
        Assert.Equal(new[] { "今日", "本週", "本月", "本年" }, periods.Select(p => p.Name));
    }

    [Fact]
    public async Task Rank_Parses_Items_And_TotalPages()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/albums-favorite_ranking-type-week-cate.html"] = Fixture("ranking.html"),
        });

        var result = await source.GetRankAsync("week", 1);

        Assert.True(result.Items.Count > 0);
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.Id)));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.CoverUrl)));
        Assert.True(result.TotalPages >= 2);
    }

    [Fact]
    public async Task Rank_First_Page_Uses_Cate_Path_And_Paged_Uses_Page_Path()
    {
        var (source, client) = CreateSourceWithClient(new Dictionary<string, string>
        {
            ["/albums-favorite_ranking-type-week-cate.html"] = Fixture("ranking.html"),
            ["/albums-favorite_ranking-page-2-type-week.html"] = Fixture("ranking.html"),
        });

        await source.GetRankAsync("week", 1);
        await source.GetRankAsync("week", 2);

        Assert.Contains(client.RequestedPaths, p => p.Contains("type-week-cate.html"));
        Assert.Contains(client.RequestedPaths, p => p.Contains("page-2-type-week.html"));
    }

    [Fact]
    public async Task Search_Parses_Items_And_Strips_Tags()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/search/index.php"] = Fixture("search.html"),
        });

        var result = await source.SearchAsync("test", 1);

        Assert.Equal(24, result.Items.Count);
        // 标题中的 <em> 标签应被剥离
        Assert.Contains(result.Items, i => i.Title.Contains("[White Lime] Test - Ryuuzen Tomoko"));
        Assert.All(result.Items, i =>
        {
            Assert.False(string.IsNullOrEmpty(i.Id));
            Assert.False(string.IsNullOrEmpty(i.Title));
            Assert.StartsWith("https:", i.CoverUrl);
        });
        // 分页器末页为 2
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(48, result.Total);
    }

    [Fact]
    public async Task GetComic_Parses_Detail()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/photos-index-aid-372788.html"] = Fixture("detail.html"),
        });

        var comic = await source.GetComicAsync("372788");

        Assert.Equal("372788", comic.Id);
        Assert.Equal("[White Lime] Test - Ryuuzen Tomoko", comic.Title);
        Assert.StartsWith("https://t4.qy0.ru/", comic.CoverUrl);
        Assert.Equal("AI圖集", Assert.Single(comic.Authors));
        Assert.Contains("WhiteLime", comic.Tags);
        Assert.Single(comic.Chapters);
        Assert.Equal("全一册", comic.Chapters[0].Title);
        Assert.Equal("372788", comic.Chapters[0].NumericId!.Value.ToString());
        Assert.Equal(comic.Title, comic.Chapters[0].ComicTitle);
    }

    [Fact]
    public async Task GetChapterImages_Returns_Urls_With_Referer()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/photos-gallery-aid-372788.html"] = Fixture("gallery.html"),
        });
        var chapter = new Chapter { Id = "372788", NumericId = 372788 };

        var pages = await source.GetChapterImagesAsync(chapter);

        // 223 张真实图片 + 1 张 shoucang 收藏图被过滤
        Assert.Equal(223, pages.Count);
        Assert.StartsWith("https://img5.qy0.ru/", pages[0].Url);
        Assert.All(pages, p =>
        {
            Assert.Equal("https://www.wnacg.com/", p.Headers["Referer"]);
            Assert.False(string.IsNullOrEmpty(p.Headers["User-Agent"]));
        });
    }

    [Fact]
    public async Task GetCategories_Parses_Home_Page()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/"] = Fixture("home.html"),
        });

        var categories = await source.GetCategoriesAsync();

        Assert.Contains(categories, c => c.Id == "5" && c.Name == "同人誌");
        Assert.Contains(categories, c => c.Id == "6" && c.Name == "單行本");
        Assert.Contains(categories, c => c.Id == "19" && c.Name == "韓漫");
        // 不应包含"更多"链接
        Assert.DoesNotContain(categories, c => c.Name.Contains("更多"));
    }

    [Fact]
    public async Task GetCategoryComics_Parses_Category_Page()
    {
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/albums-index-page-1-cate-5.html"] = Fixture("category.html"),
        });

        var result = await source.GetCategoryComicsAsync("5", 1);

        Assert.NotEmpty(result.Items);
        Assert.True(result.TotalPages >= 2);
    }
}

