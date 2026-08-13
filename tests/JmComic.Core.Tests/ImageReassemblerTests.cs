using JmComic.Core.Downloading;
using JmComic.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JmComic.Core.Tests;

/// <summary>图片保存的原子写入行为：目标文件完整、无 .tmp 残留。</summary>
public class ImageReassemblerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-img-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void SaveImage_BlockNumZero_Writes_Complete_File_Without_Tmp_Leftover()
    {
        var dir = NewTempDir();
        try
        {
            var savePath = Path.Combine(dir, "001.jpg");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            ImageReassembler.SaveImage(savePath, DownloadFormat.Jpeg, 0, bytes);

            Assert.True(File.Exists(savePath));
            Assert.Equal(bytes, File.ReadAllBytes(savePath));
            Assert.False(File.Exists(savePath + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SaveImage_BlockNumGreaterThanZero_Reassembles_And_Cleans_Tmp()
    {
        var dir = NewTempDir();
        try
        {
            using var img = new Image<Rgba32>(4, 4);
            img.Mutate(x => x.BackgroundColor(Color.Red));
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms);
            var srcBytes = ms.ToArray();

            var savePath = Path.Combine(dir, "001.jpg");
            ImageReassembler.SaveImage(savePath, DownloadFormat.Jpeg, 2, srcBytes);

            Assert.True(File.Exists(savePath));
            using (var loaded = Image.Load(savePath))
            {
                Assert.Equal(4, loaded.Width);
                Assert.Equal(4, loaded.Height);
            }
            Assert.False(File.Exists(savePath + ".tmp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}