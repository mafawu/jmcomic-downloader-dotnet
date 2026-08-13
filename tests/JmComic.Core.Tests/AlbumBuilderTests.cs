using JmComic.Core.Models;
using Xunit;

namespace JmComic.Core.Tests;

public class AlbumBuilderTests
{
    private static AlbumRespData CreateAlbumRespData()
    {
        return new AlbumRespData
        {
            Id = 1001,
            Name = "测试/漫画",
            Series =
            [
                new SeriesRespData { Id = "2001", Name = "", Sort = "1" },
                new SeriesRespData { Id = "2002", Name = "特别篇", Sort = "2" },
            ],
        };
    }

    [Fact]
    public void Build_Creates_ChapterInfos_WithExpectedTitles()
    {
        var album = AlbumBuilder.Build(CreateAlbumRespData(), "C:\\Downloads");

        Assert.Equal(2, album.ChapterInfos.Count);
        Assert.Equal("第1话", album.ChapterInfos[0].ChapterTitle);
        Assert.Equal("第2话 特别篇", album.ChapterInfos[1].ChapterTitle);
        // '/' 会被替换为空格（与原 Rust 实现一致）
        Assert.Equal("测试 漫画", album.ChapterInfos[0].AlbumTitle);
    }

    [Fact]
    public void Build_Marks_Downloaded_WhenDirExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jm-album-builder-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var albumTitle = "测试 漫画";
            Directory.CreateDirectory(Path.Combine(tempDir, albumTitle, "第1话"));

            var album = AlbumBuilder.Build(CreateAlbumRespData(), tempDir);

            Assert.True(album.ChapterInfos[0].IsDownloaded);
            Assert.False(album.ChapterInfos[1].IsDownloaded);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Build_Adds_DefaultChapter_WhenSeriesEmpty()
    {
        var resp = CreateAlbumRespData();
        resp.Series = [];

        var album = AlbumBuilder.Build(resp, "C:\\Downloads");

        Assert.Single(album.ChapterInfos);
        Assert.Equal("第1话", album.ChapterInfos[0].ChapterTitle);
        Assert.Equal(resp.Id, album.ChapterInfos[0].ChapterId);
    }
}
