using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Utils;

namespace JmComic.Core.Sources.Jm;

/// <summary>
/// 禁漫天堂（18comic）内容源：把 JmHttpClient 的站点特有实现适配到 IComicSource 通用接口。
/// 搜索、详情、章节图片地址（含 scramble_id / block_num / 图片域名 / UA）均收敛在此。
/// </summary>
public class JmSource : IComicSource
{
    private readonly JmHttpClient _client;

    public JmSource(JmHttpClient client)
    {
        _client = client;
    }

    public ComicSourceInfo Info { get; } = new()
    {
        Id = "jm",
        DisplayName = "禁漫天堂",
        RequiresLogin = false,
        SupportsSearchSort = true,
        SupportsCategories = true,
        SupportsRank = true,
        SupportsWeekly = true,
        SupportsFavorites = true,
        MaxImageConcurrency = 40,
        MaxChapterConcurrency = 3,
        MaxUrlFetchConcurrency = 10,
    };

    public async Task<SearchResult> SearchAsync(string keyword, int page, CancellationToken ct = default)
    {
        var resp = await _client.SearchAsync(keyword, page, SearchSort.Latest, ct);
        if (resp.AlbumRespData is { } album)
        {
            return new SearchResult
            {
                Total = 1,
                SingleComicId = album.Id.ToString(),
            };
        }

        var data = resp.SearchRespData
                   ?? throw new JmException("搜索响应缺少 SearchRespData 或 AlbumRespData");
        // 每页 24 条，向上取整得到总页数
        var totalPages = data.Total <= 0 ? 1 : (data.Total + 23) / 24;
        return new SearchResult
        {
            Items = data.Content.Select(ToSummary).ToList(),
            Total = data.Total,
            TotalPages = totalPages,
        };
    }

    /// <summary>获取禁漫原始专辑数据（保留站点原始字段，供本地元数据保存等场景使用）。</summary>
    public Task<AlbumRespData> GetAlbumRawAsync(string comicId, CancellationToken ct = default)
        => _client.GetAlbumAsync(long.Parse(comicId), ct);
    public async Task<ComicDetail> GetComicAsync(string comicId, CancellationToken ct = default)
    {
        var album = await _client.GetAlbumAsync(long.Parse(comicId), ct);
        return ToComicDetail(album);
    }

    public async Task<IReadOnlyList<ImagePage>> GetChapterImagesAsync(Chapter chapter, CancellationToken ct = default)
    {
        var chapterId = chapter.NumericId ?? long.Parse(chapter.Id);
        var scrambleId = await _client.GetScrambleIdAsync(chapterId, ct);
        var chapterRespData = await _client.GetChapterAsync(chapterId, ct);

        var pages = new List<ImagePage>();
        foreach (var filename in chapterRespData.Images)
        {
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            if (ext != ".webp")
            {
                continue;
            }
            var filenameWithoutExt = Path.GetFileNameWithoutExtension(filename);
            var blockNum = BlockNumCalculator.Calculate(scrambleId, chapterId, filenameWithoutExt);
            var url = $"https://{JmConstants.ImageDomain}/media/photos/{chapterId}/{filename}";
            pages.Add(new ImagePage
            {
                Url = url,
                BlockNum = blockNum,
                Headers = new Dictionary<string, string>
                {
                    ["User-Agent"] = JmConstants.UserAgent,
                },
            });
        }
        return pages;
    }

    private static ComicSummary ToSummary(AlbumInSearchRespData item) => new()
    {
        Id = item.Id,
        Title = item.Name,
        Author = item.Author,
        Category = item.Category?.Title ?? "",
        CoverUrl = ToAbsoluteUrl(item.Image),
    };

    /// <summary>禁漫封面规范化：相对路径补全为绝对 URL（老页面/本地面板复用）。</summary>
    public static string NormalizeCover(long albumId, string image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return $"https://{JmConstants.ImageDomain}/media/albums/{albumId}_3x4.jpg";
        }
        if (image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return image;
        }
        return $"https://{JmConstants.ImageDomain}{image.TrimStart('/')}";
    }

    /// <summary>把站点返回的相对图片路径补全为绝对 URL。</summary>
    private static string ToAbsoluteUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        return $"https://{JmConstants.ImageDomain}/{path.TrimStart('/')}";
    }

    /// <summary>
    /// 组装通用漫画详情。章节标题规则与 AlbumBuilder 保持一致（"第N话 [名称]"）。
    /// </summary>
    private static ComicDetail ToComicDetail(AlbumRespData album)
    {
        var albumTitle = FilenameFilter.Filter(album.Name);

        var chapters = new List<Chapter>();
        foreach (var series in album.Series)
        {
            if (!long.TryParse(series.Id, out var chapterId))
            {
                continue;
            }
            var chapterTitle = $"第{series.Sort}话";
            if (!string.IsNullOrEmpty(series.Name))
            {
                chapterTitle += $" {FilenameFilter.Filter(series.Name)}";
            }
            chapters.Add(new Chapter
            {
                Id = series.Id,
                NumericId = chapterId,
                Title = chapterTitle,
                ComicId = album.Id.ToString(),
                SourceId = "jm",
                ComicTitle = albumTitle,
            });
        }

        // 没有章节信息时添加默认章节（与 AlbumBuilder 一致）
        if (chapters.Count == 0)
        {
            chapters.Add(new Chapter
            {
                Id = album.Id.ToString(),
                NumericId = album.Id,
                Title = "第1话",
                ComicId = album.Id.ToString(),
                SourceId = "jm",
                ComicTitle = albumTitle,
            });
        }

        return new ComicDetail
        {
            Id = album.Id.ToString(),
            Title = albumTitle,
            Description = album.Description,
            Authors = album.Author,
            Tags = album.Tags,
            Chapters = chapters,
        };
    }
}



