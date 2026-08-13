using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using JmComic.Core.Utils;

namespace JmComic.Core.Tests;

public class WebpDecoderTests
{
    [Fact]
    public void Decodes_Webp_And_Resizes_To_MaxWidth()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-webp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.webp");
        try
        {
            using (var image = new Image<Rgba32>(2000, 1000))
            {
                image.Mutate(x => x.BackgroundColor(Color.Red));
                image.Save(path, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
            }

            var decoded = WebpImageDecoder.Decode(path, 800);

            Assert.NotNull(decoded);
            Assert.Equal(800, decoded!.Width);
            Assert.Equal(400, decoded.Height);
            Assert.Equal(800 * 400 * 4, decoded.BgraPixels.Length);
            // 无损 webp：红色像素在 Bgra 顺序下应为 B=0 G=0 R=255 A=255
            Assert.Equal(0, decoded.BgraPixels[0]);
            Assert.Equal(0, decoded.BgraPixels[1]);
            Assert.Equal(255, decoded.BgraPixels[2]);
            Assert.Equal(255, decoded.BgraPixels[3]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Keeps_Original_Size_When_Narrower_Than_MaxWidth()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-webp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "small.webp");
        try
        {
            using (var image = new Image<Rgba32>(500, 300))
            {
                image.Save(path, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
            }

            var decoded = WebpImageDecoder.Decode(path, 800);

            Assert.NotNull(decoded);
            Assert.Equal(500, decoded!.Width);
            Assert.Equal(300, decoded.Height);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Returns_Null_For_Invalid_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-webp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "bad.webp");
        try
        {
            File.WriteAllText(path, "not a webp");
            Assert.Null(WebpImageDecoder.Decode(path, 800));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

