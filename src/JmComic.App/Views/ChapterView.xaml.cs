using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JmComic.Core;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;
using JmComic.Core.Sources;
using JmComic.Core.Sources.Jm;

namespace JmComic.App.Views;

/// <summary>
/// 章节详情页：支持鼠标框选 + Ctrl 多选 + 右键菜单批量下载。
/// 站点差异收敛到 IComicSource，页面只依赖通用 ComicDetail / Chapter 模型。
/// </summary>
public partial class ChapterView : UserControl
{
    private readonly IComicSource _source;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly SessionService _session;

    private ComicDetail? _detail;
    private bool _isFavorite;
    private readonly Dictionary<string, Chapter> _chapterMap = new();

    /// <summary>章节卡单元格尺寸（卡片 158x54 + 边距 10），框选命中按此换算。</summary>
    private const double ChapterCellWidth = 168;
    private const double ChapterCellHeight = 64;

    private Point _dragStart;
    private bool _isDragging;

    public ObservableCollection<ChapterCardViewModel> Chapters { get; } = new();

    public ChapterView(IComicSource source, string comicId)
    {
        InitializeComponent();
        _source = source;
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _session = App.Services.GetRequiredService<SessionService>();
        FavoriteButton.Visibility = source.Info.SupportsFavorites ? Visibility.Visible : Visibility.Collapsed;
        ImageLoader.SetHeaders(CoverImage, source.Info.CoverHeaders);
        ChapterItems.ItemsSource = Chapters;
        _ = LoadAsync(comicId);
    }

    private async Task LoadAsync(string comicId)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var detail = await _source.GetComicAsync(comicId);
            _detail = detail;

            HeaderTitle.Text = detail.Title;
            HeaderMeta.Text = BuildMetaText(detail);
            HeaderDesc.Text = string.IsNullOrWhiteSpace(detail.Description) ? "暂无简介" : detail.Description;
            ImageLoader.SetSource(CoverImage, detail.CoverUrl);
            UpdateFavoriteButton();

            Chapters.Clear();
            _chapterMap.Clear();
            var chapterIndex = 0;
            foreach (var chapter in detail.Chapters)
            {
                var index = chapterIndex++;
                var card = new ChapterCardViewModel
                {
                    ChapterId = chapter.Id,
                    AlbumId = chapter.ComicId,
                    Title = chapter.Title,
                    ReadCommand = new RelayCommand(_ => Navigation.OpenOnlineReader(_source, detail.Chapters, index)),
                };
                Chapters.Add(card);
                _chapterMap[chapter.Id] = chapter;
            }
            UpdateSelectedText();
            ReadAllButton.IsEnabled = detail.Chapters.Count > 0;
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex);
            HeaderTitle.Text = "加载失败";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ReadAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null || _detail.Chapters.Count == 0)
        {
            return;
        }
        Navigation.OpenOnlineReader(_source, _detail.Chapters, 0);
    }

    private static string BuildMetaText(ComicDetail detail)
    {
        var parts = new List<string>
        {
            $"共 {detail.Chapters.Count} 章",
        };
        if (detail.Authors.Count > 0)
        {
            parts.Add($"作者：{string.Join("、", detail.Authors)}");
        }
        if (detail.Tags.Count > 0)
        {
            parts.Add($"标签：{string.Join("、", detail.Tags.Take(6))}");
        }
        return string.Join(" · ", parts);
    }

    // ====================== 框选 ======================

    private void ChaptersLayer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }
        // 点中章节卡片或滚动条时不启动框选
        if (FindVisualParent<Border>(source, b => b.DataContext is ChapterCardViewModel) is not null ||
            FindVisualParent<ScrollBar>(source) is not null)
        {
            return;
        }

        _dragStart = e.GetPosition(SelectionCanvas);
        _isDragging = true;
        Canvas.SetLeft(SelectionRect, _dragStart.X);
        Canvas.SetTop(SelectionRect, _dragStart.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        ChaptersLayer.CaptureMouse();
        e.Handled = true;
    }

    private void ChaptersLayer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }
        var pos = e.GetPosition(SelectionCanvas);
        var x = Math.Min(_dragStart.X, pos.X);
        var y = Math.Min(_dragStart.Y, pos.Y);
        var w = Math.Abs(pos.X - _dragStart.X);
        var h = Math.Abs(pos.Y - _dragStart.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
        // 忽略过小的"点击"（点击空白处），避免误清空当前选择
        if (w < 4 && h < 4)
        {
            return;
        }
        ApplySelectionToCards(new Rect(x, y, w, h));
    }

    private void ChaptersLayer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }
        _isDragging = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        ChaptersLayer.ReleaseMouseCapture();
        UpdateSelectedText();
        e.Handled = true;
    }

    private void ApplySelectionToCards(Rect selection)
    {
        // 章节卡为固定 158x54 + 边距 10 → 单元格 168x64；行由虚拟化网格按列数切分。
        // 直接用「行列 → 下标」换算命中范围（O(命中区域)），不再遍历全部章节或依赖
        // 已实例化容器（虚拟化后离屏章节本就没有容器），避免拖拽时逐帧 TransformToVisual。
        var columns = Math.Max(1, ChapterItems.Columns);
        if (columns <= 0 || Chapters.Count == 0)
        {
            return;
        }
        var origin = ChapterItems.TranslatePoint(new Point(0, 0), SelectionCanvas);
        origin.Y -= ChapterItems.VerticalOffset;

        var left = selection.Left - origin.X;
        var top = selection.Top - origin.Y;
        var right = selection.Right - origin.X;
        var bottom = selection.Bottom - origin.Y;

        var firstCol = Math.Max(0, (int)Math.Floor(left / ChapterCellWidth));
        var lastCol = Math.Min(columns - 1, (int)Math.Floor(right / ChapterCellWidth));
        var firstRow = Math.Max(0, (int)Math.Floor(top / ChapterCellHeight));
        var lastRow = Math.Min((Chapters.Count - 1) / columns, (int)Math.Floor(bottom / ChapterCellHeight));
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var col = firstCol; col <= lastCol; col++)
            {
                var index = row * columns + col;
                if (index >= 0 && index < Chapters.Count)
                {
                    Chapters[index].IsSelected = true;
                }
            }
        }
    }

    // ====================== 卡片点击 / 选择操作 ======================

    private void ChapterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ChapterCardViewModel card })
        {
            return;
        }
        // 点击「在线阅读」按钮时不触发选择
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source, _ => true) is not null)
        {
            return;
        }
        // 单击切换选中状态：逐个点选即可多选，不再清空其他已选章节
        card.IsSelected = !card.IsSelected;
        UpdateSelectedText();
        e.Handled = true;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var card in Chapters)
        {
            card.IsSelected = true;
        }
        UpdateSelectedText();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var card in Chapters)
        {
            card.IsSelected = false;
        }
        UpdateSelectedText();
    }

    private void UpdateSelectedText()
    {
        var count = Chapters.Count(c => c.IsSelected);
        SelectedText.Text = count == 0 ? "未选择章节" : $"已选 {count} 章";
        DownloadSelectedButton.Content = count == 0 ? "下载选中" : $"下载选中（{count}）";
    }

    // ====================== 下载 ======================

    private void ContextDownloadSelected_Click(object sender, RoutedEventArgs e) => DownloadSelected();

    private async void DownloadSelectedButton_Click(object sender, RoutedEventArgs e) => await DownloadSelectedAsync();

    private void DownloadSelected() => _ = DownloadSelectedAsync();

    private async Task DownloadSelectedAsync()
    {
        var selected = Chapters.Where(c => c.IsSelected && !c.IsDownloading && !c.IsDownloaded).ToList();
        if (selected.Count == 0)
        {
            ToastService.Show("请先框选需要下载的章节（已下载的除外）", ToastKind.Info);
            return;
        }
        await EnqueueChaptersAsync(selected);
        ToastService.Show($"已将 {selected.Count} 个章节加入下载队列", ToastKind.Success);
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        var pending = Chapters.Where(c => !c.IsDownloading && !c.IsDownloaded).ToList();
        if (pending.Count == 0)
        {
            ToastService.Show("没有需要下载的章节", ToastKind.Info);
            return;
        }
        await EnqueueChaptersAsync(pending);
        ToastService.Show($"已将全部 {pending.Count} 个章节加入下载队列", ToastKind.Success);
    }

    private async Task EnqueueChaptersAsync(IEnumerable<ChapterCardViewModel> cards)
    {
        // 禁漫写专辑元数据（album.json）；其余源写通用来源元数据（source.json）
        if (_detail is not null)
        {
            try
            {
                if (_source is JmSource jmSource)
                {
                    var resp = await jmSource.GetAlbumRawAsync(_detail.Id);
                    var album = AlbumBuilder.Build(resp, _config.Current.DownloadDir);
                    if (album is not null)
                    {
                        App.Services.GetRequiredService<LocalLibraryService>()
                            .SaveMetadataForAlbum(_config.Current.DownloadDir, album);
                    }
                }
                else
                {
                    App.Services.GetRequiredService<LocalLibraryService>()
                        .SaveSourceMetadata(_config.Current.DownloadDir, _source.Info.Id, _detail);
                }
            }
            catch
            {
                // 元数据失败不影响下载
            }
        }

        foreach (var card in cards)
        {
            if (!_chapterMap.TryGetValue(card.ChapterId, out var chapter))
            {
                continue;
            }
            card.IsDownloading = true;
            await _downloadManager.SubmitChapterAsync(chapter);
        }
    }

    // ====================== 收藏（仅禁漫） ======================

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null)
        {
            return;
        }
        if (_source is not JmSource)
        {
            ToastService.Show("该来源暂不支持收藏", ToastKind.Info);
            return;
        }
        if (!_session.IsLoggedIn)
        {
            ToastService.Show("请先登录后再收藏", ToastKind.Info);
            return;
        }
        try
        {
            var client = App.Services.GetRequiredService<JmHttpClient>();
            var resp = await client.ToggleFavoriteAlbumAsync(long.Parse(_detail.Id));
            _isFavorite = resp.ToggleType == ToggleType.Add;
            UpdateFavoriteButton();
            ToastService.Show(_isFavorite ? "已加入收藏" : "已取消收藏", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex);
        }
    }

    private void UpdateFavoriteButton()
    {
        if (_detail is null)
        {
            return;
        }
        FavoriteText.Text = _isFavorite ? "已收藏" : "收藏";
        if (_isFavorite)
        {
            FavoriteButton.Style = (Style)FindResource("PrimaryButtonStyle");
            FavoriteIcon.Stroke = Brushes.White;
            FavoriteText.Foreground = Brushes.White;
        }
        else
        {
            FavoriteButton.Style = (Style)FindResource("GhostButtonStyle");
            FavoriteIcon.Stroke = (Brush)FindResource("TextSecondaryBrush");
            FavoriteText.Foreground = (Brush)FindResource("TextPrimaryBrush");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Navigation.Back();

    // ====================== 视觉树工具 ======================

    private static T? FindVisualParent<T>(DependencyObject? child, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match && (predicate is null || predicate(match)))
            {
                return match;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}


