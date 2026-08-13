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
/// 绔犺妭璇︽儏椤碉細鏀寔榧犳爣妗嗛€?+ Ctrl 澶氶€?+ 鍙抽敭鑿滃崟鎵归噺涓嬭浇銆?
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
                throw new JmException("婕敾淇℃伅涓虹┖");
            }

            HeaderTitle.Text = _album.Name;
            HeaderMeta.Text = BuildMetaText(_album);
            HeaderDesc.Text = string.IsNullOrWhiteSpace(_album.Description) ? "鏆傛棤绠€浠? : _album.Description;
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
            HeaderTitle.Text = "鍔犺浇澶辫触";
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
            $"鍏?{album.ChapterInfos.Count} 绔?,
        };
        if (album.Author.Count > 0)
        {
            parts.Add($"浣滆€咃細{string.Join("銆?, album.Author)}");
        }
        if (!string.IsNullOrEmpty(album.TotalViews))
        {
            parts.Add($"瑙傜湅锛歿album.TotalViews}");
        }
        if (!string.IsNullOrEmpty(album.Likes))
        {
            parts.Add($"鐐硅禐锛歿album.Likes}");
        }
        return string.Join(" 路 ", parts);
    }

    // ====================== 妗嗛€?======================

    private void ChaptersLayer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }
        // 鐐逛腑绔犺妭鍗＄墖鎴栨粴鍔ㄦ潯鏃朵笉鍚姩妗嗛€?
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
        // 蹇界暐杩囧皬鐨?鐐瑰嚮"锛堢偣鍑荤┖鐧藉锛夛紝閬垮厤璇竻绌哄綋鍓嶉€夋嫨
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
        // SelectionCanvas 涓庣珷鑺傚垪琛ㄦ槸鍏勫紵鑺傜偣锛岀敤 TransformToVisual 璁＄畻鐩稿鍧愭爣锛?
        // 绔犺妭鍒楄〃閲嶅缓/鍥炴敹鏃跺鍣ㄥ彲鑳藉凡鑴辩瑙嗚鏍戯紙姝ゆ椂浼氭姏寮傚父锛夛紝鎹曡幏鍚庤烦杩囪鍗＄墖
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

    // ====================== 鍗＄墖鐐瑰嚮 / 閫夋嫨鎿嶄綔 ======================

    private void ChapterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: ChapterCardViewModel card })
        {
            return;
        }
        // 鍗曞嚮鍒囨崲閫変腑鐘舵€侊細閫愪釜鐐归€夊嵆鍙閫夛紝涓嶅啀娓呯┖鍏朵粬宸查€夌珷鑺?
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
        SelectedText.Text = count == 0 ? "鏈€夋嫨绔犺妭" : $"宸查€?{count} 绔?;
        DownloadSelectedButton.Content = count == 0 ? "涓嬭浇閫変腑" : $"涓嬭浇閫変腑锛坽count}锛?;
    }

    // ====================== 涓嬭浇 ======================

    private void ContextDownloadSelected_Click(object sender, RoutedEventArgs e) => DownloadSelected();

    private async void DownloadSelectedButton_Click(object sender, RoutedEventArgs e) => await DownloadSelectedAsync();

    private void DownloadSelected() => _ = DownloadSelectedAsync();

    private async Task DownloadSelectedAsync()
    {
        var selected = Chapters.Where(c => c.IsSelected && !c.IsDownloading && !c.IsDownloaded).ToList();
        if (selected.Count == 0)
        {
            ToastService.Show("璇峰厛妗嗛€夐渶瑕佷笅杞界殑绔犺妭锛堝凡涓嬭浇鐨勯櫎澶栵級", ToastKind.Info);
            return;
        }
        await EnqueueChaptersAsync(selected);
        ToastService.Show($"宸插皢 {selected.Count} 涓珷鑺傚姞鍏ヤ笅杞介槦鍒?, ToastKind.Success);
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        var pending = Chapters.Where(c => !c.IsDownloading && !c.IsDownloaded).ToList();
        if (pending.Count == 0)
        {
            ToastService.Show("娌℃湁闇€瑕佷笅杞界殑绔犺妭", ToastKind.Info);
            return;
        }
        await EnqueueChaptersAsync(pending);
        ToastService.Show($"宸插皢鍏ㄩ儴 {pending.Count} 涓珷鑺傚姞鍏ヤ笅杞介槦鍒?, ToastKind.Success);
    }

    private async Task EnqueueChaptersAsync(IEnumerable<ChapterCardViewModel> cards)
    {
        // 淇濆瓨涓撹緫鍏冩暟鎹紙鏍囩/浣滆€呯瓑锛夛紝渚涙湰鍦版ā寮忕绾垮睍绀?
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

    // ====================== 鏀惰棌 ======================

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null)
        {
            return;
        }
        if (!_session.IsLoggedIn)
        {
            ToastService.Show("璇峰厛鐧诲綍鍚庡啀鏀惰棌", ToastKind.Info);
            return;
        }
        try
        {
            var resp = await _client.ToggleFavoriteAlbumAsync(_album.Id);
            var nowFavorite = resp.ToggleType == ToggleType.Add;
            _album.IsFavorite = nowFavorite;
            UpdateFavoriteButton();
            ToastService.Show(nowFavorite ? "宸插姞鍏ユ敹钘? : "宸插彇娑堟敹钘?, ToastKind.Success);
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
        FavoriteText.Text = _album.IsFavorite ? "宸叉敹钘? : "鏀惰棌";
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

    // ====================== 瑙嗚鏍戝伐鍏?======================

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









