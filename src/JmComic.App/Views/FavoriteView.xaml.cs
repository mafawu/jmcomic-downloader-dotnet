using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Dialogs;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;
using JmComic.Core.Sources.Jm;

namespace JmComic.App.Views;

/// <summary>收藏页：未登录时提示登录，登录后展示收藏漫画列表。</summary>
public partial class FavoriteView : UserControl
{
    private const int PageSize = 20;

    private readonly SessionService _session;
    private readonly JmHttpClient _client;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly LocalLibraryService _localLibrary;

    /// <summary>已下载漫画的 (源,id) 键集合（卡片右上角徽章）。</summary>
    private HashSet<string> _downloadedKeys = new();
    private long _page = 1;
    private long _total;
    private bool _hasLoaded;

    public ObservableCollection<AlbumCardViewModel> Items { get; } = new();

    public FavoriteView()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<SessionService>();
        _client = App.Services.GetRequiredService<JmHttpClient>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _localLibrary = App.Services.GetRequiredService<LocalLibraryService>();
        FavoriteItems.ItemsSource = Items;
    }

    /// <summary>导航到本页时调用：刷新登录态；已加载过的收藏保持原状态。</summary>
    public async void OnShown()
    {
        UpdateLoginState();
        if (_session.IsLoggedIn && !_hasLoaded)
        {
            await LoadAsync(1);
        }
    }

    /// <summary>登录状态变化后强制刷新收藏数据（登录/退出时调用）。</summary>
    public async void Refresh()
    {
        UpdateLoginState();
        if (_session.IsLoggedIn)
        {
            await LoadAsync(_page);
        }
    }

    private void UpdateLoginState()
    {
        var loggedIn = _session.IsLoggedIn;
        LoginPrompt.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        FavoriteContent.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        FavoritePaging.Visibility = Visibility.Collapsed;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ToastService.Show("登录成功", ToastKind.Success);
            UpdateLoginState();
            await LoadAsync(1);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(_page);

    private async void FavPrevButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(_page - 1);

    private async void FavNextButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(_page + 1);

    private async Task LoadAsync(long page)
    {
        if (page < 1)
        {
            return;
        }
        _page = page;
        _downloadedKeys = _localLibrary.GetDownloadedKeys(_config.Current.DownloadDir);
        FavoriteLoading.Visibility = Visibility.Visible;
        FavoriteEmpty.Visibility = Visibility.Collapsed;
        FavoriteScroller.Visibility = Visibility.Collapsed;
        FavoritePaging.Visibility = Visibility.Collapsed;

        try
        {
            // folder_id=0 表示默认收藏夹
            var data = await _client.GetFavoriteFolderAsync(0, page, FavoriteSort.FavoriteTime);
            Items.Clear();
            foreach (var item in data.List)
            {
                Items.Add(ToCard(item));
            }

            FavoriteCount.Text = data.Count > 0 ? $"共 {data.Count} 部" : "";
            FavoriteTitle.Text = "我的收藏";
            _total = data.Count;
            FavPageText.Text = $"第 {page} 页";
            FavPrevButton.IsEnabled = page > 1;
            FavNextButton.IsEnabled = page * PageSize < _total;

            if (Items.Count == 0)
            {
                FavoriteEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                FavoriteScroller.Visibility = Visibility.Visible;
                FavoritePaging.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            FavoriteEmpty.Visibility = Visibility.Visible;
            ToastService.ShowError(ex);
        }
        finally
        {
            _hasLoaded = true;
            FavoriteLoading.Visibility = Visibility.Collapsed;
        }
    }

    private AlbumCardViewModel ToCard(AlbumInFavoriteRespData item)
    {
        var id = long.TryParse(item.Id, out var parsed) ? parsed : 0;
        return new AlbumCardViewModel
        {
            Id = item.Id,
            Name = item.Name,
            AuthorText = string.IsNullOrEmpty(item.Author) ? "未知作者" : item.Author,
            CoverUrl = JmSource.NormalizeCover(id, item.Image),
            IsFavorite = true,
            IsDownloaded = _downloadedKeys.Contains(LocalLibraryService.KeyFor("jm", item.Id)),
            OpenCommand = new RelayCommand(_ => Navigation.OpenAlbum(id)),
            DownloadCommand = new AsyncRelayCommand(async _ =>
            {
                try
                {
                    var (count, title) = await DownloadHelper.EnqueueAllAsync(
                        _client, _config, _downloadManager, id);
                    ToastService.Show($"已将「{title}」全部 {count} 个章节加入下载队列", ToastKind.Success);
                }
                catch (Exception ex)
                {
                    ToastService.ShowError(ex);
                }
            }),
        };
    }
}




