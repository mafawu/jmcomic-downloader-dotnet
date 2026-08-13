using JmComic.Core.Utils;
using Xunit;

namespace JmComic.Core.Tests;

public class FilenameFilterTests
{
    [Theory]
    [InlineData("a/b\\c", "a b c")]
    [InlineData("a:b", "a：b")]
    [InlineData("a*b", "a⭐b")]
    [InlineData("a?b", "a？b")]
    [InlineData("a\"b", "a'b")]
    [InlineData("a<b", "a《b")]
    [InlineData("a>b", "a》b")]
    [InlineData("a|b", "a丨b")]
    [InlineData("a.b", "a·b")]
    [InlineData("  a  ", "a")]
    public void Filter_Replaces_InvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FilenameFilter.Filter(input));
    }

    [Fact]
    public void Filter_Keeps_ChineseAndLetters()
    {
        const string input = "我的漫画 My Comic 123";
        Assert.Equal(input, FilenameFilter.Filter(input));
    }
}
