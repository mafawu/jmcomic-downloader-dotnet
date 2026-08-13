using System.Threading.Channels;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;
using JmComic.Core.Utils;

namespace JmComic.Core.Downloading;

/// <summary>
/// 多线程下载引擎（对应原 Rust 实现 download_manager.rs）。
/// 并发控制：最多同时获取 10 个章节的图片 URL、下载 3 个章节、40 张图片。
/// 通过 C# 事件向前端推送实时进度。
/// </summary>
public class DownloadManager : IDisposable
{
    private readonly JmHttpClient _jmClient;
    private readonly ConfigService _configService;
    private readonly HttpClient _imageClient;

    private readonly Channel<ChapterInfo> _channel;
    private readonly SemaphoreSlim _urlsWithBlockNumSem = new(10);
    private readonly SemaphoreSlim _chapterSem = new(3);
    private readonly SemaphoreSlim _imgSem = new(40);

    private long _bytePerSec;
    private long _downloadedImageCount;
    private long _totalImageCount;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiverTask;

    public event EventHandler<ChapterPendingEventArgs>? ChapterPending;
    public event EventHandler<ChapterStartEventArgs>? ChapterStart;
    public event EventHandler<ImageSuccessEventArgs>? ImageSuccess;
    public event EventHandler<ImageErrorEventArgs>? ImageError;
    public event EventHandler<ChapterEndEventArgs>? ChapterEnd;
    public event EventHandler<OverallProgressEventArgs>? OverallProgress;
    public event EventHandler<SpeedEventArgs>? SpeedChanged;

    public DownloadManager(JmHttpClient jmClient, ConfigService configService)
        : this(jmClient, configService, jmClient.CreateImageClient())
    {
    }

    /// <summary>测试用构造：注入图片下载 HttpClient（与 API 客户端共用 FakeHandler）。</summary>
    internal DownloadManager(JmHttpClient jmClient, ConfigService configService, HttpClient imageClient)
    {
        _jmClient = jmClient;
        _configService = configService;
        _imageClient = imageClient;

        _channel = Channel.CreateBounded<ChapterInfo>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
        _receiverTask = ReceiverLoopAsync(_cts.Token);
        _ = SpeedLoopAsync(_cts.Token);
    }

    /// <summary>提交一个章节的下载任务。</summary>
    public async Task SubmitChapterAsync(ChapterInfo chapterInfo, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(chapterInfo, ct);
    }

    private async Task ReceiverLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var chapterInfo in _channel.Reader.ReadAllAsync(ct))
            {
                _ = ProcessChapterAsync(chapterInfo, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 应用退出
        }
    }

    /// <summary>每秒上报一次下载速度。</summary>
    private async Task SpeedLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                var bytePerSec = Interlocked.Exchange(ref _bytePerSec, 0);
                var megaBytePerSec = bytePerSec / 1024.0 / 1024.0;
                SpeedChanged?.Invoke(this, new SpeedEventArgs($"{megaBytePerSec:F2}MB/s"));
            }
        }
        catch (OperationCanceledException)
        {
            // 应用退出
        }
    }

    private async Task ProcessChapterAsync(ChapterInfo chapterInfo, CancellationToken ct)
    {
        var tempDownloadDir = GetTempDownloadDir(chapterInfo);
        var downloadedCount = 0;

        try
        {
            ChapterPending?.Invoke(this, new ChapterPendingEventArgs(
                chapterInfo.ChapterId, chapterInfo.ChapterTitle, chapterInfo.AlbumTitle));

            var downloadFormat = _configService.Current.DownloadFormat;
            var ext = downloadFormat.Extension();

            // 获取此章节每张图片的下载链接以及对应的 block_num
            var urlsWithBlockNum = await GetUrlsWithBlockNumAsync(chapterInfo.ChapterId, ct);

            // 总共需要下载的图片数量
            var total = urlsWithBlockNum.Count;
            Interlocked.Add(ref _totalImageCount, total);

            // 断点续传：正式目录已存在且包含全部图片 → 整章跳过，无需重新下载
            var parentDir = Path.Combine(_configService.Current.DownloadDir, chapterInfo.AlbumTitle);
            var finalDir = Path.Combine(parentDir, chapterInfo.ChapterTitle);
            if (IsChapterComplete(finalDir, total, ext))
            {
                ChapterStart?.Invoke(this, new ChapterStartEventArgs(chapterInfo.ChapterId, total));
                Interlocked.Add(ref _downloadedImageCount, total);
                ChapterEnd?.Invoke(this, new ChapterEndEventArgs(chapterInfo.ChapterId, null));
                return;
            }

            Directory.CreateDirectory(tempDownloadDir);
            // 清理临时目录中不属于本次预期文件名集合的文件（旧格式后缀 / 残留 .tmp / 页数变化后的多余文件）
            CleanupTempDir(tempDownloadDir, total, ext);

            // 限制同时下载的章节数量
            await _chapterSem.WaitAsync(ct);
            try
            {
                ChapterStart?.Invoke(this, new ChapterStartEventArgs(chapterInfo.ChapterId, total));

                var tasks = new List<Task>();
                for (var i = 0; i < urlsWithBlockNum.Count; i++)
                {
                    var (url, blockNum) = urlsWithBlockNum[i];
                    var savePath = Path.Combine(tempDownloadDir, $"{i + 1:D3}.{ext}");
                    var task = DownloadImageAsync(
                        chapterInfo.ChapterId, url, savePath, downloadFormat, blockNum,
                        () => Interlocked.Increment(ref downloadedCount), ct);
                    tasks.Add(task);
                }
                await Task.WhenAll(tasks);
            }
            finally
            {
                _chapterSem.Release();
            }

            // 如果所有图片全部已处理（无论成功或失败），则清空全局进度计数
            var currentDownloaded = Interlocked.Read(ref _downloadedImageCount);
            var currentTotal = Interlocked.Read(ref _totalImageCount);
            if (currentDownloaded == currentTotal)
            {
                Interlocked.Exchange(ref _downloadedImageCount, 0);
                Interlocked.Exchange(ref _totalImageCount, 0);
            }

            // 检查此章节的图片是否全部下载成功
            if (downloadedCount == total)
            {
                // 下载成功：正式目录已存在（如之前不完整）则合并文件，否则直接改名
                Directory.CreateDirectory(parentDir);
                if (Directory.Exists(finalDir))
                {
                    MergeTempIntoFinal(tempDownloadDir, finalDir);
                }
                else
                {
                    Directory.Move(tempDownloadDir, finalDir);
                }
                ChapterEnd?.Invoke(this, new ChapterEndEventArgs(chapterInfo.ChapterId, null));
            }
            else
            {
                var errMsg = $"`{chapterInfo.ChapterTitle}`总共有`{total}`张图片，但只下载了`{downloadedCount}`张";
                ChapterEnd?.Invoke(this, new ChapterEndEventArgs(chapterInfo.ChapterId, errMsg));
            }
        }
        catch (Exception ex)
        {
            ChapterEnd?.Invoke(this, new ChapterEndEventArgs(chapterInfo.ChapterId, ex.Message));
        }
    }
    private async Task DownloadImageAsync(
        long chapterId, string url, string savePath, DownloadFormat downloadFormat,
        uint blockNum, Func<int> onSuccess, CancellationToken ct)
    {
        // 断点续传：目标文件已存在且非空（原子写入保证完整）→ 跳过下载，直接计为成功
        if (IsFileComplete(savePath))
        {
            var count = onSuccess();
            ImageSuccess?.Invoke(this, new ImageSuccessEventArgs(chapterId, savePath, count));
            ReportProgress();
            return;
        }

        // 限制同时下载的图片数量
        await _imgSem.WaitAsync(ct);
        byte[] imageData;
        try
        {
            imageData = await GetImageBytesAsync(url, ct);
        }
        catch (Exception ex)
        {
            ImageError?.Invoke(this, new ImageErrorEventArgs(chapterId, url, ex.Message));
            return;
        }
        finally
        {
            _imgSem.Release();
        }

        // 保存图片（图片拼接是 CPU 密集型操作，放到线程池执行，避免阻塞）
        try
        {
            await Task.Run(() =>
            {
                ImageReassembler.SaveImage(savePath, downloadFormat, blockNum, imageData);
                // 记录下载字节数（仅统计成功保存的图片）
                Interlocked.Add(ref _bytePerSec, imageData.Length);
                var count = onSuccess();
                ImageSuccess?.Invoke(this, new ImageSuccessEventArgs(chapterId, savePath, count));
            }, ct);
        }
        catch (Exception ex)
        {
            ImageError?.Invoke(this, new ImageErrorEventArgs(chapterId, url, ex.Message));
        }
        finally
        {
            ReportProgress();
        }

        return;

        // 每处理完一张图（无论成败 / 是否跳过）更新一次全局进度
        void ReportProgress()
        {
            var downloaded = Interlocked.Increment(ref _downloadedImageCount);
            var total = Interlocked.Read(ref _totalImageCount);
            var percentage = total == 0 ? 0 : downloaded * 100.0 / total;
            OverallProgress?.Invoke(this, new OverallProgressEventArgs(downloaded, total, percentage));
        }
    }
    private async Task<List<(string Url, uint BlockNum)>> GetUrlsWithBlockNumAsync(long chapterId, CancellationToken ct)
    {
        // 限制同时获取 urls_with_block_num 的数量
        await _urlsWithBlockNumSem.WaitAsync(ct);
        try
        {
            var scrambleId = await _jmClient.GetScrambleIdAsync(chapterId, ct);
            var chapterRespData = await _jmClient.GetChapterAsync(chapterId, ct);

            var urlsWithBlockNum = new List<(string, uint)>();
            foreach (var filename in chapterRespData.Images)
            {
                var ext = Path.GetExtension(filename).ToLowerInvariant();
                if (ext != ".webp")
                {
                    continue;
                }
                var filenameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                var blockNum = BlockNumCalculator.Calculate(scrambleId, chapterId, filenameWithoutExt);
                var url = $"https://{JmConstants.ImageDomain}/media/photos/{chapterId}/{filename}";
                urlsWithBlockNum.Add((url, blockNum));
            }
            return urlsWithBlockNum;
        }
        finally
        {
            _urlsWithBlockNumSem.Release();
        }
    }

    private async Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct)
    {
        var lastException = (Exception?)null;
        // 简单重试 3 次
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var httpResp = await _imageClient.GetAsync(url, ct);
                if (httpResp.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    var text = await httpResp.Content.ReadAsStringAsync(ct);
                    throw new JmException($"下载图片`{url}`失败，预料之外的状态码: {text}");
                }
                return await httpResp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex) when (attempt < 2)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        throw lastException ?? new JmException($"下载图片`{url}`失败");
    }

    private string GetTempDownloadDir(ChapterInfo chapterInfo)
    {
        // 以 `.下载中-` 开头，表示是临时目录（与原版一致）
        return Path.Combine(
            _configService.Current.DownloadDir,
            chapterInfo.AlbumTitle,
            $".下载中-{chapterInfo.ChapterTitle}");
    }


    /// <summary>断点续传判断：文件已存在且非空（原子写入保证完整）。</summary>
    internal static bool IsFileComplete(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0;

    /// <summary>章节是否已完整下载：目录存在且包含全部 001..N 的图片文件。</summary>
    internal static bool IsChapterComplete(string dir, int imageCount, string ext)
    {
        if (imageCount <= 0 || !Directory.Exists(dir))
        {
            return false;
        }
        for (var i = 1; i <= imageCount; i++)
        {
            if (!IsFileComplete(Path.Combine(dir, $"{i:D3}.{ext}")))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>清理临时目录中不属于本次预期文件名集合的文件（旧格式 / 残留 .tmp / 页数变化）。</summary>
    private static void CleanupTempDir(string tempDownloadDir, int total, string ext)
    {
        var expected = new HashSet<string>(total, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < total; i++)
        {
            expected.Add($"{i + 1:D3}.{ext}");
        }
        foreach (var file in Directory.EnumerateFiles(tempDownloadDir))
        {
            if (!expected.Contains(Path.GetFileName(file)))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 清理失败不影响下载
                }
            }
        }
    }

    /// <summary>把临时目录中的文件合并进已存在的正式目录，然后删除临时目录。</summary>
    private static void MergeTempIntoFinal(string tempDownloadDir, string finalDir)
    {
        foreach (var file in Directory.EnumerateFiles(tempDownloadDir))
        {
            File.Move(file, Path.Combine(finalDir, Path.GetFileName(file)), true);
        }
        Directory.Delete(tempDownloadDir, true);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            _receiverTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 忽略退出时的任务异常
        }
        _cts.Dispose();
        _chapterSem.Dispose();
        _imgSem.Dispose();
        _urlsWithBlockNumSem.Dispose();
        _imageClient.Dispose();
    }
}
