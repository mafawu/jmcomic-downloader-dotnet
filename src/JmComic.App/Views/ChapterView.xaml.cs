using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;

namespace JmComic.App.Views;

/// <summary>
/// 章节详情页：支持鼠标框选 + Ctrl 多选 + 右键菜单批量下载。
/// </summary>
public partial class ChapterView : UserControl
{
    private readonly JmHttpClient _client;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly SessionService _session;

    private Album? _album;
    private readonly Dictionary<long, ChapterInfo> _chapterMap = new();
    private readonly Dictionary<long, ChapterCardViewModel> _cardMap = new();

    private Point _dragStart;
    private bool _isDragging;
    private bool _additive;

    public ObservableCollection<ChapterCardViewModel> Chapters { get; } = new();

    public ChapterView(long albumId)
    {
        InitializeComponent();
        _client = App.Services.GetRequiredService<JmHttpClient>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _session = App.Services.GetRequiredService<SessionService>();
        ChapterItems.ItemsSource = Chapters;
        _ = LoadAsync(albumId);
    }

    private async Task LoadAsync(long albumId)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var resp = await _client.GetAlbumAsync(albumId);
            _album = AlbumBuilder.Build(resp, _config.Current.DownloadDir);
            if (_album is null)
            {
                throw new JmException("漫画信息为空");
            }

            HeaderTitle.Text = _album.Name;
            HeaderMeta.Text = BuildMetaText(_album);
            HeaderDesc.Text = string.IsNullOrWhiteSpace(_album.Description) ? "暂无简介" : _album.Description;
            ImageLoader.SetSource(CoverImage, $"https://{JmConstants.ImageDomain}/media/albums/{_album.Id}_3x4.jpg");
            UpdateFavoriteButton();

            Chapters.Clear();
            _chapterMap.Clear();
            _cardMap.Clear();
            foreach (var chapter in _album.ChapterInfos)
            {
                var card = new ChapterCardViewModel
                {
                    ChapterId = chapter.ChapterId,
                    AlbumId = chapter.AlbumId,
                    Title = chapter.ChapterTitle,
                    IsDownloaded = chapter.IsDownloaded,
                };
                Chapters.Add(card);
                _chapterMap[chapter.ChapterId] = chapter;
                _cardMap[chapter.ChapterId] = card;
            }
            UpdateSelectedText();
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

    private static string BuildMetaText(Album album)
    {
        var parts = new List<string>
        {
            $"共 {album.ChapterInfos.Count} 章",
        };
        if (album.Author.Count > 0)
        {
            parts.Add($"作者：{string.Join("、", album.Author)}");
        }
        if (!string.IsNullOrEmpty(album.TotalViews))
        {
            parts.Add($"观看：{album.TotalViews}");
        }
        if (!string.IsNullOrEmpty(album.Likes))
        {
            parts.Add($"点赞：{album.Likes}");
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
        _additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
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
        foreach (var card in Chapters)
        {
            if (TryGetCardRect(card, out var cardRect))
            {
                var hit = selection.IntersectsWith(cardRect);
                if (hit)
                {
                    card.IsSelected = true;
                }
                else if (!_additive)
                {
                    card.IsSelected = false;
                }
            }
            else if (!_additive)
            {
                card.IsSelected = false;
            }
        }
    }

    private bool TryGetCardRect(ChapterCardViewModel card, out Rect rect)
    {
        rect = default;
        var container = ChapterItems.ItemContainerGenerator.ContainerFromItem(card) as FrameworkElement;
        if (container is null)
        {
            return false;
        }
        var cardElement = FindVisualChild<Border>(container, b => b.DataContext == card);
        if (cardElement is null)
        {
            return false;
        }
        var transform = cardElement.TransformToAncestor(SelectionCanvas);
        rect = transform.TransformBounds(new Rect(0, 0, cardElement.ActualWidth, cardElement.ActualHeight));
        return true;
    }

    // ====================== 卡片点击 / 选择操作 ======================

    private void ChapterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ChapterCardViewModel card })
        {
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            card.IsSelected = !card.IsSelected;
        }
        else if (card.IsSelected)
        {
            card.IsSelected = false;
        }
        else
        {
            foreach (var other in Chapters)
            {
                other.IsSelected = false;
            }
            card.IsSelected = true;
        }
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
        // 保存专辑元数据（标签/作者等），供本地模式离线展示
        if (_album is not null)
        {
            App.Services.GetRequiredService<LocalLibraryService>()
                .SaveMetadataForAlbum(_config.Current.DownloadDir, _album);
        }

        foreach (var card in cards)
        {
            if (!_chapterMap.TryGetValue(card.ChapterId, out var chapterInfo))
            {
                continue;
            }
            card.IsDownloading = true;
            await _downloadManager.SubmitChapterAsync(chapterInfo);
        }
    }

    // ====================== 收藏 ======================

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null)
        {
            return;
        }
        if (!_session.IsLoggedIn)
        {
            ToastService.Show("请先登录后再收藏", ToastKind.Info);
            return;
        }
        try
        {
            var resp = await _client.ToggleFavoriteAlbumAsync(_album.Id);
            var nowFavorite = resp.ToggleType == ToggleType.Add;
            _album.IsFavorite = nowFavorite;
            UpdateFavoriteButton();
            ToastService.Show(nowFavorite ? "已加入收藏" : "已取消收藏", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex);
        }
    }

    private void UpdateFavoriteButton()
    {
        if (_album is null)
        {
            return;
        }
        FavoriteText.Text = _album.IsFavorite ? "已收藏" : "收藏";
        if (_album.IsFavorite)
        {
            FavoriteButton.Style = (Style)FindResource("PrimaryButtonStyle");
            FavoriteIcon.Foreground = Brushes.White;
            FavoriteText.Foreground = Brushes.White;
        }
        else
        {
            FavoriteButton.Style = (Style)FindResource("GhostButtonStyle");
            FavoriteIcon.Foreground = (Brush)FindResource("TextSecondaryBrush");
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

    private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (predicate is null || predicate(match)))
            {
                return match;
            }
            if (FindVisualChild(child, predicate) is T deeper)
            {
                return deeper;
            }
        }
        return null;
    }
}





