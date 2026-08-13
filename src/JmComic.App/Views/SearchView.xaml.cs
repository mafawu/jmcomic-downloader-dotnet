using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.App.Controls;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core.Downloading;
using JmComic.Core.Services;
using JmComic.Core.Sources;

namespace JmComic.App.Views;

/// <summary>
/// 搜索页：关键词 + 来源筛选 Tab（全部聚合 / 单源）+ 结果网格 + 分页。
/// 聚合模式下并发查询所有免登录源，卡片带来源徽标；单源模式跳转详情携带 (sourceId, comicId)。
/// 搜索结果按「源 + 页码」缓存：切 Tab 只筛选/补搜，不重复搜索已搜过的源。
/// </summary>
public partial class SearchView : CardGridViewBase
{

    private readonly SourceManager _sourceManager;
    private readonly AggregateSearchService _aggregate;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly LocalLibraryService _localLibrary;

    /// <summary>已下载漫画的 (源,id) 键集合（卡片右上角徽章）。</summary>
    private HashSet<string> _downloadedKeys = new();
    /// <summary>null 表示聚合全部源，否则限定单源搜索。</summary>
    private IComicSource? _filterSource;
    private long _page = 1;

    /// <summary>搜索版本号：切 Tab/翻页/新搜索时递增，用于丢弃过期的异步响应。</summary>
    private int _searchVersion;
    /// <summary>缓存对应的关键词；关键词变化时清空缓存全量重搜。</summary>
    private string _cachedKeyword = "";
    /// <summary>按「源 id → 页码 → 分组」缓存搜索结果；失败分组也缓存，避免切 Tab 反复重搜/弹错。</summary>
    private readonly Dictionary<string, Dictionary<long, SourceSearchGroup>> _sourcePageCache = new();

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
        _sourceManager = App.Services.GetRequiredService<SourceManager>();
        _aggregate = App.Services.GetRequiredService<AggregateSearchService>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _localLibrary = App.Services.GetRequiredService<LocalLibraryService>();
        BuildSourceTabs();
    }

    /// <summary>按已注册源生成「全部 + 每源」筛选 Tab。</summary>
    private void BuildSourceTabs()
    {
        var all = new RadioButton
        {
            Style = (Style)FindResource("FilterTabStyle"),
            Content = "全部",
            Tag = null,
            IsChecked = true,
        };
        all.Click += SourceTab_Click;
        SourceTabs.Children.Add(all);

        foreach (var source in _sourceManager.Sources)
        {
            var tab = new RadioButton
            {
                Style = (Style)FindResource("FilterTabStyle"),
                Content = source.Info.DisplayName,
                Tag = source,
                Margin = new Thickness(8, 0, 0, 0),
            };
            tab.Click += SourceTab_Click;
            SourceTabs.Children.Add(tab);
        }
    }

    private void SourceTab_Click(object sender, RoutedEventArgs e)
    {
        _filterSource = (sender as RadioButton)?.Tag as IComicSource;
        if (!string.IsNullOrWhiteSpace(KeywordBox.Text))
        {
            // 保留当前页码：全部→单源直接筛选缓存，单源→另一单源才补搜，单源→全部只补搜缺失源
            _ = SearchAsync(_page);
        }
    }

    private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SearchAsync(1, force: true);
        }
    }

    private void KeywordBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateKeywordPlaceholder();
        KeywordBox.CaretIndex = 0;
    }

    private void KeywordBox_LostFocus(object sender, RoutedEventArgs e) => UpdateKeywordPlaceholder();

    private void KeywordBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateKeywordPlaceholder();

    /// <summary>占位提示仅在「内容为空且未聚焦」时显示。</summary>
    private void UpdateKeywordPlaceholder()
    {
        KeywordPlaceholder.Visibility =
            string.IsNullOrEmpty(KeywordBox.Text) && !KeywordBox.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(1, force: true);
    private void ClearKeywordButton_Click(object sender, RoutedEventArgs e)
    {
        KeywordBox.Clear();
        KeywordBox.Focus();
    }

    private void EmptyRetryButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(1, force: true);

    private void PrevButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page + 1);

    private async Task SearchAsync(long page, bool force = false)
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

        _page = page;
        var version = ++_searchVersion;
        // 新关键词或显式搜索（回车/按钮/重试）时清空缓存全量重搜；切 Tab/翻页复用缓存
        if (force || keyword != _cachedKeyword)
        {
            _sourcePageCache.Clear();
            _cachedKeyword = keyword;
        }

        _downloadedKeys = _localLibrary.GetDownloadedKeys(_config.Current.DownloadDir);
        SetBusy(true);
        try
        {
            if (_filterSource is { } source)
            {
                var (group, fromCache) = await GetOrSearchSourceAsync(source, keyword, (int)page);
                if (version != _searchVersion)
                {
                    return;
                }
                RenderSingle(group, page, showError: !fromCache);
            }
            else
            {
                var (all, newFailed) = await SearchAggregateCachedAsync(keyword, (int)page);
                if (version != _searchVersion)
                {
                    return;
                }
                RenderAggregate(all, page, newFailed);
            }
        }
        catch (Exception ex)
        {
            if (version == _searchVersion)
            {
                ShowState(State.Empty);
                ToastService.ShowError(ex);
            }
        }
        finally
        {
            if (version == _searchVersion)
            {
                SetBusy(false);
            }
        }
    }

    /// <summary>取某源某页结果：缓存命中直接返回，未命中补搜并写缓存（失败也写缓存）。</summary>
    private async Task<(SourceSearchGroup Group, bool FromCache)> GetOrSearchSourceAsync(
        IComicSource source, string keyword, int page)
    {
        if (TryGetCachedGroup(source.Info.Id, page, out var cached))
        {
            return (cached, true);
        }
        var group = await _aggregate.SearchSourceAsync(source, keyword, page);
        StoreGroup(group, page);
        return (group, false);
    }

    /// <summary>聚合模式：只补搜当前页缺失的源，已缓存源直接复用；返回全部源分组及本次新失败列表。</summary>
    private async Task<(List<SourceSearchGroup> All, List<SourceSearchGroup> NewFailed)> SearchAggregateCachedAsync(
        string keyword, int page)
    {
        var missing = _aggregate.Sources
            .Where(s => !TryGetCachedGroup(s.Info.Id, page, out _))
            .ToList();

        var newGroups = new List<SourceSearchGroup>();
        if (missing.Count > 0)
        {
            var tasks = missing.Select(s => _aggregate.SearchSourceAsync(s, keyword, page)).ToArray();
            newGroups.AddRange(await Task.WhenAll(tasks));
            foreach (var group in newGroups)
            {
                StoreGroup(group, page);
            }
        }

        var all = new List<SourceSearchGroup>(_aggregate.Sources.Count);
        foreach (var source in _aggregate.Sources)
        {
            if (TryGetCachedGroup(source.Info.Id, page, out var group))
            {
                all.Add(group);
            }
        }
        return (all, newGroups.Where(g => g.Error is not null).ToList());
    }

    private bool TryGetCachedGroup(string sourceId, int page, out SourceSearchGroup group)
    {
        if (_sourcePageCache.TryGetValue(sourceId, out var pages) && pages.TryGetValue(page, out var cached))
        {
            group = cached;
            return true;
        }
        group = null!;
        return false;
    }

    private void StoreGroup(SourceSearchGroup group, int page)
    {
        if (!_sourcePageCache.TryGetValue(group.Source.Info.Id, out var pages))
        {
            pages = new Dictionary<long, SourceSearchGroup>();
            _sourcePageCache[group.Source.Info.Id] = pages;
        }
        pages[page] = group;
    }

    /// <summary>渲染单源结果；命中唯一漫画时直接打开详情。showError=false 表示复用缓存失败，不再弹错。</summary>
    private void RenderSingle(SourceSearchGroup group, long page, bool showError)
    {
        var result = group.Result;
        if (result is null)
        {
            ShowState(State.Empty);
            if (showError)
            {
                ToastService.Show($"{group.Source.Info.DisplayName} 搜索失败", ToastKind.Error);
            }
            return;
        }
        if (result.IsSingleMatch && result.SingleComicId is { } singleId)
        {
            // 搜索命中唯一漫画：直接打开详情
            Results.Clear();
            ShowState(State.Result);
            Navigation.OpenComic(group.Source.Info.Id, singleId);
            return;
        }
        if (result.Items.Count == 0)
        {
            ShowState(State.Empty);
            return;
        }

        Results.Clear();
        foreach (var item in result.Items)
        {
            Results.Add(ToCard(item, group.Source, false));
        }
        UpdatePaging(page, result.TotalPages);
    }

    /// <summary>渲染聚合结果：按源注册顺序合并，仅提示本次新失败的源。</summary>
    private void RenderAggregate(List<SourceSearchGroup> groups, long page, List<SourceSearchGroup> newFailed)
    {
        var okGroups = groups.Where(g => g.Result is not null).ToList();
        if (okGroups.Count == 0)
        {
            ShowState(State.Empty);
            if (newFailed.Count > 0)
            {
                ToastService.Show("全部来源搜索失败，请稍后重试", ToastKind.Error);
            }
            return;
        }

        Results.Clear();
        foreach (var group in okGroups)
        {
            foreach (var item in group.Result!.Items)
            {
                Results.Add(ToCard(item, group.Source, true));
            }
        }

        if (newFailed.Count > 0)
        {
            var names = string.Join("、", newFailed.Select(f => f.Source.Info.DisplayName));
            ToastService.Show($"{names} 搜索失败，已展示其余来源结果", ToastKind.Info);
        }
        UpdatePaging(page, groups.Count == 0 ? 1 : groups.Max(g => g.Result?.TotalPages ?? 1));
    }

    private void UpdatePaging(long page, long totalPages)
    {
        PageText.Text = $"第 {page} 页";
        PrevButton.IsEnabled = page > 1;
        NextButton.IsEnabled = page < totalPages;
        ShowState(State.Result);
    }

    private AlbumCardViewModel ToCard(ComicSummary item, IComicSource source, bool showBadge) => new()
    {
        Id = item.Id,
        Name = item.Title,
        AuthorText = string.IsNullOrEmpty(item.Author) ? "未知作者" : item.Author,
        CoverUrl = item.CoverUrl,
        SourceBadge = showBadge ? source.Info.DisplayName : "",
        ImageHeaders = source.Info.CoverHeaders,
        IsDownloaded = _downloadedKeys.Contains(LocalLibraryService.KeyFor(source.Info.Id, item.Id)),
        OpenCommand = new RelayCommand(_ => Navigation.OpenComic(source.Info.Id, item.Id)),
        DownloadCommand = new AsyncRelayCommand(async _ =>
        {
            try
            {
                var (count, title) = await DownloadHelper.EnqueueAllAsync(
                    source, _config, _downloadManager, item.Id);
                ToastService.Show($"已将「{title}」全部 {count} 个章节加入下载队列", ToastKind.Success);
            }
            catch (Exception ex)
            {
                ToastService.ShowError(ex);
            }
        }),
    };

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