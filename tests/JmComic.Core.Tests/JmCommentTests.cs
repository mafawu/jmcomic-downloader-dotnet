using System.Net;
using System.Text;
using JmComic.Core.Http;
using JmComic.Core.Services;
using JmComic.Core.Sources.Jm;

namespace JmComic.Core.Tests;

/// <summary>评论获取：验证 /forum 请求参数与评论/回评解析（对齐 jmcomic 参考实现）。</summary>
public class JmCommentTests
{
    private sealed class CommentHandler : HttpMessageHandler
    {
        public string? LastQuery { get; private set; }
        public string ResponseJson { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastQuery = request.RequestUri!.Query;
            var tokenparam = request.Headers.TryGetValues("tokenparam", out var values) ? values.First() : "";
            var ts = long.Parse(tokenparam.Split(',')[0]);
            var encrypted = TestCrypto.EncryptData(ts, ResponseJson);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"code\":200,\"data\":\"{encrypted}\"}}", Encoding.UTF8),
            });
        }
    }

    private static JmSource CreateSource(CommentHandler handler)
    {
        var dir = Path.Combine(Path.GetTempPath(), "jm-comment-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new JmSource(new JmHttpClient(new ConfigService(Path.Combine(dir, "config.json")), handler));
    }

    [Fact]
    public async Task GetComments_Sends_Forum_Query()
    {
        var handler = new CommentHandler();
        var source = CreateSource(handler);

        await source.GetCommentsAsync("302820", 2);

        Assert.Contains("mode=all", handler.LastQuery);
        Assert.Contains("page=2", handler.LastQuery);
        Assert.Contains("aid=302820", handler.LastQuery);
    }

    [Fact]
    public async Task GetComments_Maps_Comments_And_Nested_Replies()
    {
        var handler = new CommentHandler
        {
            ResponseJson = """
                {"list":[
                  {"CID":"10908956","AID":"302820","UID":"16829597","username":"6hao","nickname":"6hao",
                   "likes":"0","addtime":"Aug 12, 2026","parent_CID":"0",
                   "content":"<div style='flex-direction:row;flex-wrap:wrap;'>真的有人喜歡自己被綠嗎</div>",
                   "spoiler":"1","replys":[
                     {"CID":"10909000","UID":"19017451","username":"FuckSlutCunt","nickname":"大屌猛男666",
                      "likes":"0","addtime":"Aug 12, 2026","parent_CID":"10908956",
                      "content":"<div>有的兄弟有的😂<br>第二行</div>","spoiler":"2"}
                   ]},
                  {"CID":"10901908","AID":"302820","UID":"1519506","username":"cv13572468","nickname":"偽純愛戰士",
                   "likes":"0","addtime":"Aug 10, 2026","parent_CID":"0",
                   "content":"觀前提醒","spoiler":"0","replys":[]}
                ],"total":"415"}
                """,
        };
        var source = CreateSource(handler);

        var page = await source.GetCommentsAsync("302820", 1);

        Assert.Equal(415, page.Total);
        Assert.Equal(1, page.Page);
        Assert.Equal(42, page.PageCount);
        Assert.Equal(2, page.Items.Count);

        var main = page.Items[0];
        Assert.Equal("10908956", main.CommentId);
        Assert.Equal("302820", main.AlbumId);
        Assert.Equal("16829597", main.UserId);
        Assert.Null(main.ParentCommentId);
        Assert.Equal("6hao", main.Nickname);
        Assert.Equal("真的有人喜歡自己被綠嗎", main.Content);
        Assert.False(main.IsSpoiler);
        Assert.Single(main.Replies);

        var reply = main.Replies[0];
        Assert.Equal("10909000", reply.CommentId);
        Assert.Equal("10908956", reply.ParentCommentId);
        Assert.Equal("有的兄弟有的😂\n第二行", reply.Content);
        Assert.True(reply.IsSpoiler);
        Assert.Empty(reply.Replies);

        var second = page.Items[1];
        Assert.Equal("10901908", second.CommentId);
        Assert.Equal("觀前提醒", second.Content);
        Assert.False(second.IsSpoiler);
        Assert.Empty(second.Replies);
    }

    [Fact]
    public async Task GetComments_Total_Invalid_Is_Null()
    {
        var handler = new CommentHandler { ResponseJson = """{"list":[],"total":"abc"}""" };
        var source = CreateSource(handler);

        var page = await source.GetCommentsAsync("302820", 1);

        Assert.Null(page.Total);
        Assert.Null(page.PageCount);
        Assert.Empty(page.Items);
    }
}