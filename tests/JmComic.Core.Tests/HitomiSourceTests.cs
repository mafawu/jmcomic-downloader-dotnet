using System.Buffers.Binary;
using System.Net;
using System.Text;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Hitomi;

namespace JmComic.Core.Tests;

/// <summary>HitomiSource：nozomi 列表 / 画廊信息 / B-tree 搜索 / 图片 URL 构造（不依赖网络）。</summary>
public class HitomiSourceTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "fixtures", "hitomi");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDir, name));

    /// <summary>固定 64 位十六进制 hash（与 gg.js 解析、URL 构造测试共用）。</summary>
    private const string SampleHash = "6d11574931d71419a12b30bbcb1e18e6d4482155352cdcfc10d8cc89e93196f7";

    /// <summary>按 URL 路由的假 HTTP 处理器：文件表 + 模板生成器 + Range 切片。</summary>
    private sealed class FakeHitomiHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly Dictionary<string, Func<string, byte[]>> _templates;
        public readonly List<string> RequestedUrls = new();

        public FakeHitomiHandler(
            Dictionary<string, byte[]>? files = null,
            Dictionary<string, Func<string, byte[]>>? templates = null)
        {
            _files = files ?? new Dictionary<string, byte[]>();
            _templates = templates ?? new Dictionary<string, Func<string, byte[]>>();
        }

        public void Add(string url, byte[] data) => _files[url] = data;

        public void AddTemplate(string prefix, Func<string, byte[]> generator) => _templates[prefix] = generator;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            RequestedUrls.Add(request.Headers.Range is null ? url : $"{url} [{request.Headers.Range}]");

            byte[] data;
            var fileKey = _files.Keys.FirstOrDefault(k => url.StartsWith(k, StringComparison.Ordinal));
            if (fileKey is not null)
            {
                data = _files[fileKey];
            }
            else
            {
                var tpl = _templates.Keys.FirstOrDefault(k => url.StartsWith(k, StringComparison.Ordinal));
                if (tpl is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }
                data = _templates[tpl](url);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data),
            };
            if (request.Headers.Range is { Ranges.Count: > 0 } range)
            {
                var from = range.Ranges.First().From ?? 0;
                var to = range.Ranges.First().To ?? data.Length - 1;
                if (from < data.Length)
                {
                    var end = (int)Math.Min(to + 1, data.Length);
                    response.Content = new ByteArrayContent(data[(int)from..end]);
                    response.StatusCode = HttpStatusCode.PartialContent;
                }
            }
            return Task.FromResult(response);
        }
    }

    private static string BaseUrl => $"https://{HitomiConstants.Domain}";

    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>BigEndian Int32 序列 → nozomi 文件字节。</summary>
    private static byte[] NozomiBytes(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        var data = new byte[list.Count * 4];
        for (var i = 0; i < list.Count; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(i * 4, 4), list[i]);
        }
        return data;
    }

    private static (HitomiSource Source, FakeHitomiHandler Handler) CreateSource(FakeHitomiHandler handler)
    {
        var client = new HitomiHttpClient(handler);
        return (new HitomiSource(new HitomiGalleryClient(client), new HitomiGgResolver(client)), handler);
    }

    // ====================== Info ======================

    [Fact]
    public void Info_Describes_Hitomi()
    {
        var (source, _) = CreateSource(new FakeHitomiHandler());

        Assert.Equal("hitomi", source.Info.Id);
        Assert.Equal("hitomi", source.Info.DisplayName);
        Assert.False(source.Info.RequiresLogin);
        Assert.False(source.Info.SupportsSearchSort);
        Assert.False(source.Info.SupportsCategories);
        Assert.True(source.Info.SupportsRank);
        Assert.False(source.Info.SupportsWeekly);
        Assert.False(source.Info.SupportsFavorites);
        Assert.Equal(8, source.Info.MaxImageConcurrency);
        Assert.Contains("Referer", source.Info.CoverHeaders.Keys);
    }

    [Fact]
    public void Rank_Periods_Is_Single_Popular()
    {
        var (source, _) = CreateSource(new FakeHitomiHandler());

        var periods = source.GetRankPeriods();
        var period = Assert.Single(periods);
        Assert.Equal("popular", period.Id);
        Assert.Equal("热门", period.Name);
    }

    // ====================== 二进制索引 ======================

    [Fact]
    public void ParseNozomiIds_Reads_BigEndian_Int32()
    {
        var data = NozomiBytes(new[] { 1469394, 1626682, 25 });

        var ids = HitomiBinaryIndex.ParseNozomiIds(data);

        Assert.Equal(new[] { 1469394, 1626682, 25 }, ids);
    }

    [Fact]
    public void DecodeNode_Parses_Keys_Datas_And_Subnodes()
    {
        var key = HitomiBinaryIndex.HashTerm("touhou");
        var node = new byte[HitomiConstants.MaxNodeSize];
        var offset = 0;
        WriteInt32(node, ref offset, 1);                // key 数
        WriteInt32(node, ref offset, key.Length);       // key 长度
        key.CopyTo(node, offset); offset += key.Length; // key
        WriteInt32(node, ref offset, 1);                // data 数
        WriteInt64(node, ref offset, 4096);             // data 偏移
        WriteInt32(node, ref offset, 12);               // data 长度
        for (var i = 0; i <= HitomiConstants.B; i++)
        {
            WriteInt64(node, ref offset, 0);            // 子节点地址（叶子）
        }

        var decoded = HitomiBinaryIndex.DecodeNode(node);

        var decodedKey = Assert.Single(decoded.Keys);
        Assert.Equal(key, decodedKey);
        var data = Assert.Single(decoded.Datas);
        Assert.Equal((4096L, 12), data);
        Assert.True(HitomiBinaryIndex.IsLeaf(decoded));
    }

    private static void WriteInt32(byte[] buffer, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static void WriteInt64(byte[] buffer, ref int offset, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), value);
        offset += 8;
    }

    // ====================== 排行（nozomi 分页） ======================

    [Fact]
    public async Task Rank_Slices_Nozomi_Into_Pages()
    {
        var handler = new FakeHitomiHandler();
        handler.Add($"{BaseUrl}/n/popular-all.nozomi", NozomiBytes(Enumerable.Range(1000, 60)));
        handler.Add($"{BaseUrl}/gg.js", Text(Fixture("gg.js")));
        handler.AddTemplate($"{BaseUrl}/galleries/", url =>
        {
            var id = int.Parse(url[(url.LastIndexOf('/') + 1)..].Replace(".js", ""));
            return Text($$"""var galleryinfo = {"id":{{id}},"title":"gallery {{id}}","files":[{"name":"01.webp","hash":"{{SampleHash}}","width":1,"height":1}]}""");
        });
        var (source, _) = CreateSource(handler);

        var result = await source.GetRankAsync("popular", 2);

        Assert.Equal(60, result.Total);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(25, result.Items.Count);
        Assert.Equal("gallery 1025", result.Items[0].Title); // 第 2 页 = id 1025..1049 的第一条
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.CoverUrl)));
    }

    // ====================== 详情 ======================

    [Fact]
    public async Task GetComicAsync_Builds_Detail_With_Single_Chapter()
    {
        var handler = new FakeHitomiHandler();
        handler.Add($"{BaseUrl}/galleries/4116540.js", Text(Fixture("gallery-4116540.js")));
        handler.Add($"{BaseUrl}/gg.js", Text(Fixture("gg.js")));
        var (source, _) = CreateSource(handler);

        var detail = await source.GetComicAsync("4116540");

        Assert.Equal("4116540", detail.Id);
        Assert.False(string.IsNullOrEmpty(detail.Title));
        Assert.NotEmpty(detail.Tags);
        Assert.Matches(@"^https://[a-z]tn\.gold-usergeneratedcontent\.net/webpbigtn/", detail.CoverUrl);
        var chapter = Assert.Single(detail.Chapters);
        Assert.Equal("hitomi", chapter.SourceId);
        Assert.Equal("全一册", chapter.Title);
        Assert.Equal(4116540, chapter.NumericId);
    }

    [Fact]
    public async Task GetChapterImagesAsync_Returns_Webp_Or_Avif_Urls()
    {
        var handler = new FakeHitomiHandler();
        handler.Add($"{BaseUrl}/galleries/4116540.js", Text(Fixture("gallery-4116540.js")));
        handler.Add($"{BaseUrl}/gg.js", Text(Fixture("gg.js")));
        var (source, _) = CreateSource(handler);

        var pages = await source.GetChapterImagesAsync(new Chapter
        {
            Id = "4116540",
            NumericId = 4116540,
            Title = "全一册",
            SourceId = "hitomi",
        });

        Assert.True(pages.Count > 0);
        Assert.All(pages, p =>
        {
            Assert.Matches(@"^https://w\d+\.gold-usergeneratedcontent\.net/[^/]+/\d+/[0-9a-f]{64}\.webp$", p.Url);
            Assert.Equal(HitomiConstants.Referer, p.Headers["Referer"]);
        });
    }

    // ====================== 搜索（B-tree tagindex） ======================

    [Fact]
    public async Task SearchAsync_Uses_TagIndex_BTree()
    {
        var term = "touhou";
        var key = HitomiBinaryIndex.HashTerm(term);
        var ids = new[] { 111, 222, 333 };

        // 数据段：count + id 序列
        var data = new byte[4 + ids.Length * 4];
        BinaryPrimitives.WriteInt32BigEndian(data, ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4 + i * 4, 4), ids[i]);
        }

        // 根节点（地址 0）：单 key 指向数据段，叶子
        var node = new byte[HitomiConstants.MaxNodeSize];
        var offset = 0;
        WriteInt32(node, ref offset, 1);
        WriteInt32(node, ref offset, key.Length);
        key.CopyTo(node, offset); offset += key.Length;
        WriteInt32(node, ref offset, 1);
        WriteInt64(node, ref offset, 0);
        WriteInt32(node, ref offset, data.Length);
        for (var i = 0; i <= HitomiConstants.B; i++)
        {
            WriteInt64(node, ref offset, 0);
        }

        var handler = new FakeHitomiHandler();
        handler.Add($"{BaseUrl}/galleriesindex/version", Text("1786607455"));
        handler.Add($"{BaseUrl}/galleriesindex/galleries.1786607455.index", node);
        handler.Add($"{BaseUrl}/galleriesindex/galleries.1786607455.data", data);
        handler.Add($"{BaseUrl}/gg.js", Text(Fixture("gg.js")));
        handler.AddTemplate($"{BaseUrl}/galleries/", url =>
        {
            var id = int.Parse(url[(url.LastIndexOf('/') + 1)..].Replace(".js", ""));
            return Text($$"""var galleryinfo = {"id":{{id}},"title":"gallery {{id}}","files":[{"name":"01.webp","hash":"{{SampleHash}}","width":1,"height":1}]}""");
        });
        var (source, _) = CreateSource(handler);

        var result = await source.SearchAsync(term, 1);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new[] { "111", "222", "333" }, result.Items.Select(i => i.Id));
        // 校验 Range 请求确实按 B-tree 协议发出
        Assert.Contains(handler.RequestedUrls, u => u.Contains("galleries.1786607455.index [bytes=0-463]"));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("galleries.1786607455.data"));
    }

    // ====================== gg.js 解析 ======================

    [Fact]
    public void GgParse_Reads_Default_Cases_And_B()
    {
        var body = """
        gg = { m: function(g) {
        var o = 1;
        switch (g) {
        case 276:
        case 3018:
        case 42:
        o = 0; break;
        }
        return o;
        },
        s: function(h) { return 1; },
        b: '1234567/'
        };
        """;

        var state = HitomiGgResolver.Parse(body, 0);

        Assert.Equal(1, state.MDefault);
        Assert.Equal(0, state.MMap[276]);
        Assert.Equal(0, state.MMap[3018]);
        Assert.Equal(0, state.MMap[42]);
        Assert.Equal("1234567/", state.B);
    }

    [Fact]
    public void S_Reverses_Hash_Tail()
    {
        // "…96f7" → 末位 "7" + 倒数 2-3 位 "6f" = "76f" = 1903
        Assert.Equal("1903", HitomiGgResolver.S(SampleHash));
        Assert.Equal("7/6f/" + SampleHash, HitomiGgResolver.RealFullPathFromHash(SampleHash));
    }
}





