using System.Text.Json.Serialization;

namespace JmComic.Core.Models;

/// <summary>本地视频文件夹（对应 GreenResourcesManager 的 VideoFolder）。</summary>
public class VideoFolder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("folderPath")]
    public string FolderPath { get; set; } = "";

    [JsonPropertyName("series")]
    public string Series { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("actors")]
    public List<string> Actors { get; set; } = new();

    [JsonPropertyName("addedDate")]
    public DateTime AddedDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("files")]
    public List<VideoFile> Files { get; set; } = new();

    // ===== 用户数据 =====

    [JsonPropertyName("watchProgress")]
    public double WatchProgress { get; set; }

    [JsonPropertyName("watchCount")]
    public int WatchCount { get; set; }

    [JsonPropertyName("lastWatchedAt")]
    public string? LastWatchedAt { get; set; }

    [JsonPropertyName("rating")]
    public int Rating { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    /// <summary>文件夹中所有视频的总大小（字节）。</summary>
    [JsonIgnore]
    public long TotalSizeBytes => Files.Sum(f => f.FileSizeBytes);

    /// <summary>格式化的总大小。</summary>
    [JsonIgnore]
    public string TotalSizeText
    {
        get
        {
            if (TotalSizeBytes < 1024 * 1024) return $"{TotalSizeBytes / 1024.0:F0} KB";
            if (TotalSizeBytes < 1024L * 1024 * 1024) return $"{TotalSizeBytes / 1024.0 / 1024.0:F1} MB";
            return $"{TotalSizeBytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }
    }

    /// <summary>封面：取第一个有缩略图的文件。</summary>
    [JsonIgnore]
    public string CoverPath => Files.FirstOrDefault(f => !string.IsNullOrEmpty(f.ThumbnailPath))?.ThumbnailPath ?? "";
}

/// <summary>单个视频文件。</summary>
public class VideoFile
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("thumbnailPath")]
    public string? ThumbnailPath { get; set; }

    [JsonPropertyName("watchProgress")]
    public double WatchProgress { get; set; }

    [JsonPropertyName("lastWatchedAt")]
    public string? LastWatchedAt { get; set; }
}