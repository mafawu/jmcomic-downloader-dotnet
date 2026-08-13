using JmComic.Core.Utils;
using Xunit;

namespace JmComic.Core.Tests;

public class MangaFilenameParserTests
{
    [Theory]
    [InlineData("[汉化组 (作者名)] (C97) 作品标题 [中文]", "作品标题")]
    [InlineData("[社团 (作者)] タイトル", "タイトル")]
    [InlineData("[無修正] タイトル [中国翻訳]", "タイトル")]
    [InlineData("[全彩] タイトル [中文]", "タイトル")]
    [InlineData("[XXX汉化] (C99) [社团 (作者)] タイトル", "タイトル")]
    [InlineData("[漢化組 (作者)] (C97) 作品名 [中文]", "作品名")]
    [InlineData("(Fantia) 标题", "标题")]
    [InlineData("(合集) 标题", "标题")]
    [InlineData("[作者] 标题", "标题")]
    [InlineData("普通标题", "普通标题")]
    [InlineData("タイトル (作品名) [汉化]", "タイトル")]
    [InlineData("(C97) [社团 (作者)] タイトル", "タイトル")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Parse_ReturnsCleanTitle(string? fileName, string expectedTitle)
    {
        var (title, _) = MangaFilenameParser.Parse(fileName ?? "");
        Assert.Equal(expectedTitle, title);
    }

    [Theory]
    // 期望标签用逗号分隔，比较时不关心顺序
    [InlineData("[汉化组 (作者名)] (C97) 作品标题 [中文]", "组:汉化组,作者:作者名,会场:C97,其他:中文,标题:作品标题")]
    [InlineData("[社团 (作者)] タイトル", "组:社团,作者:作者,标题:タイトル")]
    [InlineData("[無修正] タイトル [中国翻訳]", "其他:无修正,汉化:中国翻译,标题:タイトル")]
    [InlineData("[全彩] タイトル [中文]", "其他:全彩,其他:中文,标题:タイトル")]
    [InlineData("[XXX汉化] (C99) [社团 (作者)] タイトル", "组:社团,作者:作者,会场:C99,汉化:XXX汉化,标题:タイトル")]
    [InlineData("[漢化組 (作者)] (C97) 作品名 [中文]", "组:漢化組,作者:作者,会场:C97,其他:中文,标题:作品名")]
    [InlineData("(Fantia) 标题", "平台:Fantia,标题:标题")]
    [InlineData("(合集) 标题", "作品:合集,标题:标题")]
    [InlineData("[作者] 标题", "作者:作者,标题:标题")]
    [InlineData("普通标题", "标题:普通标题")]
    [InlineData("タイトル (作品名) [汉化]", "作品:作品名,汉化:汉化,标题:タイトル")]
    [InlineData("(C97) [社团 (作者)] タイトル", "组:社团,作者:作者,会场:C97,标题:タイトル")]
    public void Parse_ReturnsExpectedTags(string fileName, string expectedJoined)
    {
        var (_, tags) = MangaFilenameParser.Parse(fileName);
        var expected = expectedJoined.Split(',').ToHashSet();
        Assert.True(expected.SetEquals(tags));
    }
}
