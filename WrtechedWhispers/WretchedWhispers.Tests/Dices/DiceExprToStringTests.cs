using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Dices;

/// <summary>
/// DiceExpr.ToString renders standard dice notation rather than the record's default dump
/// ("DiceExpr { Count = 1, Sides = 4, Constant = 0 }"), which used to leak into adversary DTOs.
/// </summary>
public class DiceExprToStringTests
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
        Assert.Equal("d4", DiceExpr.D4.ToString());
        Assert.Equal("d20", DiceExpr.D20.ToString());
    }

    [Fact]
    public void RoundTripsThroughParse()
    {
        Assert.Equal("2d10+3", DiceExpr.Parse("2d10+3").ToString());
        Assert.Equal("d6-2", DiceExpr.Parse("1d6-2").ToString());
    }
}
