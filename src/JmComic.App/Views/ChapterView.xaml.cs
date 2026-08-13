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
    private readonly Dictionary<string, ChapterCardViewModel> _cardMap = new();

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
            _cardMap.Clear();
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
                _cardMap[chapter.Id] = card;
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
        // 框选只追加选中，不清除已选章节；清空请使用页面上的"清空"按钮
        foreach (var card in Chapters)
        {
            if (TryGetCardRect(card, out var cardRect) && selection.IntersectsWith(cardRect))
            {
                card.IsSelected = true;
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
        // SelectionCanvas 与章节列表是兄弟节点，用 TransformToVisual 计算相对坐标；
        // 章节列表重建/回收时容器可能已脱离视觉树（此时会抛异常），捕获后跳过该卡片
        try
        {
            var transform = cardElement.TransformToVisual(SelectionCanvas);
            rect = transform.TransformBounds(new Rect(0, 0, cardElement.ActualWidth, cardElement.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
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


