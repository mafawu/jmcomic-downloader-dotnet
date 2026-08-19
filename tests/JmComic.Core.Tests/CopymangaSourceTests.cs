using System.Net;
using System.Text;
using System.Text.Json;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Copymanga;

namespace JmComic.Core.Tests;

/// <summary>
/// CopymangaSource：用模拟 JSON 响应验证搜索 / 详情 / 章节 / 图片映射（不依赖网络）。
/// 响应结构对齐 copymanga-downloader（lanyeeee）源码。
/// </summary>
public class CopymangaSourceTests
{
    /// <summary>返回固定 JSON 的假客户端（按路径前缀路由）。</summary>
    private sealed class FakeClient : CopymangaHttpClient
    {
        private readonly Dictionary<string, string> _responses;

        public readonly List<string> RequestedPaths = new();
        public string? LoginPassword;

        public FakeClient(Dictionary<string, string> responses) : base(null, new FakeHandler())
        {
            _responses = responses;
        }

        public override Task<T> GetAsync<T>(string path, bool requireAuth = false, CancellationToken ct = default)
        {
            RequestedPaths.Add(path);
            var key = _responses.Keys.FirstOrDefault(k => path.StartsWith(k, StringComparison.Ordinal));
            if (key is null)
            {
                throw new InvalidOperationException($"未配置的路径: {path}");
            }
            var json = _responses[key];
            var api = JsonSerializer.Deserialize<CopyApiResponse<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (api is null || api.Results is null)
            {
                throw new InvalidOperationException($"无法解析响应: {path}");
            }
            return Task.FromResult(api.Results);
        }

        public override Task<CopyLoginResult> LoginAsync(
            string username, string encodedPassword, int salt, CancellationToken ct = default)
        {
            LoginPassword = encodedPassword;
            return Task.FromResult(new CopyLoginResult { Token = "test-token", Username = username });
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
        }
    }

    private static CopymangaSource CreateSource(Dictionary<string, string> responses)
        => new(new FakeClient(responses));

    private static (CopymangaSource Source, FakeClient Client) CreateSourceWithClient(Dictionary<string, string> responses)
    {
        var client = new FakeClient(responses);
        return (new CopymangaSource(client), client);
    }

    private static string SearchJson(params string[] names)
    {
        var items = string.Join(",", names.Select(n =>
            "{\"name\":\"" + n + "\",\"path_word\":\"" + n.ToLowerInvariant() +
            "\",\"cover\":\"https://img.mangacopy.com/uploads/" + n + ".jpg\"," +
            "\"ban\":0,\"author\":[{\"name\":\"作者甲\"}],\"popular\":100}"));
        return "{\"code\":200,\"message\":\"ok\",\"results\":{\"total\":" + names.Length +
               ",\"limit\":20,\"offset\":0,\"list\":[" + items + "]}}";
    }

    [Fact]
    public void Info_Describes_Copymanga()
    {
        var source = CreateSource(new Dictionary<string, string>());

        Assert.Equal("copymanga", source.Info.Id);
        Assert.Equal("拷贝漫画", source.Info.DisplayName);
        Assert.False(source.Info.RequiresLogin);
        Assert.False(source.Info.SupportsCategories);
        Assert.False(source.Info.SupportsRank);
        Assert.False(source.Info.SupportsWeekly);
        Assert.False(source.Info.SupportsFavorites);
    }

    [Fact]
    public async Task SearchAsync_Parses_Results_And_Pages()
    {
        var (source, client) = CreateSourceWithClient(new Dictionary<string, string>
        {
            ["/api/v3/search/comic"] = SearchJson("武炼巅峰", "斗破苍穹"),
        });

        var result = await source.SearchAsync("武炼", 1);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("武炼巅峰", result.Items[0].Title);
        Assert.Equal("武炼巅峰", result.Items[0].Id); // path_word
        Assert.Equal("作者甲", result.Items[0].Author);
        Assert.Equal("https://img.mangacopy.com/uploads/武炼巅峰.jpg", result.Items[0].CoverUrl);
        Assert.Contains("/api/v3/search/comic", client.RequestedPaths[0]);
    }

    [Fact]
    public async Task SearchAsync_Computes_TotalPages()
    {
        var names = Enumerable.Range(1, 45).Select(i => $"漫画{i}").ToArray();
        var source = CreateSource(new Dictionary<string, string>
        {
            ["/api/v3/search/comic"] = SearchJson(names),
        });

        var result = await source.SearchAsync("漫画", 2);

        Assert.Equal(45, result.Total);
        Assert.Equal(3, result.TotalPages); // ceil(45/20)
    }

    [Fact]
    public async Task GetComicAsync_Uses_Comic2_And_Groups_Chapters()
    {
        var (source, client) = CreateSourceWithClient(new Dictionary<string, string>
        {
            ["/api/v3/comic2/武炼巅峰"] =
                """
                {"code":200,"message":"ok","results":{
                  "comic":{"uuid":"c1","name":"武炼巅峰","path_word":"武炼巅峰",
                           "cover":"https://img.mangacopy.com/c.jpg","brief":"经典国漫",
                           "author":[{"name":"作者甲"}],"theme":[{"name":"热血"}]},
                  "groups":{
                    "zhengxu":{"path_word":"zhengxu","count":2,"name":"正序"},
                    "fanwai":{"path_word":"fanwai","count":1,"name":"番外"}
                  }}}
                """,
            ["/api/v3/comic/武炼巅峰/group/zhengxu/chapters"] =
                """
                {"code":200,"message":"ok","results":{"total":2,"limit":100,"offset":0,"list":[
                  {"index":1,"uuid":"aaa-111","name":"第1话","ordered":10,"comic_path_word":"武炼巅峰","group_path_word":"zhengxu"},
                  {"index":2,"uuid":"aaa-222","name":"第2话","ordered":20,"comic_path_word":"武炼巅峰","group_path_word":"zhengxu"}
                ]}}
                """,
            ["/api/v3/comic/武炼巅峰/group/fanwai/chapters"] =
                """
                {"code":200,"message":"ok","results":{"total":1,"limit":100,"offset":0,"list":[
                  {"index":1,"uuid":"bbb-333","name":"特别篇","ordered":30,"comic_path_word":"武炼巅峰","group_path_word":"fanwai"}
                ]}}
                """,
        });

        var detail = await source.GetComicAsync("武炼巅峰");

        Assert.Equal("武炼巅峰", detail.Title);
        Assert.Equal("经典国漫", detail.Description);
        Assert.Equal(new List<string> { "作者甲" }, detail.Authors);
        Assert.Equal(new List<string> { "热血" }, detail.Tags);
        Assert.Equal(3, detail.Chapters.Count);
        Assert.Equal("正序·第1话", detail.Chapters[0].Title);
        Assert.Equal("aaa-111", detail.Chapters[0].Id);
        Assert.Equal("番外·特别篇", detail.Chapters[2].Title);
        // 按 ordered/10 排序：第1话(1.0) < 第2话(2.0) < 特别篇(3.0)
        Assert.Equal(1.0, detail.Chapters[0].OrderValue);
        Assert.Equal(3.0, detail.Chapters[2].OrderValue);
        Assert.All(detail.Chapters, c => Assert.Equal("copymanga", c.SourceId));
        Assert.Contains("/api/v3/comic2/武炼巅峰", client.RequestedPaths[0]);
    }

    [Fact]
    public async Task GetChapterImagesAsync_Reorders_By_Words_And_Upgrades_Resolution()
    {
        var (source, client) = CreateSourceWithClient(new Dictionary<string, string>
        {
            ["/api/v3/comic/武炼巅峰/chapter2/aaa-111"] =
                """
                {"code":200,"message":"ok","results":{
                  "chapter":{"uuid":"aaa-111","name":"第1话",
                    "contents":[
                      {"url":"https://img.mangacopy.com/a.c800x.webp"},
                      {"url":"https://img.mangacopy.com/b.c800x.webp"},
                      {"url":"https://img.mangacopy.com/c.c800x.webp"}
                    ],
                    "words":[2,0,1]}}
                }
                """,
        });

        var chapter = new Chapter
        {
            Id = "aaa-111",
            ComicId = "武炼巅峰",
            Title = "第1话",
            ComicTitle = "武炼巅峰",
            SourceId = "copymanga",
        };

        var images = await source.GetChapterImagesAsync(chapter);

        // words=[2,0,1]：contents[0](a) 放位置2，contents[1](b) 放位置0，contents[2](c) 放位置1
        // 结果顺序应为 b, c, a；且 .c800x. 替换为 .c1500x.
        Assert.Equal(3, images.Count);
        Assert.Equal("https://img.mangacopy.com/b.c1500x.webp", images[0].Url);
        Assert.Equal("https://img.mangacopy.com/c.c1500x.webp", images[1].Url);
        Assert.Equal("https://img.mangacopy.com/a.c1500x.webp", images[2].Url);
        Assert.Contains("Referer", images[0].Headers.Keys);
        Assert.Equal(0u, images[0].BlockNum);
        Assert.Contains("/api/v3/comic/武炼巅峰/chapter2/aaa-111", client.RequestedPaths[0]);
    }

    [Fact]
    public async Task LoginAsync_Encodes_Password_And_Sets_Token()
    {
        var (source, client) = CreateSourceWithClient(new Dictionary<string, string>());

        await source.LoginAsync("user", "pass123");

        Assert.Equal("test-token", client.Token);
        Assert.True(source.IsLoggedIn);
        // base64("pass123-1729")
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("pass123-1729"));
        Assert.Equal(expected, client.LoginPassword);
    }
}
