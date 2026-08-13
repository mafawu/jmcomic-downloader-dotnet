using JmComic.Core.Models;
using JmComic.Core.Services;

namespace JmComic.Core.Tests;

/// <summary>
/// AlbumUpdateService 更新对比测试：新增章节判定（按章节标题匹配本地章节目录）、
/// 大小写容错、临时目录过滤。
/// </summary>
public class AlbumUpdateServiceTests
{
    private static Album MakeAlbum(params string[] chapterTitles)
    {
        var album = new Album();
        foreach (var title in chapterTitles)
        {
            album.ChapterInfos.Add(new ChapterInfo { ChapterTitle = title });
        }
        return album;
    }

    [Fact]
    public void NoNewChapters_ReturnsEmpty()
    {
        var album = MakeAlbum("第1话", "第2话", "第3话");
        var local = new[] { "第1话", "第2话", "第3话" };
        var result = AlbumUpdateService.ComputeNewChapters(album, local);
        Assert.Empty(result);
    }

    [Fact]
    public void NewChapters_ReturnsOnlyMissingOnes()
    {
        var album = MakeAlbum("第1话", "第2话", "第3话", "第4话");
        var local = new[] { "第1话", "第3话" };
        var result = AlbumUpdateService.ComputeNewChapters(album, local);
        Assert.Equal(new[] { "第2话", "第4话" }, result.Select(c => c.ChapterTitle));
    }

    [Fact]
    public void MissingAlbumId_FallsBackToRemoteCount()
    {
        var album = MakeAlbum("第1话");
        var result = AlbumUpdateService.ComputeNewChapters(album, Array.Empty<string>());
        Assert.Single(result);
    }

    [Fact]
    public void ChapterMatch_IsCaseInsensitive()
    {
        var album = MakeAlbum("第1话 Special");
        var local = new[] { "第1话 special" };
        var result = AlbumUpdateService.ComputeNewChapters(album, local);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyOrNullChapterTitles_AreIgnored()
    {
        var album = MakeAlbum("", "第1话", null!);
        var result = AlbumUpdateService.ComputeNewChapters(album, Array.Empty<string>());
        Assert.Single(result);
        Assert.Equal("第1话", result[0].ChapterTitle);
    }

    [Fact]
    public void LocalChapterNames_TrimAndSkipBlanks()
    {
        var album = MakeAlbum("第1话");
        var result = AlbumUpdateService.ComputeNewChapters(album, new[] { "  ", "" });
        Assert.Single(result);
    }

    [Fact]
    public void ListLocalChapterDirs_SkipsTempDownloadDirs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jm-update-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "第1话"));
            Directory.CreateDirectory(Path.Combine(root, "第2话"));
            Directory.CreateDirectory(Path.Combine(root, ".下载中-第3话"));

            var dirs = AlbumUpdateService.ListLocalChapterDirs(root);
            Assert.Equal(new[] { "第1话", "第2话" }, dirs.OrderBy(d => d));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ListLocalChapterDirs_MissingDir_ReturnsEmpty()
    {
        var dirs = AlbumUpdateService.ListLocalChapterDirs(Path.Combine(Path.GetTempPath(), "definitely-not-exists-xyz"));
        Assert.Empty(dirs);
    }
}
