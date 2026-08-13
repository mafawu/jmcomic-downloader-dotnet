using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Controls;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;

namespace JmComic.App.Views;

/// <summary>搜索页：关键词 + 排序 + 结果网格 + 分页。</summary>
public partial class SearchView : UserControl
{
    private const int PageSize = 20;

    private readonly JmHttpClient _client;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;

    private SearchSort _sort = SearchSort.Latest;
    private long _total;
    private long _page = 1;
    private string _keyword = "";

    public ObservableCollection<AlbumCardViewModel> Results { get; } = new();

    /// <summary>内容区当前状态，驱动 <see cref="StateHostStyle"/> 切换模板。</summary>
    public State CurrentState
    {
        get => (State)GetValue(CurrentStateProperty);
        set => SetValue(CurrentStateProperty, value);
    }

    public static readonly DependencyProperty CurrentStateProperty = DependencyProperty.Register(
        nameof(CurrentState),
        typeof(State),
        typeof(SearchView),
        new PropertyMetadata(State.Hint));

    public SearchView()
    {
        InitializeComponent();
        _client = App.Services.GetRequiredService<JmHttpClient>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        SortBox.SelectedIndex = 0;
    }

    private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SearchAsync(1);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(1);
    private void ClearKeywordButton_Click(object sender, RoutedEventArgs e)
    {
        KeywordBox.Clear();
        KeywordBox.Focus();
    }

    private void EmptyRetryButton_Click(object sender, RoutedEventArgs e)
    {
        _ = SearchAsync(1);
    }


    private void PrevButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page + 1);

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _sort = Enum.TryParse<SearchSort>(tag, out var sort) ? sort : SearchSort.Latest;
        }
    }

    private async Task SearchAsync(long page)
    {
        var keyword = KeywordBox.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ToastService.Show("请输入搜索关键词", ToastKind.Info);
            return;
        }
        if (page < 1)
        {
            return;
        }

        _keyword = keyword;
        _page = page;
        SetBusy(true);
        try
        {
            var resp = await _client.SearchAsync(keyword, page, _sort);
            if (resp.IsAlbum && resp.AlbumRespData is { } album)
            {
                // 搜索命中唯一漫画：直接打开详情
                Results.Clear();
                ShowState(State.Result);
                Navigation.OpenAlbum(album.Id);
                return;
            }

            Results.Clear();
            var data = resp.SearchRespData;
            if (data is null || data.Content.Count == 0)
            {
                ShowState(State.Empty);
                return;
            }

            _total = data.Total;
            foreach (var item in data.Content)
            {
                Results.Add(ToCard(item));
            }
            PageText.Text = $"第 {page} 页";
            PrevButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page * PageSize < _total;
            ShowState(State.Result);
        }
        catch (Exception ex)
        {
            ShowState(State.Empty);
            ToastService.ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private AlbumCardViewModel ToCard(AlbumInSearchRespData item)
    {
        var id = long.TryParse(item.Id, out var parsed) ? parsed : 0;
        return new AlbumCardViewModel
        {
            Id = id,
            Name = item.Name,
            AuthorText = string.IsNullOrEmpty(item.Author) ? "未知作者" : item.Author,
            CoverUrl = NormalizeCover(id, item.Image),
            IsFavorite = item.IsFavorite,
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

    internal static string NormalizeCover(long albumId, string image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return $"https://{JmConstants.ImageDomain}/media/albums/{albumId}_3x4.jpg";
        }
        if (image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return image;
        }
        return $"https://{JmConstants.ImageDomain}{image.TrimStart('/')}";
    }

    /// <summary>搜索页内容区状态。</summary>
    public enum State
    {
        Hint,
        Loading,
        Empty,
        Result,
    }

    private void SetBusy(bool busy)
    {
        if (busy)
        {
            CurrentState = State.Loading;
            PagingPanel.Visibility = Visibility.Collapsed;
        }
        else if (CurrentState == State.Loading)
        {
            CurrentState = State.Hint;
        }
    }

    private void ShowState(State state)
    {
        CurrentState = state;
        PagingPanel.Visibility = state == State.Result ? Visibility.Visible : Visibility.Collapsed;
    }
}

