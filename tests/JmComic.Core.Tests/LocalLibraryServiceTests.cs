using JmComic.Core.Models;
using JmComic.Core.Services;
using Xunit;

namespace JmComic.Core.Tests;

public class LocalLibraryServiceTests
{
    private static string CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-local-lib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string path, string content = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static LocalLibraryService CreateService()
    {
        var configDir = Path.Combine(Path.GetTempPath(), "jm-local-lib-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        var configService = new ConfigService(Path.Combine(configDir, "config.json"));
        return new LocalLibraryService(configService);
    }

    [Fact]
    public void Scan_ReturnsEmpty_WhenRootMissing()
    {
        var service = CreateService();
        var result = service.Scan(Path.Combine(Path.GetTempPath(), "jm-not-exists-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_SkipsTempDownloadDirs()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "漫画A", "第1话"));
            Directory.CreateDirectory(Path.Combine(root, "漫画B", ".下载中-第1话"));

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal("漫画A", comic.Name);
            Assert.Equal(1, comic.ChapterCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_SkipsAlbumWithoutChapters()
    {
        var root = CreateTempRoot();
        try
        {
            // 只有元数据、没有章节的目录不算已完成漫画
            Directory.CreateDirectory(Path.Combine(root, "占位"));
            WriteFile(Path.Combine(root, "占位", LocalLibraryService.MetadataFileName), """{"id":1,"name":"占位"}""");

            var result = CreateService().Scan(root);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_ReadsMetadataTagsAndAuthor()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "测试漫画");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.MetadataFileName),
                """{"id":42,"name":"测试漫画","tags":["纯爱","汉化"],"author":["作者A"]}""");

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal(42, comic.AlbumId);
            Assert.Equal(new[] { "纯爱", "汉化" }, comic.Tags);
            Assert.Equal(new[] { "作者A" }, comic.Author);
            Assert.True(comic.HasMetadata);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_FindsCover_FromFirstChapterFirstImage()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "封面漫画");
            WriteFile(Path.Combine(albumDir, "第2话", "005.jpg"));
            WriteFile(Path.Combine(albumDir, "第1话", "003.png"));
            WriteFile(Path.Combine(albumDir, "第1话", "001.jpg"));
            WriteFile(Path.Combine(albumDir, "第1话", "说明.txt"));

            var result = CreateService().Scan(root, countImages: true);

            var comic = Assert.Single(result);
            Assert.Equal("001.jpg", Path.GetFileName(comic.CoverPath));
            Assert.Equal(2, comic.ChapterCount);
            Assert.Equal(3, comic.ImageCount); // 001.jpg + 003.png + 005.jpg

            // 默认不统计图片数（大图库性能优化）
            Assert.Equal(0, CreateService().Scan(root).Single().ImageCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_PrefersCoverFile_InAlbumDir()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "封面漫画");
            WriteFile(Path.Combine(albumDir, "cover.jpg"));
            WriteFile(Path.Combine(albumDir, "第1话", "001.jpg"));

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal("cover.jpg", Path.GetFileName(comic.CoverPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveMetadata_RoundTrips()
    {
        var root = CreateTempRoot();
        try
        {
            var service = CreateService();
            var album = new Album
            {
                Id = 7,
                Name = "元数据漫画",
                Tags = new List<string> { "标签1", "标签2" },
                Author = new List<string> { "作者B" },
            };
            service.SaveMetadataForAlbum(root, album);

            var albumDir = Path.Combine(root, "元数据漫画");
            var metadataPath = Path.Combine(albumDir, LocalLibraryService.MetadataFileName);
            Assert.True(File.Exists(metadataPath));

            // 只有元数据、没有章节时不算漫画
            Assert.Empty(service.Scan(root));

            // 加入章节后，扫描能读回元数据
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            var comic = Assert.Single(service.Scan(root));
            Assert.Equal(7, comic.AlbumId);
            Assert.Equal(new[] { "标签1", "标签2" }, comic.Tags);
            Assert.Equal(new[] { "作者B" }, comic.Author);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_ReadsLegacyMetadataJson_WithNumericId()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "原版漫画");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.LegacyMetadataFileName),
                """{"id":258148,"name":"原版漫画","tags":["全彩","巨乳","NTR"],"author":["K-てん"],"works":[],"actors":[],"description":""}""");

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal(258148, comic.AlbumId);
            Assert.Equal(new[] { "全彩", "巨乳", "NTR" }, comic.Tags);
            Assert.Equal(new[] { "K-てん" }, comic.Author);
            Assert.True(comic.HasMetadata);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_ReadsLegacyMetadataJson_WithStringId()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "原版漫画2");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.LegacyMetadataFileName),
                """{"id":"12345","name":"原版漫画2","tags":["純愛"],"author":["作者X"]}""");

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal(12345, comic.AlbumId);
            Assert.Equal(new[] { "純愛" }, comic.Tags);
            Assert.Equal(new[] { "作者X" }, comic.Author);
            Assert.True(comic.HasMetadata);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_PrefersAlbumJson_OverLegacyMetadataJson()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "双元数据漫画");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.MetadataFileName),
                """{"id":1,"name":"新","tags":["新版标签"]}""");
            WriteFile(Path.Combine(albumDir, LocalLibraryService.LegacyMetadataFileName),
                """{"id":2,"name":"旧","tags":["旧版标签"]}""");

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal(1, comic.AlbumId);
            Assert.Equal(new[] { "新版标签" }, comic.Tags);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanIncremental_ReusesUnchangedAndTracksChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var service = CreateService();
            var albumA = Path.Combine(root, "漫画A");
            Directory.CreateDirectory(Path.Combine(albumA, "第1话"));
            WriteFile(Path.Combine(albumA, LocalLibraryService.MetadataFileName), """{"id":1,"name":"漫画A","tags":["标签A"]}""");
            var albumB = Path.Combine(root, "漫画B");
            Directory.CreateDirectory(Path.Combine(albumB, "第1话"));
            WriteFile(Path.Combine(albumB, LocalLibraryService.MetadataFileName), """{"id":2,"name":"漫画B","tags":["标签B"]}""");

            var full = service.Scan(root);
            Assert.Equal(2, full.Count);

            // 未变化：增量复用缓存，结果一致
            var incremental = service.ScanIncremental(root, full);
            Assert.Equal(2, incremental.Count);
            Assert.Equal(full.OrderBy(c => c.Name).Select(c => c.Tags.Single()),
                incremental.OrderBy(c => c.Name).Select(c => c.Tags.Single()));

            // 新增目录：增量能扫到
            var albumC = Path.Combine(root, "漫画C");
            Directory.CreateDirectory(Path.Combine(albumC, "第1话"));
            WriteFile(Path.Combine(albumC, LocalLibraryService.MetadataFileName), """{"id":3,"name":"漫画C","tags":["标签C"]}""");
            incremental = service.ScanIncremental(root, incremental);
            Assert.Equal(3, incremental.Count);
            Assert.Contains(incremental, c => c.Name == "漫画C");

            // 元数据变化：增量重建并读到新标签
            WriteFile(Path.Combine(albumA, LocalLibraryService.MetadataFileName), """{"id":1,"name":"漫画A","tags":["新标签A"]}""");
            incremental = service.ScanIncremental(root, incremental);
            Assert.Equal("新标签A", incremental.Single(c => c.Name == "漫画A").Tags.Single());

            // 删除目录：增量结果中消失
            Directory.Delete(albumB, true);
            incremental = service.ScanIncremental(root, incremental);
            Assert.Equal(2, incremental.Count);
            Assert.DoesNotContain(incremental, c => c.Name == "漫画B");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanIncremental_BackfillsImageCount_WhenCacheLacksCounts()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "漫画A");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, "第1话", "001.jpg"));
            WriteFile(Path.Combine(albumDir, "第1话", "002.jpg"));

            var service = CreateService();
            var full = service.Scan(root); // 默认不统计图片数
            Assert.Equal(0, full.Single().ImageCount);

            // 缓存条目缺少图片数：增量扫描自动重建并补全总页数
            var incremental = service.ScanIncremental(root, full);
            var comic = incremental.Single();
            Assert.Equal(2, comic.ImageCount);

            // 补全后再次增量扫描直接复用缓存，不再重复重建
            var second = service.ScanIncremental(root, incremental);
            Assert.Equal(2, second.Single().ImageCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public void Cache_SaveAndLoad_RoundTrips()
    {
        var root = CreateTempRoot();
        try
        {
            var service = CreateService();
            var albumDir = Path.Combine(root, "缓存漫画");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.MetadataFileName), """{"id":9,"name":"缓存漫画","tags":["标签X","标签Y"]}""");
            var comics = service.Scan(root);

            var cachePath = Path.Combine(root, "cache.json");
            var roots = new Dictionary<string, List<LocalComic>>(StringComparer.OrdinalIgnoreCase) { [root] = comics };
            service.SaveCache(cachePath, roots);

            var loaded = service.LoadCache(cachePath);
            Assert.True(loaded.ContainsKey(root));
            var comic = Assert.Single(loaded[root]);
            Assert.Equal("缓存漫画", comic.Name);
            Assert.Equal(new[] { "标签X", "标签Y" }, comic.Tags);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_ExpandsCollectionDirs()
    {
        var root = CreateTempRoot();
        try
        {
            // 合集：一级目录下是作品，作品内才是章节
            Directory.CreateDirectory(Path.Combine(root, "[菜雞喵]合集", "作品A", "第1话"));
            Directory.CreateDirectory(Path.Combine(root, "[菜雞喵]合集", "作品B", "第1话"));
            WriteFile(Path.Combine(root, "[菜雞喵]合集", "作品A", "cover.jpg"));
            // 普通漫画（名字带合集但子目录是章节）不受影响
            Directory.CreateDirectory(Path.Combine(root, "病娇妹妹上下篇合集", "第1话"));

            var result = CreateService().Scan(root);

            Assert.Equal(3, result.Count);
            Assert.Contains(result, c => c.Name == "作品A");
            Assert.Contains(result, c => c.Name == "作品B");
            Assert.Contains(result, c => c.Name == "病娇妹妹上下篇合集");
            Assert.DoesNotContain(result, c => c.Name.Contains("菜雞喵"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanIncremental_ExpandsAndDetectsNewCollectionItems()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "[合集]", "作品A", "第1话"));
            Directory.CreateDirectory(Path.Combine(root, "[合集]", "作品B", "第1话"));

            var service = CreateService();
            var first = service.Scan(root);
            Assert.Equal(2, first.Count);

            // 增量扫描复用缓存
            var second = service.ScanIncremental(root, first);
            Assert.Equal(2, second.Count);

            // 合集内新增作品，增量扫描能发现
            Directory.CreateDirectory(Path.Combine(root, "[合集]", "作品C", "第1话"));
            var third = service.ScanIncremental(root, second);
            Assert.Equal(3, third.Count);
            Assert.Contains(third, c => c.Name == "作品C");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public void Scan_FallsBackToParsedMetadata_WhenAlbumJsonMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "[汉化组 (作者名)] (C97) 作品标题 [中文]");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, "第1话", "001.jpg"));

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal("作品标题", comic.Name);           // 干净标题
            Assert.Equal("作品标题", comic.NameCn);         // 中文标题离线提取
            Assert.Equal(new[] { "作者名" }, comic.Author); // 作者兜底
            Assert.Contains("组:汉化组", comic.Tags);
            Assert.Contains("会场:C97", comic.Tags);
            Assert.Contains("其他:中文", comic.Tags);
            Assert.False(comic.HasMetadata);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_KeepsParsedJapaneseName_AndLeavesTranslationToBackfill()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "[社团 (作者)] タイトル");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal("タイトル", comic.Name);
            Assert.Equal("", comic.NameCn); // 无中文片段，留给在线翻译
            Assert.Equal(new[] { "作者" }, comic.Author);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_MetadataWinsOverParsedFallback()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "[汉化组 (作者名)] (C97) 作品标题 [中文]");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.MetadataFileName),
                """{"id":7,"name":"网站原名","nameCn":"官方中文名","tags":["纯爱"],"author":["站内作者"]}""");

            var result = CreateService().Scan(root);

            var comic = Assert.Single(result);
            Assert.Equal("[汉化组 (作者名)] (C97) 作品标题 [中文]", comic.Name); // 有元数据时 Name 保持目录名
            Assert.Equal("官方中文名", comic.NameCn);
            Assert.Equal(new[] { "纯爱" }, comic.Tags);
            Assert.Equal(new[] { "站内作者" }, comic.Author);
            Assert.True(comic.HasMetadata);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scan_ReadsSourceId_From_SourceJson()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "绅士作品");
            Directory.CreateDirectory(Path.Combine(albumDir, "全一册"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.SourceMetadataFileName),
                """{"source_id":"wnacg","comic_id":"12345","title":"绅士作品"}""");

            var comic = Assert.Single(CreateService().Scan(root));

            Assert.Equal("wnacg", comic.SourceId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveSourceMetadata_Then_GetDownloadedKeys_Matches()
    {
        var root = CreateTempRoot();
        try
        {
            var service = CreateService();
            var detail = new JmComic.Core.Sources.ComicDetail
            {
                Id = "987",
                Title = "测试画廊",
                Authors = { "作者A" },
                Tags = { "tag1" },
            };
            service.SaveSourceMetadata(root, "hitomi", detail);
            Directory.CreateDirectory(Path.Combine(root, "测试画廊", "全一册"));

            var keys = service.GetDownloadedKeys(root);

            Assert.Contains("hitomi:987", keys);
            Assert.Equal("hitomi:987", LocalLibraryService.KeyFor("hitomi", "987"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetDownloadedKeys_FallsBack_To_Jm_For_Legacy_AlbumJson()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "禁漫作品");
            Directory.CreateDirectory(Path.Combine(albumDir, "第1话"));
            WriteFile(Path.Combine(albumDir, LocalLibraryService.MetadataFileName), """{"id":42,"name":"禁漫作品"}""");

            var keys = CreateService().GetDownloadedKeys(root);

            Assert.Contains("jm:42", keys);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetDownloadedKeys_Skips_Dirs_Without_Chapters()
    {
        var root = CreateTempRoot();
        try
        {
            var albumDir = Path.Combine(root, "只有元数据");
            WriteFile(Path.Combine(albumDir, LocalLibraryService.SourceMetadataFileName),
                """{"source_id":"wnacg","comic_id":"1","title":"只有元数据"}""");

            var keys = CreateService().GetDownloadedKeys(root);

            Assert.Empty(keys);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
