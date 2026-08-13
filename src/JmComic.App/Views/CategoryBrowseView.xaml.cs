using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using JmComic.App.Common;
using Microsoft.Extensions.DependencyInjection;
using JmComic.App.Services;
using JmComic.App.ViewModels;
using JmComic.Core.Downloading;
using JmComic.Core.Services;
using JmComic.Core.Sources;

namespace JmComic.App.Views;

/// <summary>
/// 通用分类浏览页：面向任意实现 ICategorySource 的内容源（如绅士漫画）。
/// 左侧分类列表（含子分类），右侧漫画卡片网格 + 分页。
/// </summary>
public partial class CategoryBrowseView : UserControl
{
    private readonly SourceManager _sourceManager;
    private readonly ConfigService _config;
    private readonly DownloadManager _downloadManager;
    private readonly LocalLibraryService _localLibrary;

    /// <summary>已下载漫画的 (源,id) 键集合（卡片右上角徽章）。</summary>
    private HashSet<string> _downloadedKeys = new();
    private ICategorySource? _source;
    private string _categoryId = "";
    private long _page = 1;
    private long _totalPages = 1;

    public ObservableCollection<AlbumCardViewModel> Results { get; } = new();

    /// <summary>内容区当前状态。</summary>
    public State CurrentState
    {
        get => (State)GetValue(CurrentStateProperty);
        set => SetValue(CurrentStateProperty, value);
    }

    public static readonly DependencyProperty CurrentStateProperty = DependencyProperty.Register(
        nameof(CurrentState),
        typeof(State),
        typeof(CategoryBrowseView),
        new PropertyMetadata(State.Loading));

    public CategoryBrowseView()
    {
        InitializeComponent();
        _sourceManager = App.Services.GetRequiredService<SourceManager>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _downloadManager = App.Services.GetRequiredService<DownloadManager>();
        _localLibrary = App.Services.GetRequiredService<LocalLibraryService>();
    }

    /// <summary>导航到本页时调用：按当前源加载分类列表。</summary>
    public async void OnShown()
    {
        if (_sourceManager.Current is not ICategorySource source)
        {
            return;
        }
        _source = source;
        CategoryTitle.Text = $"{_sourceManager.Current.Info.DisplayName} 分类";
        await LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        CategoryList.Children.Clear();
        CurrentState = State.Loading;
        try
        {
            var categories = await _source!.GetCategoriesAsync();
            if (categories.Count == 0)
            {
                CurrentState = State.Empty;
                return;
            }

            var firstSelected = false;
            foreach (var category in categories)
            {
                AddCategoryPill(category, 0, ref firstSelected);
            }
            if (!firstSelected && CategoryList.Children.Count > 0 && CategoryList.Children[0] is RadioButton first)
            {
                first.IsChecked = true;
                await LoadComicsAsync(((ComicCategory)first.Tag).Id, 1);
            }
        }
        catch (Exception ex)
        {
            CurrentState = State.Empty;
            ToastService.ShowError(ex);
        }
    }

    private void AddCategoryPill(ComicCategory category, int depth, ref bool firstSelected)
    {
        var pill = new RadioButton
        {
            Style = (Style)FindResource("CategoryPillStyle"),
            Content = category.Name,
            Tag = category,
            Margin = new Thickness(depth * 16, 2, 8, 2),
            IsChecked = !firstSelected,
        };
        firstSelected = true;
        pill.Click += CategoryPill_Click;
        CategoryList.Children.Add(pill);

        foreach (var child in category.Children)
        {
            AddCategoryPill(child, depth + 1, ref firstSelected);
        }
    }

    private async void CategoryPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: ComicCategory category })
        {
            await LoadComicsAsync(category.Id, 1);
        }
    }

    private async Task LoadComicsAsync(string categoryId, long page)
    {
        if (_source is null || page < 1)
        {
            return;
        }
        _categoryId = categoryId;
        _page = page;
        _downloadedKeys = _localLibrary.GetDownloadedKeys(_config.Current.DownloadDir);
        CurrentState = State.Loading;
        PagingPanel.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _source.GetCategoryComicsAsync(categoryId, (int)page);
            Results.Clear();
            if (result.Items.Count == 0)
            {
                CurrentState = State.Empty;
                return;
            }

            foreach (var item in result.Items)
            {
                Results.Add(ToCard(item));
            }
            _totalPages = result.TotalPages;
            PageText.Text = $"第 {page} 页";
            PrevButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page < _totalPages;
            CurrentState = State.Result;
            PagingPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            CurrentState = State.Empty;
            ToastService.ShowError(ex);
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => _ = LoadComicsAsync(_categoryId, _page - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => _ = LoadComicsAsync(_categoryId, _page + 1);

    private AlbumCardViewModel ToCard(ComicSummary item)
    {
        var source = _sourceManager.Current;
        return new AlbumCardViewModel
        {
            Id = item.Id,
            Name = item.Title,
            AuthorText = string.IsNullOrEmpty(item.Author) ? "未知作者" : item.Author,
            CoverUrl = item.CoverUrl,
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
    }

    /// <summary>分类浏览页内容区状态。</summary>
    public enum State
    {
        Loading,
        Empty,
        Result,
    }
}



