using System.Text.Json;
using JmComic.Core.Models;
using JmComic.Core.Sources;
using JmComic.Core.Utils;

namespace JmComic.Core.Services;

/// <summary>
/// 本地漫画库服务：扫描本地目录中的已下载漫画（每个一级子目录视为一部漫画），
/// 并负责在下载时保存/读取专辑元数据（album.json）。
/// 约定：以 ".下载中-" 开头的目录视为未完成的下载，扫描时跳过。
/// </summary>
public class LocalLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ConfigService _config;

    public LocalLibraryService(ConfigService config)
    {
        _config = config;
    }

    public const string MetadataFileName = "album.json";

    /// <summary>原版 jmcomic-downloader（Tauri 版）生成的元数据文件，扫描时兼容读取。</summary>
    public const string LegacyMetadataFileName = "元数据.json";

    /// <summary>通用来源元数据（wnacg/hitomi 等非禁漫源下载时写入，用于识别源与站点 id）。</summary>
    public const string SourceMetadataFileName = "source.json";

    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    /// <summary>
    /// 递归展开目录：若某目录的子目录中还存在子目录（如「[xxx]合集」文件夹），
    /// 视为合集并展开其作品目录；普通漫画目录（子目录为章节）原样返回。
    /// 最多展开 4 层，避免意外的深层嵌套导致扫描过慢。
    /// </summary>
    private static List<string> ExpandComicDirs(string rootDir, int depth = 0)
    {
        if (depth > 4)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(rootDir))
        {
            var dirName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(dirName) || IsTempDownloadDir(dirName))
            {
                continue;
            }

            var children = Directory.EnumerateDirectories(dir)
                .Where(d => !IsTempDownloadDir(Path.GetFileName(d)))
                .ToList();

            var isCollection = children.Count > 0
                && children.Any(c => Directory.EnumerateDirectories(c).Any());

            if (isCollection)
            {
                result.AddRange(ExpandComicDirs(dir, depth + 1));
            }
            else
            {
                result.Add(dir);
            }
        }

        return result;
    }
    /// <summary>
    /// 扫描指定根目录，返回其中所有本地漫画（按修改时间倒序）。
    /// 默认只枚举目录（不统计图片文件数），保证大图库下扫描足够快；
    /// 需要图片数时传入 <paramref name="countImages"/> = true。
    /// </summary>
    public List<LocalComic> Scan(string rootDir, bool countImages = false)
    {
        var result = new List<LocalComic>();
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
        {
            return result;
        }

        foreach (var albumDir in ExpandComicDirs(rootDir))
        {
            var dirName = Path.GetFileName(albumDir);
            if (string.IsNullOrEmpty(dirName))
            {
                continue;
            }

            var metadata = TryReadMetadata(albumDir);
            var sourceMetadata = TryReadSourceMetadata(albumDir);
            var chapterDirs = Directory.EnumerateDirectories(albumDir)
                .Where(d => !IsTempDownloadDir(Path.GetFileName(d)))
                .ToList();

            if (chapterDirs.Count == 0)
            {
                // 只有元数据、没有实际章节的目录不算已完成漫画
                continue;
            }

            result.Add(BuildComic(albumDir, dirName, metadata, sourceMetadata, chapterDirs, countImages));
        }

        return result.OrderByDescending(c => c.ModifiedAt).ToList();
    }

    /// <summary>
    /// 增量扫描：复用缓存中「目录修改时间未变化」的条目，只重建新增/变化的目录。
    /// <paramref name="cache"/> 为该根目录上次扫描到的漫画列表；返回该根目录当前的完整列表。
    /// </summary>
    public List<LocalComic> ScanIncremental(string rootDir, IReadOnlyList<LocalComic> cache)
    {
        var result = new List<LocalComic>();
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
        {
            return result;
        }

        var cacheByPath = new Dictionary<string, LocalComic>(StringComparer.OrdinalIgnoreCase);
        foreach (var comic in cache)
        {
            if (!string.IsNullOrEmpty(comic.Path))
            {
                cacheByPath[comic.Path] = comic;
            }
        }

        foreach (var albumDir in ExpandComicDirs(rootDir))
        {
            var dirName = Path.GetFileName(albumDir);
            if (string.IsNullOrEmpty(dirName))
            {
                continue;
            }

            if (cacheByPath.TryGetValue(albumDir, out var cached)
                && cached.ModifiedAt == Directory.GetLastWriteTime(albumDir)
                && cached.MetadataStamp == GetMetadataStamp(albumDir))
            {
                // 目录未变化：直接复用缓存条目（含标签/封面），避免重复读元数据与枚举章节
                result.Add(cached);
                continue;
            }

            var metadata = TryReadMetadata(albumDir);
            var sourceMetadata = TryReadSourceMetadata(albumDir);
            var chapterDirs = Directory.EnumerateDirectories(albumDir)
                .Where(d => !IsTempDownloadDir(Path.GetFileName(d)))
                .ToList();

            if (chapterDirs.Count == 0)
            {
                // 只有元数据、没有实际章节的目录不算已完成漫画
                continue;
            }

            result.Add(BuildComic(albumDir, dirName, metadata, sourceMetadata, chapterDirs, false));
        }

        return result.OrderByDescending(c => c.ModifiedAt).ToList();
    }

    /// <summary>读取本地漫画库缓存；不存在或损坏时返回空缓存。</summary>
    public Dictionary<string, List<LocalComic>> LoadCache(string path)
    {
        var empty = new Dictionary<string, List<LocalComic>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
            {
                return empty;
            }
            var cache = JsonSerializer.Deserialize<LocalLibraryCache>(File.ReadAllText(path));
            return cache?.Roots is null
                ? empty
                : new Dictionary<string, List<LocalComic>>(cache.Roots, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return empty;
        }
    }

    /// <summary>保存本地漫画库缓存（原子写入：先写 .tmp 再覆盖）。</summary>
    public void SaveCache(string path, Dictionary<string, List<LocalComic>> roots)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var cache = new LocalLibraryCache { SavedAt = DateTime.Now, Roots = roots };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache, JsonOptions));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"保存本地漫画库缓存失败: {ex.Message}");
        }
    }

    /// <summary>对缓存复用导致缺少中文名的条目做本地提取补全（零成本、无网络请求），结果写回缓存对象与 album.json。</summary>
    public int BackfillExtractedNames(List<LocalComic> comics)
    {
        var filled = 0;
        foreach (var comic in comics)
        {
            if (!string.IsNullOrWhiteSpace(comic.NameCn))
            {
                continue;
            }

            var parsed = MangaFilenameParser.Parse(comic.Name);
            var nameCn = TitleTranslator.ExtractChineseName(parsed.Title.Length > 0 ? parsed.Title : comic.Name);
            if (string.IsNullOrWhiteSpace(nameCn))
            {
                continue;
            }

            comic.NameCn = nameCn;
            UpdateNameCn(comic.Path, nameCn);
            filled++;
        }
        return filled;
    }

    /// <summary>对缺少中文名的漫画批量翻译（并发限流），结果写回缓存对象并持久化到 album.json。</summary>
    public async Task<int> ApplyTranslationsAsync(List<LocalComic> comics, CancellationToken ct = default)
    {
        var options = _config.Current.TitleTranslate;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey) || comics.Count == 0)
        {
            return 0;
        }

        var translator = new TitleTranslator();
        var missing = comics.Where(c => string.IsNullOrWhiteSpace(c.NameCn)).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var tasks = missing.Select(async comic =>
        {
            var parsed = MangaFilenameParser.Parse(comic.Name);
            var translateInput = parsed.Title.Length > 0 ? parsed.Title : comic.Name;
            var translated = await translator.TranslateAsync(translateInput, options, ct);
            if (string.IsNullOrWhiteSpace(translated))
            {
                return false;
            }

            comic.NameCn = translated;
            UpdateNameCn(comic.Path, translated);
            return true;
        });
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r);
    }

    /// <summary>把中文名写回本版元数据 album.json（供后续离线读取）；不存在 album.json 时跳过。</summary>
    private static void UpdateNameCn(string albumDir, string nameCn)
    {
        if (string.IsNullOrWhiteSpace(albumDir) || string.IsNullOrWhiteSpace(nameCn))
        {
            return;
        }

        var path = Path.Combine(albumDir, MetadataFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<AlbumMetadata>(File.ReadAllText(path));
            if (metadata is null || metadata.NameCn == nameCn)
            {
                return;
            }

            metadata.NameCn = nameCn;
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"写入中文名失败: {ex.Message}");
        }
    }

    /// <summary>把专辑元数据写入本地目录（下载时调用，供本地模式离线展示标签/作者）。</summary>
    /// <summary>根据下载根目录与专辑，在专辑目录写入元数据（目录名与下载引擎保持一致）。</summary>
    public void SaveMetadataForAlbum(string downloadDir, Album album)
    {
        if (string.IsNullOrWhiteSpace(downloadDir))
        {
            return;
        }
        SaveMetadata(Path.Combine(downloadDir, FilenameFilter.Filter(album.Name)), album);
    }

    public void SaveMetadata(string albumDir, Album album)
    {
        if (string.IsNullOrWhiteSpace(albumDir))
        {
            return;
        }
        Directory.CreateDirectory(albumDir);
        WriteMetadata(albumDir, BuildMetadata(album, ""));
    }

    /// <summary>读取专辑元数据；不存在或损坏时返回 null。</summary>
    public AlbumMetadata? ReadMetadata(string albumDir)
        => TryReadMetadata(albumDir);

    /// <summary>
    /// 用 API 最新数据补全专辑元数据（补全缺失字段）：保留已有的 nameCn（翻译/提取结果），
    /// 其余字段以 API 返回为准，不重建目录名。
    /// </summary>
    public void SaveMetadataFromApi(string albumDir, Album album)
    {
        if (string.IsNullOrWhiteSpace(albumDir) || album is null)
        {
            return;
        }
        Directory.CreateDirectory(albumDir);
        var existing = TryReadMetadata(albumDir);
        WriteMetadata(albumDir, BuildMetadata(album, existing?.NameCn ?? ""));
    }

    private static AlbumMetadata BuildMetadata(Album album, string nameCn) => new()
    {
        Id = album.Id,
        Name = album.Name,
        NameCn = nameCn,
        Tags = album.Tags,
        Author = album.Author,
        Works = album.Works,
        Actors = album.Actors,
        Description = album.Description,
        Addtime = album.Addtime,
        TotalViews = album.TotalViews,
        Likes = album.Likes,
        CommentTotal = album.CommentTotal,
        SeriesId = album.SeriesId,
        Series = album.Series,
        ChapterInfos = album.ChapterInfos,
        RelatedList = album.RelatedList,
        Liked = album.Liked,
        IsFavorite = album.IsFavorite,
        IsAids = album.IsAids,
    };

    private static void WriteMetadata(string albumDir, AlbumMetadata metadata)
    {
        var target = Path.Combine(albumDir, MetadataFileName);
        var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(metadata, JsonOptions));
        File.Move(temp, target, true);
    }

    private static LocalComic BuildComic(
        string albumDir, string dirName, AlbumMetadata? metadata, SourceMetadata? sourceMetadata,
        List<string> chapterDirs, bool countImages)
    {
        var chapterCount = chapterDirs.Count;
        var imageCount = 0L;
        if (countImages)
        {
            foreach (var chapterDir in chapterDirs)
            {
                imageCount += CountImages(chapterDir);
            }
        }

        // 无元数据时用文件名解析器兜底：干净标题 + 作者/组/汉化等标签（离线、零成本）
        var parsed = MangaFilenameParser.Parse(dirName);
        return new LocalComic
        {
            SourceId = sourceMetadata?.SourceId ?? "",
            AlbumId = metadata?.Id,
            Name = metadata is null && parsed.Title.Length > 0 ? parsed.Title : dirName,
            NameCn = metadata?.NameCn is { Length: > 0 } nameCn
                ? nameCn
                : BuildFallbackNameCn(dirName, parsed.Title),
            Path = albumDir,
            CoverPath = FindCover(albumDir, chapterDirs),
            Tags = metadata?.Tags is { Count: > 0 } tags ? tags : BuildFallbackTags(parsed.Tags),
            Author = metadata?.Author is { Count: > 0 } author ? author : BuildFallbackAuthors(parsed.Tags),
            ChapterCount = chapterCount,
            ImageCount = imageCount,
            ModifiedAt = Directory.GetLastWriteTime(albumDir),
            MetadataStamp = GetMetadataStamp(albumDir),
            HasMetadata = metadata is not null,
        };
    }

    /// <summary>无元数据时用解析出的干净标题提取中文名（解析器已剥离括号噪音）。</summary>
    private static string BuildFallbackNameCn(string dirName, string parsedTitle)
    {
        var fromParsed = TitleTranslator.ExtractChineseName(parsedTitle);
        return fromParsed.Length > 0 ? fromParsed : TitleTranslator.ExtractChineseName(dirName);
    }

    /// <summary>无元数据时用解析标签兜底：作者独立成字段，其余保留「类别:值」格式供标签筛选。</summary>
    private static List<string> BuildFallbackTags(IEnumerable<string> parsedTags)
        => parsedTags
            .Where(t => !t.StartsWith("作者:", StringComparison.Ordinal)
                        && !t.StartsWith("标题:", StringComparison.Ordinal))
            .ToList();

    /// <summary>无元数据时从解析标签中提取作者列表（去掉「作者:」前缀）。</summary>
    private static List<string> BuildFallbackAuthors(IEnumerable<string> parsedTags)
        => parsedTags
            .Where(t => t.StartsWith("作者:", StringComparison.Ordinal))
            .Select(t => t["作者:".Length..])
            .ToList();

    /// <summary>读取专辑元数据：优先 album.json（本版格式），缺失时兼容读取原版「元数据.json」。</summary>
    private static AlbumMetadata? TryReadMetadata(string albumDir)
    {
        var primaryPath = Path.Combine(albumDir, MetadataFileName);
        if (File.Exists(primaryPath))
        {
            try
            {
                return JsonSerializer.Deserialize<AlbumMetadata>(File.ReadAllText(primaryPath));
            }
            catch
            {
                // 主元数据损坏时忽略，继续尝试兼容格式
            }
        }

        var legacyPath = Path.Combine(albumDir, LegacyMetadataFileName);
        if (File.Exists(legacyPath))
        {
            return TryReadLegacyMetadata(legacyPath);
        }
        return null;
    }

    /// <summary>宽容解析原版「元数据.json」：id 支持数字或字符串，缺失/类型不符的字段按空值处理。</summary>
    private static AlbumMetadata? TryReadLegacyMetadata(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new AlbumMetadata
            {
                Id = ReadId(root),
                Name = ReadString(root, "name") ?? "",
                NameCn = ReadString(root, "nameCn") ?? ReadString(root, "name_cn") ?? "",
                Tags = ReadStringArray(root, "tags"),
                Author = ReadStringArray(root, "author"),
                Works = ReadStringArray(root, "works"),
                Actors = ReadStringArray(root, "actors"),
                Description = ReadString(root, "description") ?? "",
            };
        }
        catch
        {
            return null;
        }
    }

    private static long ReadId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var id))
        {
            if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number))
            {
                return number;
            }
            if (id.ValueKind == JsonValueKind.String && long.TryParse(id.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        return 0;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }
        return null;
    }

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        var list = new List<string>();
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    list.Add(item.GetString()!);
                }
            }
        }
        return list;
    }

    /// <summary>封面查找：优先专辑目录下的 cover.*，否则取第一个章节的第一张图片。</summary>
    private static string FindCover(string albumDir, List<string> chapterDirs)
    {
        foreach (var ext in CoverExtensions)
        {
            var cover = Path.Combine(albumDir, "cover" + ext);
            if (File.Exists(cover))
            {
                return cover;
            }
        }

        var firstChapter = chapterDirs
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (firstChapter is null)
        {
            return "";
        }

        var firstImage = Directory.EnumerateFiles(firstChapter)
            .Where(f => CoverExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return firstImage ?? "";
    }

    private static long CountImages(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .LongCount(f => CoverExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>元数据文件时间戳：取 album.json / 元数据.json 中较新的最后修改时间；都没有则为 null。</summary>
    private static DateTime? GetMetadataStamp(string albumDir)
    {
        DateTime? stamp = null;
        foreach (var name in new[] { MetadataFileName, LegacyMetadataFileName, SourceMetadataFileName })
        {
            var path = Path.Combine(albumDir, name);
            if (File.Exists(path))
            {
                var time = File.GetLastWriteTime(path);
                if (stamp is null || time > stamp)
                {
                    stamp = time;
                }
            }
        }
        return stamp;
    }

    /// <summary>通用来源元数据：下载非禁漫源漫画时写入（目录名与下载引擎保持一致）。</summary>
    public void SaveSourceMetadata(string downloadDir, string sourceId, ComicDetail detail)
    {
        if (string.IsNullOrWhiteSpace(downloadDir) || detail is null || string.IsNullOrWhiteSpace(detail.Title))
        {
            return;
        }
        var albumDir = Path.Combine(downloadDir, FilenameFilter.Filter(detail.Title));
        Directory.CreateDirectory(albumDir);

        var metadata = new SourceMetadata
        {
            SourceId = sourceId,
            ComicId = detail.Id,
            Title = detail.Title,
            Authors = detail.Authors,
            Tags = detail.Tags,
            CoverUrl = detail.CoverUrl,
            Description = detail.Description,
        };
        var target = Path.Combine(albumDir, SourceMetadataFileName);
        var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(metadata, JsonOptions));
        File.Move(temp, target, true);
    }

    /// <summary>读取通用来源元数据；不存在或损坏时返回 null。</summary>
    public SourceMetadata? ReadSourceMetadata(string albumDir)
    {
        try
        {
            var path = Path.Combine(albumDir, SourceMetadataFileName);
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<SourceMetadata>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private SourceMetadata? TryReadSourceMetadata(string albumDir) => ReadSourceMetadata(albumDir);

    /// <summary>扫描下载目录，收集「已下载」漫画的 (源, id) 键集合（无 source.json 的旧版下载回退为禁漫 jm）。</summary>
    public HashSet<string> GetDownloadedKeys(string downloadDir)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(downloadDir) || !Directory.Exists(downloadDir))
        {
            return keys;
        }

        foreach (var albumDir in ExpandComicDirs(downloadDir))
        {
            var dirName = Path.GetFileName(albumDir);
            if (string.IsNullOrEmpty(dirName))
            {
                continue;
            }
            var chapterDirs = Directory.EnumerateDirectories(albumDir)
                .Where(d => !IsTempDownloadDir(Path.GetFileName(d)))
                .ToList();
            if (chapterDirs.Count == 0)
            {
                continue;
            }

            var sourceMetadata = TryReadSourceMetadata(albumDir);
            var metadata = TryReadMetadata(albumDir);
            var sourceId = sourceMetadata?.SourceId is { Length: > 0 } sid ? sid : "jm";
            var comicId = sourceMetadata?.ComicId is { Length: > 0 } cid
                ? cid
                : metadata?.Id is > 0 ? metadata.Id.ToString() : null;
            if (comicId is not null)
            {
                keys.Add(KeyFor(sourceId, comicId));
            }
        }
        return keys;
    }

    /// <summary>「已下载」键格式：{sourceId}:{comicId}，列表页卡片按源与 id 直接匹配。</summary>
    public static string KeyFor(string sourceId, string comicId) => $"{sourceId}:{comicId}";
    private static bool IsTempDownloadDir(string name) => name.StartsWith(".下载中-", StringComparison.Ordinal);
}





