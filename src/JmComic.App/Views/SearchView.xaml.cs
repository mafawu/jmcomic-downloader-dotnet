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

public partial class SearchView : CardGridViewBase
{
    private readonly SourceManager _sourceManager;
    private readonly AggregateSearchService _aggregate;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly LocalLibraryService _localLibrary;

    private HashSet<string> _downloadedKeys = new();
    private IComicSource? _filterSource;
    private long _page = 1;
    private int _searchVersion;
    private CancellationTokenSource? _searchCts;
    private string _cachedKeyword = "";
    private readonly Dictionary<string, Dictionary<long, SourceSearchGroup>> _sourcePageCache = new();

    public ObservableCollection<AlbumCardViewModel> Results { get; } = new();

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

    private void BuildSourceTabs()
    {
        foreach (var source in _sourceManager.Sources)
        {
            var isFirst = SourceTabs.Children.Count == 0;
            var tab = new RadioButton
            {
                Style = (Style)FindResource("FilterTabStyle"),
                Content = source.Info.DisplayName,
                Tag = source,
                IsChecked = isFirst,
                Margin = isFirst ? new Thickness(0) : new Thickness(8, 0, 0, 0),
            };
            tab.Click += SourceTab_Click;
            SourceTabs.Children.Add(tab);
        }
        _filterSource = _sourceManager.Sources.FirstOrDefault();
        if (_filterSource != null)
        {
            _sourceManager.Current = _filterSource;
        }
    }

    private void SourceTab_Click(object sender, RoutedEventArgs e)
    {
        var source = (sender as RadioButton)?.Tag as IComicSource;
        if (source == null) return;
        _filterSource = source;
        _sourceManager.Current = source;
        if (!string.IsNullOrWhiteSpace(KeywordBox.Text))
        {
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

    private void UpdateKeywordPlaceholder()
    {
        var hasText = !string.IsNullOrEmpty(KeywordBox.Text);
        var isFocused = KeywordBox.IsKeyboardFocused;
        KeywordPlaceholder.Visibility = !hasText && !isFocused ? Visibility.Visible : Visibility.Collapsed;
        ClearKeywordButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Search(string keyword)
    {
        KeywordBox.Text = keyword;
        _ = SearchAsync(1, force: true);
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

    private IComicSource SelectedSource => _filterSource ?? _sourceManager.Current;

    private async Task SearchAsync(long page, bool force = false)
    {
        var keyword = KeywordBox.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ToastService.Show("请输入搜索关键词", ToastKind.Info);
            return;
        }
        if (page < 1) return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var searchCt = _searchCts.Token;

        _page = page;
        var version = ++_searchVersion;
        if (force || keyword != _cachedKeyword)
        {
            _sourcePageCache.Clear();
            _cachedKeyword = keyword;
        }

        var source = SelectedSource;
        SetBusy(true);
        try
        {
            try
            {
                var keysTask = Task.Run(() => _localLibrary.GetDownloadedKeys(_config.Current.DownloadDir), searchCt);
                var completed = await Task.WhenAny(keysTask, Task.Delay(TimeSpan.FromSeconds(2), searchCt));
                if (completed == keysTask && keysTask.IsCompletedSuccessfully)
                    _downloadedKeys = keysTask.Result;
            }
            catch (OperationCanceledException) { return; }
            catch { }
            if (searchCt.IsCancellationRequested) return;

            var (group, fromCache) = await GetOrSearchSourceAsync(source, keyword, (int)page, searchCt);
            if (version != _searchVersion || searchCt.IsCancellationRequested) return;
            RenderSingle(group, page, showError: !fromCache);
        }
        catch (OperationCanceledException) { }
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
                SetBusy(false);
        }
    }

    private async Task<(SourceSearchGroup Group, bool FromCache)> GetOrSearchSourceAsync(
        IComicSource source, string keyword, int page, CancellationToken ct = default)
    {
        if (TryGetCachedGroup(source.Info.Id, page, out var cached))
        {
            return (cached, true);
        }
        var group = await _aggregate.SearchSourceAsync(source, keyword, page, ct);
        StoreGroup(group, page);
        return (group, false);
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
