using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using JmComic.App.Common;
using JmComic.App.Controls;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core;
using JmComic.Core.Downloading;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;
using JmComic.Core.Sources.Jm;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Views;

/// <summary>排行页：按浏览量 / 最新 / 图片数 / 点赞浏览热门漫画（app API 不支持天/周/月周期）。</summary>
public partial class RankView : CardGridViewBase
{
    private const int PageSize = 20;

    private readonly JmHttpClient _client;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly LocalLibraryService _localLibrary;

    /// <summary>已下载漫画的 (源,id) 键集合（卡片右上角徽章）。</summary>
    private HashSet<string> _downloadedKeys = new();
    private SearchSort _sort = SearchSort.View;
    private RankPeriod _period = RankPeriod.All;
    private long _total;
    private long _page = 1;
    private bool _hasLoaded;

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
        typeof(RankView),
        new PropertyMetadata(State.Loading));

    public RankView()
    {
        InitializeComponent();
        _client = App.Services.GetRequiredService<JmHttpClient>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _localLibrary = App.Services.GetRequiredService<LocalLibraryService>();
        SortBox.SelectedIndex = 0;
    }

    /// <summary>导航到本页时调用：仅首次加载或用户显式切换周期时刷新，其余保持已浏览状态。</summary>
    public async void OnShown(RankPeriod? period = null)
    {
        if (period.HasValue && period.Value != _period)
        {
            _period = period.Value;
            SelectPeriod(period.Value);
            await LoadAsync(1);
            return;
        }
        if (!_hasLoaded)
        {
            await LoadAsync(1);
        }
    }

    private void SelectPeriod(RankPeriod period)
    {
        var target = period switch
        {
            RankPeriod.Today => PeriodToday,
            RankPeriod.Week => PeriodWeek,
            RankPeriod.Month => PeriodMonth,
            _ => PeriodAll,
        };
        target.IsChecked = true;
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        var period = sender switch
        {
            RadioButton r when ReferenceEquals(r, PeriodToday) => RankPeriod.Today,
            RadioButton r when ReferenceEquals(r, PeriodWeek) => RankPeriod.Week,
            RadioButton r when ReferenceEquals(r, PeriodMonth) => RankPeriod.Month,
            _ => RankPeriod.All,
        };
        if (period != _period)
        {
            _period = period;
            _ = LoadAsync(1);
        }
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            var sort = Enum.TryParse<SearchSort>(tag, out var parsed) ? parsed : SearchSort.View;
            if (sort != _sort)
            {
                _sort = sort;
                _ = LoadAsync(1);
            }
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(_page - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(_page + 1);

    private void EmptyRetryButton_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(1);

    private async Task LoadAsync(long page)
    {
        if (page < 1)
        {
            return;
        }

        _page = page;
        SetBusy(true);
        try { var dir = _config.Current.DownloadDir; _downloadedKeys = await Task.Run(() => _localLibrary.GetDownloadedKeys(dir)); } catch { _downloadedKeys = new HashSet<string>(); }
        try
        {
            var resp = await _client.CategoriesFilterAsync(page, _sort, _period);
            if (resp.IsAlbum && resp.AlbumRespData is { } album)
            {
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
            TotalText.Text = $"共 {_total} 部";
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
            _hasLoaded = true;
            SetBusy(false);
        }
    }

    private AlbumCardViewModel ToCard(AlbumInSearchRespData item)
    {
        var id = long.TryParse(item.Id, out var parsed) ? parsed : 0;
        return new AlbumCardViewModel
        {
            Id = item.Id,
            Name = item.Name,
            AuthorText = string.IsNullOrEmpty(item.Author) ? "未知作者" : item.Author,
            CoverUrl = JmSource.NormalizeCover(id, item.Image),
            IsFavorite = item.IsFavorite,
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

    /// <summary>排行页内容区状态。</summary>
    public enum State
    {
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
            CurrentState = State.Empty;
        }
    }

    private void ShowState(State state)
    {
        CurrentState = state;
        PagingPanel.Visibility = state == State.Result ? Visibility.Visible : Visibility.Collapsed;
    }
}




