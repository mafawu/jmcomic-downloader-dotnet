using JmComic.Core.Sources;

namespace JmComic.Core.Tests;

/// <summary>聚合搜索：并发合并、失败隔离、单源命中。</summary>
public class AggregateSearchServiceTests
{
    private sealed class FakeSource : IComicSource
    {
        public FakeSource(string id, SearchResult? result, Exception? error = null, bool requiresLogin = false, Action? onSearch = null)
        {
            Info = new ComicSourceInfo { Id = id, DisplayName = id, RequiresLogin = requiresLogin };
            Result = result;
            Error = error;
            OnSearch = onSearch;
        }

        public ComicSourceInfo Info { get; }
        public SearchResult? Result { get; }
        public Exception? Error { get; }
        public Action? OnSearch { get; }

        public Task<SearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
        {
            OnSearch?.Invoke();
            if (Error is not null)
            {
                throw Error;
            }
            return Task.FromResult(Result ?? new SearchResult());
        }

        public Task<ComicDetail> GetComicAsync(string comicId, CancellationToken ct = default)
            => Task.FromResult(new ComicDetail());

        public Task<IReadOnlyList<ImagePage>> GetChapterImagesAsync(Chapter chapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ImagePage>>(Array.Empty<ImagePage>());
    }

    private static SearchResult ResultWith(string id, string title) => new()
    {
        Total = 1,
        TotalPages = 1,
        Items = new List<ComicSummary>
        {
            new() { Id = id, Title = title, CoverUrl = "https://x/" + id },
        },
    };

    [Fact]
    public async Task Merges_Results_From_All_Sources()
    {
        var service = new AggregateSearchService(new IComicSource[]
        {
            new FakeSource("a", ResultWith("1", "A1")),
            new FakeSource("b", ResultWith("2", "B1")),
        });

        var result = await service.SearchAsync("x", 1);

        Assert.Equal(2, result.Groups.Count);
        Assert.Equal("A1", result.Groups[0].Result!.Items[0].Title);
        Assert.Equal("B1", result.Groups[1].Result!.Items[0].Title);
        Assert.All(result.Groups, g => Assert.Null(g.Error));
    }

    [Fact]
    public async Task Isolates_Failed_Source()
    {
        var service = new AggregateSearchService(new IComicSource[]
        {
            new FakeSource("a", ResultWith("1", "A1")),
            new FakeSource("b", null, new InvalidOperationException("站点挂了")),
        });

        var result = await service.SearchAsync("x", 1);

        Assert.Null(result.Groups[0].Error);
        Assert.Equal("站点挂了", result.Groups[1].Error);
        Assert.Null(result.Groups[1].Result);
    }

    [Fact]
    public void Excludes_Login_Required_Sources()
    {
        var service = new AggregateSearchService(new IComicSource[]
        {
            new FakeSource("free", ResultWith("1", "A")),
            new FakeSource("paid", ResultWith("2", "B"), requiresLogin: true),
        });

        Assert.Single(service.Sources);
        Assert.Equal("free", service.Sources[0].Info.Id);
    }

    [Fact]
    public async Task TotalPages_Uses_Max_Across_Sources()
    {
        var service = new AggregateSearchService(new IComicSource[]
        {
            new FakeSource("a", new SearchResult { TotalPages = 1 }),
            new FakeSource("b", new SearchResult { TotalPages = 5 }),
        });

        var result = await service.SearchAsync("x", 1);

        Assert.Equal(5, result.TotalPages);
    }

    [Fact]
    public async Task SearchSourceAsync_Searches_Only_Requested_Source()
    {
        var searched = new List<string>();
        var sourceA = new FakeSource("a", ResultWith("1", "A1"), onSearch: () => searched.Add("a"));
        var sourceB = new FakeSource("b", ResultWith("2", "B1"), onSearch: () => searched.Add("b"));
        var service = new AggregateSearchService(new IComicSource[] { sourceA, sourceB });

        var group = await service.SearchSourceAsync(sourceA, "x", 1);

        Assert.Equal("a", group.Source.Info.Id);
        Assert.Null(group.Error);
        Assert.Equal("A1", group.Result!.Items[0].Title);
        Assert.Equal(new[] { "a" }, searched);
    }

    [Fact]
    public async Task SearchSourceAsync_Isolates_Failure()
    {
        var bad = new FakeSource("b", null, new InvalidOperationException("站点挂了"));
        var service = new AggregateSearchService(new IComicSource[]
        {
            new FakeSource("a", ResultWith("1", "A1")),
            bad,
        });

        var group = await service.SearchSourceAsync(bad, "x", 1);

        Assert.Null(group.Result);
        Assert.Equal("站点挂了", group.Error);
    }
}