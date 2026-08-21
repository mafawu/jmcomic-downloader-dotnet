using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.Core.Models;

namespace JmComic.App.Controls;

/// <summary>视频文件夹卡片：封面 + 名称 + 标签 + 进度条。</summary>
public partial class VideoCard : UserControl
{
    public static readonly DependencyProperty FolderProperty =
        DependencyProperty.Register(nameof(Folder), typeof(VideoFolder), typeof(VideoCard),
            new PropertyMetadata(null, OnFolderChanged));

    public VideoFolder? Folder
    {
        get => (VideoFolder?)GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    public event EventHandler<VideoFolder>? OpenRequested;

    public VideoCard()
    {
        InitializeComponent();
        MouseEnter += (_, _) => PlayOverlay.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => PlayOverlay.Visibility = Visibility.Collapsed;
    }

    private static void OnFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VideoCard card || e.NewValue is not VideoFolder folder) return;
        card.TitleText.Text = string.IsNullOrEmpty(folder.Name) ? "(未命名)" : folder.Name;
        card.FileCountText.Text = $"{folder.Files.Count} 个";
        card.SizeText.Text = folder.TotalSizeText;

        if (!string.IsNullOrEmpty(folder.Series))
        {
            card.SeriesText.Text = $"系列: {folder.Series}";
            card.SeriesText.Visibility = Visibility.Visible;
        }

        if (folder.Rating > 0)
        {
            card.RatingText.Text = new string('★', folder.Rating);
            card.RatingText.Visibility = Visibility.Visible;
        }

        card.TagItems.ItemsSource = folder.Tags.Take(3).ToList();

        // 封面（暂用占位图标，后续可加载缩略图）
        if (!string.IsNullOrEmpty(folder.CoverPath) && System.IO.File.Exists(folder.CoverPath))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(folder.CoverPath);
                bitmap.EndInit();
                card.CoverImage.Source = bitmap;
                card.PlaceholderIcon.Visibility = Visibility.Collapsed;
            }
            catch { }
        }
        else
        {
            card.CoverImage.Source = null;
            card.PlaceholderIcon.Visibility = Visibility.Visible;
        }

        // 进度条
        var progress = Math.Clamp(folder.WatchProgress, 0, 100);
        card.ProgressBar.Width = card.ActualWidth * progress / 100.0;
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        OpenRequested?.Invoke(this, Folder!);
    }
}