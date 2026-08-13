using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JmComic.Core.Utils;

/// <summary>
/// WebP 解码辅助：用 ImageSharp 解码本地 WebP 文件并缩放到目标宽度，
/// 输出 Bgra32 像素数据（供 WPF 构造 BitmapSource，无需 PNG/JPEG 转码中间步骤）。
/// 用于系统 WIC 不支持 WebP 时的兜底路径。
/// </summary>
public static class WebpImageDecoder
{
    public sealed record DecodedImage(int Width, int Height, byte[] BgraPixels);

    /// <summary>解码 WebP 文件；宽超限时按比例缩放到 maxWidth。失败返回 null。</summary>
    public static DecodedImage? Decode(string path, int maxWidth)
    {
        try
        {
            using var image = Image.Load<Bgra32>(path);
            if (image.Width > maxWidth)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxWidth),
                    Mode = ResizeMode.Max,
                }));
            }

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            return new DecodedImage(image.Width, image.Height, pixels);
        }
        catch
        {
            // 文件损坏 / 不是有效 WebP 时返回 null，由调用方决定占位处理
            return null;
        }
    }
}

