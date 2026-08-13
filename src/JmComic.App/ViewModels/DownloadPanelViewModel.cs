using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using JmComic.App.Common;
using JmComic.App.Services;
using JmComic.Core.Downloading;

namespace JmComic.App.ViewModels;

/// <summary>
/// 下载面板视图模型：订阅 DownloadManager 事件，
/// 汇总章节进度、全局进度与实时速度。
/// </summary>
public class DownloadPanelViewModel : ObservableObject, IDisposable
{
    private readonly DownloadManager _manager;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<long, DownloadItemViewModel> _itemsById = new();

    public DownloadPanelViewModel(DownloadManager manager)
    {
        _manager = manager;
        _dispatcher = Application.Current.Dispatcher;

        _manager.ChapterPending += OnChapterPending;
        _manager.ChapterStart += OnChapterStart;
        _manager.ImageSuccess += OnImageSuccess;
        _manager.ImageError += OnImageError;
        _manager.ChapterEnd += OnChapterEnd;
        _manager.OverallProgress += OnOverallProgress;
        _manager.SpeedChanged += OnSpeedChanged;
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    private double _overallProgress;
    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, value);
    }

    private string _progressSummary = "";
    public string ProgressSummary
    {
        get => _progressSummary;
        private set => SetProperty(ref _progressSummary, value);
    }

    private string _speed = "0.00 MB/s";
    public string Speed
    {
        get => _speed;
        private set => SetProperty(ref _speed, value);
    }

    private bool _hasDownloads;
    public bool HasDownloads
    {
        get => _hasDownloads;
        private set => SetProperty(ref _hasDownloads, value);
    }
    private bool _allDone;
    /// <summary>全部任务均已完成（用于面板全局进度绿色高亮）。</summary>
    public bool AllDone
    {
        get => _allDone;
        private set => SetProperty(ref _allDone, value);
    }

    private void OnChapterPending(object? sender, ChapterPendingEventArgs e) => _dispatcher.Invoke(() =>
    {
        var item = GetOrAdd(e.ChapterId, e.AlbumTitle, e.ChapterTitle);
        item.StatusText = "等待中…";
        item.IsDownloading = false;
        AllDone = false;
    });

    private void OnChapterStart(object? sender, ChapterStartEventArgs e) => _dispatcher.Invoke(() =>
    {
        if (!_itemsById.TryGetValue(e.ChapterId, out var item))
        {
            return;
        }
        item.TotalCount = e.Total;
        item.Progress = 0;
        item.ProgressText = $"0 / {e.Total}";
        item.StatusText = "下载中…";
        item.IsDownloading = true;
        item.IsFailed = false;
        item.IsDone = false;
        AllDone = false;
    });

    private void OnImageSuccess(object? sender, ImageSuccessEventArgs e) => _dispatcher.Invoke(() =>
    {
        if (!_itemsById.TryGetValue(e.ChapterId, out var item))
        {
            return;
        }
        var total = item.TotalCount > 0 ? item.TotalCount : e.DownloadedCount;
        item.Progress = total == 0 ? 100 : e.DownloadedCount * 100.0 / total;
        item.ProgressText = $"{e.DownloadedCount} / {total}";
    });

    private void OnImageError(object? sender, ImageErrorEventArgs e) => _dispatcher.Invoke(() =>
    {
        if (!_itemsById.TryGetValue(e.ChapterId, out var item))
        {
            return;
        }
        item.StatusText = "下载出错";
        item.IsFailed = true;
        AllDone = false;
    });

    private void OnChapterEnd(object? sender, ChapterEndEventArgs e) => _dispatcher.Invoke(() =>
    {
        if (!_itemsById.TryGetValue(e.ChapterId, out var item))
        {
            return;
        }
        item.IsDownloading = false;
        if (e.ErrMsg is null)
        {
            item.StatusText = "已完成";
            item.IsDone = true;
            item.Progress = 100;
            ToastService.Show($"「{item.ChapterTitle}」下载完成", ToastKind.Success);
        }
        else
        {
            item.StatusText = "下载失败";
            item.IsFailed = true;
            ToastService.Show(e.ErrMsg, ToastKind.Error);
        }
        AllDone = Items.Count > 0 && Items.All(i => i.IsDone);
    });

    private void OnOverallProgress(object? sender, OverallProgressEventArgs e) => _dispatcher.Invoke(() =>
    {
        OverallProgress = e.Percentage;
        ProgressSummary = $"{e.DownloadedImageCount} / {e.TotalImageCount} 张";
    });

    private void OnSpeedChanged(object? sender, SpeedEventArgs e) => _dispatcher.Invoke(() =>
    {
        Speed = e.Speed;
    });

    private DownloadItemViewModel GetOrAdd(long chapterId, string albumTitle, string chapterTitle)
    {
        if (_itemsById.TryGetValue(chapterId, out var item))
        {
            return item;
        }
        item = new DownloadItemViewModel
        {
            ChapterId = chapterId,
            AlbumTitle = albumTitle,
            ChapterTitle = chapterTitle,
        };
        _itemsById[chapterId] = item;
        Items.Insert(0, item);

        // 最多保留 80 条历史记录
        while (Items.Count > 80)
        {
            var last = Items[^1];
            Items.RemoveAt(Items.Count - 1);
            _itemsById.Remove(last.ChapterId);
        }
        HasDownloads = true;
        return item;
    }

    public void Dispose()
    {
        _manager.ChapterPending -= OnChapterPending;
        _manager.ChapterStart -= OnChapterStart;
        _manager.ImageSuccess -= OnImageSuccess;
        _manager.ImageError -= OnImageError;
        _manager.ChapterEnd -= OnChapterEnd;
        _manager.OverallProgress -= OnOverallProgress;
        _manager.SpeedChanged -= OnSpeedChanged;
    }
}

