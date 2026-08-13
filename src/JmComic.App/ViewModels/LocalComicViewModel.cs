using System.Windows.Input;
using JmComic.Core.Models;

namespace JmComic.App.ViewModels;

/// <summary>本地漫画卡片（封面 + 名字 + 标签 + 章节数）。</summary>
public class LocalComicViewModel
{
    public string Name { get; init; } = "";
    public string NameCn { get; init; } = "";
    public string CoverPath { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    public int ChapterCount { get; init; }
    public long ImageCount { get; init; }
    public bool HasMetadata { get; init; }

    /// <summary>卡片上展示的标签（最多 3 个）。</summary>
    public List<string> DisplayTags { get; init; } = new();

    /// <summary>中文名与原名不同且非空时，在卡片上展示中文名。</summary>
    public bool ShowNameCn => !string.IsNullOrEmpty(NameCn) && NameCn != Name;

    /// <summary>章节/图片统计文本。</summary>
    public string StatsText { get; init; } = "";

    public ICommand? OpenFolderCommand { get; set; }
    public ICommand? OpenReaderCommand { get; set; }

    /// <summary>对应的本地漫画数据（供详情面板/更新检查使用）。</summary>
    public LocalComic Source { get; init; } = null!;
}

