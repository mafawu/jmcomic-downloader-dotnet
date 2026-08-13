using System.Net;
using System.Text;
using JmComic.Core;
using JmComic.Core.Http;
using JmComic.Core.Models;
using JmComic.Core.Services;

namespace JmComic.Core.Tests;

public class JmHttpClientDomainRotationTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<string> RequestedHosts = new();

        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnRequest { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedHosts.Add(request.RequestUri!.Host);
            if (OnRequest is not null)
            {
                return OnRequest(request);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("<html>404 Not Found</html>", Encoding.UTF8),
            });
        }
    }

    /// <summary>构造可被客户端解密的成功响应：从 tokenparam 头中取 ts 并用相同密钥加密。</summary>
    private static string BuildSuccessBody(HttpRequestMessage request, string plaintext)
    {
        var tokenparam = request.Headers.TryGetValues("tokenparam", out var values) ? values.First() : "";
        var ts = long.Parse(tokenparam.Split(',')[0]);
        var data = TestCrypto.EncryptData(ts, plaintext);
        return $"{{\"code\":200,\"data\":\"{data}\"}}";
    }

    private static ConfigService ConfigWithDomains(params string[] domains)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-http-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var list = string.Join(",", domains.Select(d => $"\"{d}\""));
        File.WriteAllText(path, $"{{\"apiDomains\":[{list}]}}");
        return new ConfigService(path);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8),
    };

    private static HttpResponseMessage Html404() => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent("<html>404 Not Found</html>", Encoding.UTF8),
    };

    [Fact]
    public async Task Falls_Back_To_Next_Domain_When_First_Returns_Domain_Error()
    {
        var handler = new FakeHandler();
        handler.OnRequest = request => Task.FromResult(
            request.RequestUri!.Host == "a.example.com"
                ? Html404()
                : Ok(BuildSuccessBody(request, "{\"id\":123,\"name\":\"测试漫画\"}")));
        var client = new JmHttpClient(ConfigWithDomains("a.example.com", "b.example.com"), handler);

        var album = await client.GetAlbumAsync(123);

        Assert.Equal(123, album.Id);
        Assert.Equal("测试漫画", album.Name);
        Assert.Equal(new[] { "a.example.com", "b.example.com" }, handler.RequestedHosts);
    }

    [Fact]
    public async Task Fails_When_All_Domains_Unavailable()
    {
        var handler = new FakeHandler(); // 默认全部返回 404 错误页
        var client = new JmHttpClient(ConfigWithDomains("a.example.com", "b.example.com"), handler);

        var ex = await Assert.ThrowsAsync<JmException>(() => client.GetAlbumAsync(123));

        Assert.Contains("所有接口域名均不可用", ex.Message);
        Assert.Contains("a.example.com", ex.Message);
        Assert.Contains("b.example.com", ex.Message);
        Assert.Equal(new[] { "a.example.com", "b.example.com" }, handler.RequestedHosts);
    }

    [Fact]
    public async Task Business_Error_Does_Not_Switch_Domain()
    {
        var handler = new FakeHandler();
        handler.OnRequest = _ => Task.FromResult(Ok("{\"code\":403,\"data\":\"\",\"error_msg\":\"风控\"}"));
        var client = new JmHttpClient(ConfigWithDomains("a.example.com", "b.example.com"), handler);

        var ex = await Assert.ThrowsAsync<JmException>(() => client.GetAlbumAsync(123));

        Assert.Contains("预料之外的code", ex.Message);
        Assert.Equal(new[] { "a.example.com" }, handler.RequestedHosts);
    }

    [Fact]
    public async Task Failed_Domain_Is_Skipped_On_Next_Request()
    {
        var handler = new FakeHandler();
        handler.OnRequest = request => Task.FromResult(
            request.RequestUri!.Host == "a.example.com"
                ? Html404()
                : Ok(BuildSuccessBody(request, "{\"id\":123,\"name\":\"测试漫画\"}")));
        var client = new JmHttpClient(ConfigWithDomains("a.example.com", "b.example.com"), handler);

        var first = await client.GetAlbumAsync(123);
        Assert.NotNull(first);
        handler.RequestedHosts.Clear();

        var second = await client.GetAlbumAsync(123);

        Assert.NotNull(second);
        Assert.Equal(new[] { "b.example.com" }, handler.RequestedHosts);
    }

    /// <summary>构造带登录凭据的配置（自动重登录需要）。</summary>
    private static ConfigService ConfigWithCredentials(string username, string password, params string[] domains)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-http-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var list = string.Join(",", domains.Select(d => $"\"{d}\""));
        File.WriteAllText(path, $"{{\"apiDomains\":[{list}],\"username\":\"{username}\",\"password\":\"{password}\"}}");
        return new ConfigService(path);
    }

    [Fact]
    public async Task Auth_Request_Stays_On_Session_Domain_After_Login()
    {
        var handler = new FakeHandler();
        handler.OnRequest = request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/login")
            {
                return Task.FromResult(Ok(BuildSuccessBody(request, "{\"username\":\"user\"}")));
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/favorite")
            {
                return Task.FromResult(Ok(BuildSuccessBody(request, "{\"list\":[],\"count\":0}")));
            }
            return Task.FromResult(Html404());
        };
        var client = new JmHttpClient(ConfigWithCredentials("user", "pass", "a.example.com", "b.example.com"), handler);

        await client.LoginAsync("user", "pass");
        handler.RequestedHosts.Clear();

        var fav = await client.GetFavoriteFolderAsync(0, 1, FavoriteSort.FavoriteTime);

        Assert.NotNull(fav);
        // AVS Cookie 仅对登录域名有效：收藏请求必须固定在该域名，而不是轮换到下一个
        Assert.Equal(new[] { "a.example.com" }, handler.RequestedHosts);
    }

    [Fact]
    public async Task Auth_Request_Relogins_On_New_Domain_When_Session_Domain_Down()
    {
        var favCalls = 0;
        var handler = new FakeHandler();
        handler.OnRequest = request =>
        {
            var host = request.RequestUri!.Host;
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/login")
            {
                return Task.FromResult(Ok(BuildSuccessBody(request, "{\"username\":\"user\"}")));
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/favorite")
            {
                if (host == "a.example.com")
                {
                    return Task.FromResult(Html404()); // 会话域名故障
                }
                favCalls++;
                return favCalls == 1
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("{\"code\":401,\"data\":\"\",\"errorMsg\":\"請先登入會員\"}", Encoding.UTF8),
                    })
                    : Task.FromResult(Ok(BuildSuccessBody(request, "{\"list\":[],\"count\":0}")));
            }
            return Task.FromResult(Html404());
        };
        var client = new JmHttpClient(ConfigWithCredentials("user", "pass", "a.example.com", "b.example.com"), handler);

        await client.LoginAsync("user", "pass");
        handler.RequestedHosts.Clear();

        var fav = await client.GetFavoriteFolderAsync(0, 1, FavoriteSort.FavoriteTime);

        Assert.NotNull(fav);
        Assert.Equal(2, favCalls);
        // 顺序：会话域名 a 故障 -> b 首次 401 -> b 自动重登录 -> b 重试成功
        Assert.Equal(
            new[] { "a.example.com", "b.example.com", "b.example.com", "b.example.com" },
            handler.RequestedHosts);
    }
}


