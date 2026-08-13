using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Services;

/// <summary>下载辅助：拉取专辑详情并整本加入下载队列。</summary>
public static class DownloadHelper
{
    public static async Task<(int Count, string AlbumTitle)> EnqueueAllAsync(
        JmHttpClient client, ConfigService config, DownloadManager manager,
        long albumId, CancellationToken ct = default)
    {
        var resp = await client.GetAlbumAsync(albumId, ct);
        var album = AlbumBuilder.Build(resp, config.Current.DownloadDir);
        foreach (var chapter in album.ChapterInfos)
        {
            await manager.SubmitChapterAsync(chapter, ct);
        }
        // 保存专辑元数据（标签/作者等），供本地模式离线展示
        App.Services.GetRequiredService<LocalLibraryService>()
            .SaveMetadataForAlbum(config.Current.DownloadDir, album);
        return (album.ChapterInfos.Count, album.Name);
    }
}

