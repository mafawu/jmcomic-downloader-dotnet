using System.Text.Json;
using JmComic.Core.Models;

namespace JmComic.Core.Services;

/// <summary>
/// 本地视频库服务：管理视频文件夹列表（CRUD），扫描文件夹中的视频文件，
/// 持久化到 video-folders.json。缩略图暂不自动提取，后续可扩展 FFmpeg。
/// </summary>
public class VideoLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>常见视频扩展名。</summary>
    public static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".webm", ".wmv", ".mov", ".flv", ".m4v", ".ts", ".rmvb"
    };

    private readonly string _filePath;
    private List<VideoFolder> _folders;

    public VideoLibraryService(string filePath)
    {
        _filePath = filePath;
        _folders = Load();
    }

    public IReadOnlyList<VideoFolder> Folders => _folders;

    // ====================== CRUD ======================

    public VideoFolder Add(string name, string folderPath, string series = "", List<string>? tags = null, List<string>? actors = null)
    {
        var folder = new VideoFolder
        {
            Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(folderPath) : name.Trim(),
            FolderPath = folderPath,
            Series = series,
            Tags = tags ?? new List<string>(),
            Actors = actors ?? new List<string>(),
        };
        folder.Files = ScanVideoFiles(folderPath);
        _folders.Add(folder);
        Save();
        return folder;
    }

    public void Update(VideoFolder folder)
    {
        var idx = _folders.FindIndex(f => f.Id == folder.Id);
        if (idx >= 0)
        {
            _folders[idx] = folder;
            Save();
        }
    }

    public void Remove(string id)
    {
        _folders.RemoveAll(f => f.Id == id);
        Save();
    }

    public VideoFolder? GetById(string id)
        => _folders.FirstOrDefault(f => f.Id == id);

    /// <summary>重新扫描指定文件夹中的视频文件。</summary>
    public List<VideoFile> Rescan(string folderId)
    {
        var folder = GetById(folderId);
        if (folder is null) return new List<VideoFile>();
        folder.Files = ScanVideoFiles(folder.FolderPath);
        Save();
        return folder.Files;
    }

    // ====================== 扫描 ======================

    /// <summary>扫描目录中的视频文件（不递归子目录，只统计顶层视频文件）。</summary>
    public static List<VideoFile> ScanVideoFiles(string dirPath)
    {
        var result = new List<VideoFile>();
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
        {
            return result;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(dirPath))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!VideoExtensions.Contains(ext))
                {
                    continue;
                }
                var fi = new FileInfo(file);
                result.Add(new VideoFile
                {
                    FileName = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    FileSizeBytes = fi.Length,
                });
            }
        }
        catch
        {
            // 目录不可读时返回空
        }

        return result.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ====================== 持久化 ======================

    private List<VideoFolder> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<List<VideoFolder>>(File.ReadAllText(_filePath));
                if (data is not null) return data;
            }
        }
        catch { }
        return new List<VideoFolder>();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_folders, JsonOptions));
            File.Move(temp, _filePath, true);
        }
        catch { }
    }
}