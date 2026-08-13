namespace JmComic.Core.Sources;

/// <summary>聚合搜索：一个源的结果分组。</summary>
public class SourceSearchGroup
{
    public required IComicSource Source { get; init; }

    /// <summary>null 表示该源搜索失败（Error 非空）。</summary>
    public SearchResult? Result { get; init; }

    /// <summary>该源搜索失败时的错误消息；null 表示成功。</summary>
    public string? Error { get; init; }
}

/// <summary>聚合搜索结果：按源分组，每源一页。</summary>
public class AggregateSearchResult
{
    public IReadOnlyList<SourceSearchGroup> Groups { get; init; } = Array.Empty<SourceSearchGroup>();
    public int Page { get; init; }

    /// <summary>所有源的最大总页数，用于聚合分页。</summary>
    public long TotalPages => Groups.Count == 0
        ? 1
        : Groups.Max(g => g.Result?.TotalPages ?? 1);
}

/// <summary>
/// 聚合搜索服务：并发查询所有免登录源，失败隔离（单源异常不影响其他源）。
/// </summary>
public class AggregateSearchService
{
    private readonly IReadOnlyList<IComicSource> _sources;

    public AggregateSearchService(IEnumerable<IComicSource> sources)
    {
        _sources = sources.Where(s => !s.Info.RequiresLogin).ToList();
    }

    public IReadOnlyList<IComicSource> Sources => _sources;

    public async Task<AggregateSearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
    {
        var tasks = _sources.Select(s => SearchOneAsync(s, keyword, page, ct)).ToArray();
        var groups = await Task.WhenAll(tasks);
        return new AggregateSearchResult { Groups = groups, Page = page };
    }

    /// <summary>单源搜索（失败隔离）：搜索页切换源时补搜缺失源用，失败不抛异常。</summary>
    public Task<SourceSearchGroup> SearchSourceAsync(
        IComicSource source, string keyword, int page, CancellationToken ct = default)
        => SearchOneAsync(source, keyword, page, ct);

    private static async Task<SourceSearchGroup> SearchOneAsync(
        IComicSource source, string keyword, int page, CancellationToken ct)
    {
        try
        {
            var result = await source.SearchAsync(keyword, page, ct);
            return new SourceSearchGroup { Source = source, Result = result };
        }
        catch (Exception ex)
        {
            return new SourceSearchGroup { Source = source, Error = ex.Message };
        }
    }
}

