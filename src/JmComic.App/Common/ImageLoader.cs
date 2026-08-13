using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JmComic.Core;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace JmComic.App.Common;

/// <summary>
/// 异步图片加载附加属性：给 Image.Source 绑定一个 URL 或本地文件路径字符串即可异步加载，
/// 加载失败时保持占位（透明）。
/// 性能设计：本地封面缩略解码（约 360px）并在后台线程解码，带缓存与并发限制；
/// 本地 webp 图片会自动缩放转码为 PNG 显示（WPF 原生不支持 webp）。
/// </summary>
public static class ImageLoader
{
    private const int ThumbnailWidth = 360;
    private const int MaxLocalCacheCount = 512;

    private static readonly HttpClient HttpClient;
    private static readonly ConcurrentDictionary<string, BitmapImage> LocalCache = new();
    private static readonly SemaphoreSlim DecodeGate = new(4);

    static ImageLoader()
    {
        HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", JmConstants.UserAgent);
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(ImageLoader), new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
        {
            return;
        }
        var value = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            image.Source = null;
            return;
        }

        // 用 Tag 作令牌，防止旧请求覆盖新 URL
        var token = Guid.NewGuid();
        image.Tag = token;
        image.Source = null;

        try
        {
            BitmapImage? bitmap;
            if (File.Exists(value))
            {
                bitmap = await LoadLocalAsync(value);
            }
            else if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await HttpClient.GetByteArrayAsync(value);
                bitmap = await Task.Run(() => DecodeBytes(bytes));
            }
            else
            {
                return;
            }

            if (!Equals(image.Tag, token))
            {
                return;
            }
            image.Source = bitmap;
        }
        catch
        {
            if (Equals(image.Tag, token))
            {
                image.Source = null;
            }
        }
    }

    /// <summary>本地图片：带缓存 + 并发限制 + 后台线程解码。</summary>
    private static async Task<BitmapImage?> LoadLocalAsync(string path)
    {
        if (LocalCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        await DecodeGate.WaitAsync();
        try
        {
            if (LocalCache.TryGetValue(path, out cached))
            {
                return cached;
            }
            var bitmap = await Task.Run(() => DecodeLocalFile(path));
            if (bitmap is null)
            {
                return null;
            }
            if (LocalCache.Count >= MaxLocalCacheCount)
            {
                LocalCache.Clear();
            }
            LocalCache[path] = bitmap;
            return bitmap;
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    /// <summary>后台线程执行：解码本地图片（webp 缩放转 PNG，其余缩略解码）。</summary>
    private static BitmapImage? DecodeLocalFile(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            using var image = ImageSharpImage.Load(stream);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(ThumbnailWidth, ThumbnailWidth),
                Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
            }));
            using var pngStream = new MemoryStream();
            image.Save(pngStream, new PngEncoder());
            return DecodeBytes(pngStream.ToArray());
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = ThumbnailWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>后台线程执行：从字节解码为冻结的缩略位图。</summary>
    private static BitmapImage DecodeBytes(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = ThumbnailWidth;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

