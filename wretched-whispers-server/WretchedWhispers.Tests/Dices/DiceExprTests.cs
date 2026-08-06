using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Dices;

/// <summary>
/// DiceExpr.ToString renders standard dice notation rather than the record's default dump
/// ("DiceExpr { Count = 1, Sides = 4, Constant = 0 }"), which used to leak into adversary DTOs.
/// Parse is the inverse: standard notation in, DiceExpr out.
/// </summary>
public class DiceExprTests
{
    [Theory]
    [InlineData(1, 4, 0, "d4")]
    [InlineData(1, 8, 0, "d8")]
    [InlineData(2, 10, 0, "2d10")]
    [InlineData(1, 6, 2, "d6+2")]
    [InlineData(2, 10, 3, "2d10+3")]
    [InlineData(1, 6, -2, "d6-2")]
    [InlineData(2, 6, -1, "2d6-1")]
    public void RendersStandardNotation(int count, int sides, int constant, string expected)
    {
        Assert.Equal(expected, new DiceExpr(count, sides, constant).ToString());
    }

    [Fact]
    public void NamedHelpersRenderCleanly()
    {
        Assert.Equal("d20", DiceExpr.D20.ToString());
    }

    [Fact]
    public void RoundTripsThroughParse()
    {
        Assert.Equal("2d10+3", DiceExpr.Parse("2d10+3").ToString());
        Assert.Equal("d6-2", DiceExpr.Parse("1d6-2").ToString());
    }

    [Theory]
    [InlineData("d6", 1, 6, 0)] // omitted count defaults to 1
    [InlineData("2d4", 2, 4, 0)]
    [InlineData("1d8+2", 1, 8, 2)]
    [InlineData("2d10-3", 2, 10, -3)]
    [InlineData(" 2d4 ", 2, 4, 0)] // surrounding whitespace is trimmed
    [InlineData("D6", 1, 6, 0)] // case-insensitive
    public void Parse_ValidExpression_ReturnsDiceExpr(string input, int count, int sides, int constant)
    {
        Assert.Equal(new DiceExpr(count, sides, constant), DiceExpr.Parse(input));
    }

    // One row per distinct throw path in DiceExpr.Parse.
    [Theory]
    [InlineData("")] // null or whitespace
    [InlineData("d6+x")] // unparsable positive constant
    [InlineData("d6-x")] // unparsable negative constant
    [InlineData("6")] // no 'd' at all
    [InlineData("1d6d8")] // more than one 'd'
    [InlineData("0d6")] // non-positive dice count
    [InlineData("1d0")] // non-positive dice sides
    [InlineData("99999999d9")] // count above the sanity cap: rolling is O(count) inside a turn
    [InlineData("1d99999999")] // sides above the sanity cap
    public void Parse_MalformedExpression_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => DiceExpr.Parse(input));
    }
}
