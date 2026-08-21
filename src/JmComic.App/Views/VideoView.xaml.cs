using System.IO;
using System.Windows;
using System.Windows.Controls;
using JmComic.App.Controls;
using JmComic.App.Services;
using JmComic.Core.Models;
using JmComic.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace JmComic.App.Views;

/// <summary>本地视频库：管理视频文件夹，卡片展示 + 筛选 + 排序。</summary>
public partial class VideoView : UserControl
{
    private readonly VideoLibraryService _library;
    private List<VideoFolder> _all = new();
    private List<VideoFolder> _filtered = new();
    private string _sortTag = "AddedDesc";

    public VideoView()
    {
        InitializeComponent();
        _library = App.Services.GetRequiredService<VideoLibraryService>();
        Loaded += (_, _) => Reload();
    }

    public void OnShown() => Reload();

    private void Reload()
    {
        _all = _library.Folders.ToList();
        ApplySortAndRender();
    }

    private void ApplySortAndRender()
    {
        _filtered = _sortTag switch
        {
            "AddedAsc" => _all.OrderBy(f => f.AddedDate).ToList(),
            "NameAsc" => _all.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "NameDesc" => _all.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "RatingDesc" => _all.OrderByDescending(f => f.Rating).ThenByDescending(f => f.AddedDate).ToList(),
            "SizeDesc" => _all.OrderByDescending(f => f.TotalSizeBytes).ToList(),
            _ => _all.OrderByDescending(f => f.AddedDate).ToList(),
        };

        VideoCountText.Text = _filtered.Count == 0 ? "" : $"共 {_filtered.Count} 个";
        EmptyPanel.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsScroll.Visibility = _filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CardsPanel.Children.Clear();

        foreach (var folder in _filtered)
        {
            var card = new VideoCard { Folder = folder, Width = 220 };
            card.OpenRequested += OnCardOpen;
            CardsPanel.Children.Add(card);
        }
    }

    private void OnCardOpen(object? sender, VideoFolder folder)
    {
        if (string.IsNullOrEmpty(folder.FolderPath) || !Directory.Exists(folder.FolderPath))
        {
            ToastService.Show("文件夹不存在或已被移动", ToastKind.Error);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", folder.FolderPath);
        }
        catch (Exception ex)
        {
            ToastService.ShowError(ex, "打开文件夹失败：");
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择视频文件夹",
        };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        // 防止重复添加
        if (_library.Folders.Any(f => string.Equals(f.FolderPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            ToastService.Show("该文件夹已存在", ToastKind.Info);
            return;
        }

        var added = _library.Add("", path);
        ToastService.Show($"已添加「{added.Name}」（{added.Files.Count} 个视频）", ToastKind.Success);
        Reload();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _sortTag = tag;
            if (_all.Count > 0) ApplySortAndRender();
        }
    }
}