using JmComic.Core.Services;
using Xunit;

namespace JmComic.Core.Tests;

public class TitleTranslatorTests
{
    [Theory]
    // 去掉方括号/圆括号标签块后，合并标题主体中的连续汉字
    [InlineData("[禁漫汉化组](C107)[えるうなぎ(えるう)]幼驯染は性奉仕当番[中国翻译]", "幼驯染性奉仕当番")]
    [InlineData("[驴子汉化组] (C107) [陆の孤岛亭(しゃよー)] 樱春女学院の男优 5 [中国翻译]", "樱春女学院男优")]
    [InlineData("[K個人漢化] [K-てん] 性知識0の彼女はエロガキの精液便所 [中國翻譯]", "性知識彼女精液便所")]
    // 纯日文/英文标题提取不到中文，返回空（留给在线翻译）
    [InlineData("純情セックスフレンド", "純情")]
    [InlineData("Dragon Night", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ExtractChineseName_ReturnsExpected(string? title, string expected)
    {
        Assert.Equal(expected, TitleTranslator.ExtractChineseName(title!));
    }

    [Theory]
    // 残留假名或括号 = 翻译未完成，需要重译
    [InlineData("[咬牙 【TS】TS吧！2～雌性堕落篇～【女体化】]", true)]
    [InlineData("小蝦米翻 食いしん坊コジマ", true)]
    [InlineData("[17H 蜥臀目 SKIN 正常任务03 中文 DL版]", true)]
    [InlineData("十色がをん 不醒的子", true)]
    // 干净的中文名 / 提取结果 / 无括号无假名 = 完成
    [InlineData("性知識彼女精液便所", false)]
    [InlineData("怀疑 1+2+after", false)]
    [InlineData("2024年12月3日", false)]
    [InlineData("幼驯染性奉仕当番", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksUnfinished_DetectsLeftoverKanaOrBrackets(string? name, bool expected)
    {
        Assert.Equal(expected, TitleTranslator.LooksUnfinished(name));
    }
}
