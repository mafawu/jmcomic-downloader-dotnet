using System.Windows;
using System.Windows.Controls;
using JmComic.App.Common;
using JmComic.App.Services;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Views;

/// <summary>
/// 本地漫画详情面板：展示本地漫画信息，支持手动「检查更新」与「更新下载」。
/// 更新检查为手动触发（不做后台自动检查），发现新章节后「更新下载」按钮才可用。
/// </summary>
public partial class LocalComicDetailPanel : UserControl
{
    private readonly DownloadManager _downloadManager;
    private readonly AlbumUpdateService _updateService;

    private LocalComic? _comic;
    private AlbumUpdateResult? _lastResult;
    private CancellationTokenSource? _checkCts;
    private bool _checking;

    public LocalComicDetailPanel()
    {
        InitializeComponent();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _updateService = App.Services.GetRequiredService<AlbumUpdateService>();
    }

    public void Show(LocalComic comic)
    {
        _comic = comic;
        _lastResult = null;
        _checkCts?.Cancel();
        _checkCts = null;
        _checking = false;

        TitleText.Text = string.IsNullOrEmpty(comic.NameCn) ? comic.Name : comic.NameCn;
        TitleCnText.Text = comic.Name;
        TitleCnText.Visibility = !string.IsNullOrEmpty(comic.NameCn) && comic.NameCn != comic.Name
            ? Visibility.Visible
            : Visibility.Collapsed;

        MetaText.Text = $"{comic.ChapterCount} 章";
        if (comic.ImageCount > 0)
        {
            MetaText.Text += $" · {comic.ImageCount} 图";
        }
        MetaText.Text += $" · 更新 {comic.ModifiedAt:yyyy-MM-dd}";

        PathText.Text = comic.Path;
        ImageLoader.SetSource(CoverImage, comic.CoverPath);

        DownloadUpdateButton.IsEnabled = false;
        if (comic.AlbumId is > 0)
        {
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = "点击「检查更新」查看是否有新章节";
        }
        else
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "该漫画缺少专辑 ID，无法检查更新";
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var comic = _comic;
        if (comic is null || comic.AlbumId is not > 0 || _checking)
        {
            return;
        }

        _checking = true;
        _lastResult = null;
        CheckUpdateButton.IsEnabled = false;
        DownloadUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        _checkCts?.Cancel();
        _checkCts = new CancellationTokenSource();

        try
        {
            var result = await _updateService.CheckAsync(comic.AlbumId.Value, comic.Path, _checkCts.Token);
            _lastResult = result;
            if (result.HasUpdates)
            {
                UpdateStatusText.Text =
                    $"发现 {result.NewChapters.Count} 个新章节（本地 {result.LocalChapterCount} 章 → 线上 {result.RemoteChapterCount} 章），可点击「更新下载」";
                DownloadUpdateButton.IsEnabled = true;
            }
            else
            {
                UpdateStatusText.Text = $"已是最新（本地 {result.LocalChapterCount} 章 / 线上 {result.RemoteChapterCount} 章）";
            }
        }
        catch (OperationCanceledException)
        {
            // 切换漫画时取消旧检查，不提示
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "检查更新失败，可稍后重试";
            ToastService.ShowError(ex);
        }
        finally
        {
            _checking = false;
            if (comic.AlbumId is > 0)
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _lastResult;
        if (result is null || result.NewChapters.Count == 0)
        {
            return;
        }

        DownloadUpdateButton.IsEnabled = false;
        try
        {
            foreach (var chapter in result.NewChapters)
            {
                await _downloadManager.SubmitChapterAsync(chapter);
            }
            UpdateStatusText.Text =
                $"已将 {result.NewChapters.Count} 个新章节加入下载队列，下载完成后点击本地页「刷新」查看";
            ToastService.Show($"已将 {result.NewChapters.Count} 个章节加入下载队列", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => Navigation.CloseLocalDetail();
}
