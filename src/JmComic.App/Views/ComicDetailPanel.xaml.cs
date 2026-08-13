using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JmComic.App.Common;
using JmComic.Core.Models;

namespace JmComic.App.Views;

/// <summary>右侧漫画详情面板：进入阅读后展示当前漫画的详细信息（封面/标题/标签/作者/简介/路径）。</summary>
public partial class ComicDetailPanel : UserControl
{
    private Guid _metadataToken;

    public ComicDetailPanel()
    {
        InitializeComponent();
    }

    public void Show(LocalComic comic)
    {
        TitleText.Text = string.IsNullOrEmpty(comic.NameCn) ? comic.Name : comic.NameCn;
        TitleCnText.Text = comic.Name;
        TitleCnText.Visibility = !string.IsNullOrEmpty(comic.NameCn) && comic.NameCn != comic.Name
            ? Visibility.Visible
            : Visibility.Collapsed;

        TagItems.ItemsSource = comic.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();

        var author = string.Join("、", comic.Author.Where(a => !string.IsNullOrWhiteSpace(a)));
        AuthorText.Text = $"作者：{author}";
        AuthorText.Visibility = author.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var stats = $"{comic.ChapterCount} 章";
        if (comic.ImageCount > 0)
        {
            stats += $" · {comic.ImageCount} 图";
        }
        stats += $" · 更新 {comic.ModifiedAt:yyyy-MM-dd}";
        MetaText.Text = stats;
        MetaText.Visibility = Visibility.Visible;

        PathText.Text = comic.Path;
        ImageLoader.SetSource(CoverImage, comic.CoverPath);

        // 异步补全元数据（作品/出演/简介）；令牌防止旧漫画的加载结果覆盖新漫画
        var token = Guid.NewGuid();
        _metadataToken = token;
        WorksText.Visibility = Visibility.Collapsed;
        ActorsText.Visibility = Visibility.Collapsed;
        DescLabel.Visibility = Visibility.Collapsed;
        DescText.Visibility = Visibility.Collapsed;
        _ = LoadMetadataAsync(comic.Path, token);
    }

    private async Task LoadMetadataAsync(string albumDir, Guid token)
    {
        var metadata = await Task.Run(() => TryReadMetadata(albumDir));
        if (metadata is null || !Equals(_metadataToken, token))
        {
            return;
        }

        var works = string.Join("、", metadata.Works.Where(w => !string.IsNullOrWhiteSpace(w)));
        if (works.Length > 0)
        {
            WorksText.Text = $"作品：{works}";
            WorksText.Visibility = Visibility.Visible;
        }

        var actors = string.Join("、", metadata.Actors.Where(a => !string.IsNullOrWhiteSpace(a)));
        if (actors.Length > 0)
        {
            ActorsText.Text = $"出演：{actors}";
            ActorsText.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            DescText.Text = metadata.Description;
            DescLabel.Visibility = Visibility.Visible;
            DescText.Visibility = Visibility.Visible;
        }
    }

    private static AlbumMetadata? TryReadMetadata(string albumDir)
    {
        foreach (var name in new[] { "album.json", "元数据.json" })
        {
            var path = Path.Combine(albumDir, name);
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                return JsonSerializer.Deserialize<AlbumMetadata>(File.ReadAllText(path));
            }
            catch
            {
                // 单个元数据损坏时继续尝试下一个
            }
        }
        return null;
    }
}