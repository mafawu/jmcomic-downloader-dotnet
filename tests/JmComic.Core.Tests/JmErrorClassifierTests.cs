using System.Net;
using System.Net.Sockets;
using JmComic.Core.Errors;

namespace JmComic.Core.Tests;

/// <summary>
/// JmErrorClassifier 分类测试：类型优先（HTTP 状态码/网络异常），
/// 关键词兜底（404 / timeout / max retries 等），Unknown 保留原文。
/// </summary>
public class JmErrorClassifierTests
{
    [Fact]
    public void Http404_ClassifiesAsNotFound()
    {
        var ex = new HttpRequestException("not found", null, HttpStatusCode.NotFound);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.NotFound, info.Type);
        Assert.Contains("未找到", info.Message);
    }

    [Fact]
    public void Http401_ClassifiesAsAuth()
    {
        var ex = new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Auth, info.Type);
        Assert.Contains("账号和密码", info.Message);
    }

    [Fact]
    public void Http403_ClassifiesAsPermission()
    {
        var ex = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Permission, info.Type);
    }

    [Fact]
    public void HttpRequestWithoutStatus_ClassifiesAsNetwork()
    {
        var ex = new HttpRequestException("connection refused");
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Network, info.Type);
        Assert.Contains("自动尝试其他可用接口域名", info.Message);
    }

    [Fact]
    public void Timeout_ClassifiesAsNetwork()
    {
        var ex = new TaskCanceledException("The operation was canceled due to timeout", null, CancellationToken.None);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Network, info.Type);
        Assert.Contains("超时", info.Message);
    }

    [Fact]
    public void UserCancellation_RemainsUnknown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("canceled", null, cts.Token);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Unknown, info.Type);
    }

    [Fact]
    public void SocketException_ClassifiesAsNetwork()
    {
        var ex = new SocketException((int)SocketError.HostUnreachable);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Network, info.Type);
    }

    [Fact]
    public void JmExceptionWith404InMessage_ClassifiesAsNotFound()
    {
        var ex = new JmException("使用漫画详情失败，预料之外的状态码(404): <html>...");
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.NotFound, info.Type);
    }

    [Fact]
    public void LoginErrorMessage_ClassifiesAsAuth()
    {
        var ex = new JmException("使用账号密码登录失败: 账号或密码错误");
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Auth, info.Type);
    }

    [Fact]
    public void TimeoutKeywordInMessage_ClassifiesAsNetwork()
    {
        var info = JmErrorClassifier.Classify("请求超时: A connection attempt failed because the connected party did not properly respond");
        Assert.Equal(JmErrorType.Network, info.Type);
    }

    [Fact]
    public void MaxRetriesKeyword_ClassifiesAsNetwork()
    {
        var info = JmErrorClassifier.Classify("max retries exceeded with url");
        Assert.Equal(JmErrorType.Network, info.Type);
    }

    [Fact]
    public void PartialDownloadMessage_ClassifiesAsDownloadFailed()
    {
        var info = JmErrorClassifier.Classify("`第1话`总共有`10`张图片，但只下载了`8`张");
        Assert.Equal(JmErrorType.DownloadFailed, info.Type);
        Assert.Contains("部分图片", info.Message);
    }

    [Fact]
    public void UnknownException_KeepsOriginalMessage()
    {
        var ex = new InvalidOperationException("some local error");
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.Unknown, info.Type);
        Assert.Equal("some local error", info.Message);
    }

    [Fact]
    public void InnerExceptionIsInspected()
    {
        var inner = new HttpRequestException("boom", null, HttpStatusCode.NotFound);
        var ex = new JmException("外层包装", inner);
        var info = JmErrorClassifier.Classify(ex);
        Assert.Equal(JmErrorType.NotFound, info.Type);
        Assert.Contains("外层包装", info.Detail);
    }

    [Fact]
    public void Null_ClassifiesAsUnknown()
    {
        var info = JmErrorClassifier.Classify((Exception?)null);
        Assert.Equal(JmErrorType.Unknown, info.Type);
        Assert.False(string.IsNullOrWhiteSpace(info.Message));
    }

    [Fact]
    public void NullMessage_ClassifiesAsUnknown()
    {
        var info = JmErrorClassifier.Classify((string?)null!);
        Assert.Equal(JmErrorType.Unknown, info.Type);
    }
}
