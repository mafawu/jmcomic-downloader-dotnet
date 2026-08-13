namespace JmComic.Core.Sources;

/// <summary>内容源元信息：UI 的站点切换、能力展示等均依赖此信息。</summary>
public class ComicSourceInfo
{
    /// <summary>源唯一标识，如 "jm"、"wnacg"。</summary>
    public string Id { get; init; } = "";

    /// <summary>展示名称，如 "禁漫天堂"。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>该源的核心能力（搜索/详情/下载）是否需要登录。</summary>
    public bool RequiresLogin { get; init; }
}
