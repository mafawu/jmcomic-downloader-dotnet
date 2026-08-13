using JmComic.Core.Downloading;
using Xunit;

namespace JmComic.Core.Tests;

public class BlockNumCalculatorTests
{
    [Theory]
    // id < scramble_id → 0
    [InlineData(220_980, 100, "x", 0)]
    [InlineData(220_980, 200_000, "y", 0)]
    // id < 268_850 → 10
    [InlineData(220_980, 250_000, "y", 10)]
    [InlineData(0, 0, "z", 10)]
    // id >= 268_850：x = id < 421_926 ? 10 : 8
    // md5("300000abc") 末字符 'e'(101) % 10 = 1 → 2*1+2 = 4
    [InlineData(220_980, 300_000, "abc", 4)]
    // md5("421926test1") 末字符 'a'(97)，x=8，97%8=1 → 4
    [InlineData(220_980, 421_926, "test1", 4)]
    // md5("421927test2") 末字符 'f'(102)，x=8，102%8=6 → 14
    [InlineData(220_980, 421_927, "test2", 14)]
    // md5("468984abc") 末字符 '0'(48)，x=8，48%8=0 → 2
    [InlineData(220_980, 468_984, "abc", 2)]
    public void Calculate_Matches_GoldenVectors(long scrambleId, long id, string filename, uint expected)
    {
        var actual = BlockNumCalculator.Calculate(scrambleId, id, filename);
        Assert.Equal(expected, actual);
    }
}
