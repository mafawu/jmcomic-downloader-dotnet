using System.Windows.Input;
using JmComic.App.Common;

namespace JmComic.App.ViewModels;

/// <summary>搜索结果 / 收藏列表中的漫画卡片。</summary>
public class AlbumCardViewModel : ObservableObject
{
    public string Id { get; init; } = "";

    /// <summary>聚合搜索时显示来源站点（如 "禁漫天堂"）；单源模式为空。</summary>
    public string SourceBadge { get; init; } = "";

    /// <summary>加载封面需要的请求头（防盗链 Referer 等）。</summary>
    public IReadOnlyDictionary<string, string>? ImageHeaders { get; init; }

    public string Kind { get; init; } = "Manga";
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
    public string ChapterId { get; init; } = "";
    public string AlbumId { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>在线阅读命令（打开在线阅读器并跳到本章）。</summary>
    public ICommand? ReadCommand { get; set; }

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
