using System.Windows.Input;
using JmComic.App.Common;

namespace JmComic.App.ViewModels;

/// <summary>搜索结果 / 收藏列表中的漫画卡片。</summary>
public class AlbumCardViewModel : ObservableObject
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string AuthorText { get; init; } = "";
    public string CoverUrl { get; init; } = "";
    public bool IsFavorite { get; init; }

    /// <summary>是否已下载到本地（卡片右上角显示徽章）。</summary>
    public bool IsDownloaded { get; init; }

    public ICommand? OpenCommand { get; set; }
    public ICommand? DownloadCommand { get; set; }
}

/// <summary>章节卡片（支持框选状态）。</summary>
public class ChapterCardViewModel : ObservableObject
{
    public long ChapterId { get; init; }
    public long AlbumId { get; init; }
    public string Title { get; init; } = "";

    private bool _isDownloaded;
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set => SetProperty(ref _isDownloaded, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
