using WretchedWhispers.Engine.GameTools;
using Xunit;

namespace WretchedWhispers.Tests.GameTools;

public sealed class DiceToolsTests : TestBase
{
    private readonly DiceTools _tools;

    public DiceToolsTests()
    {
        _tools = new DiceTools(Dice);
    }

    [Theory]
    [InlineData("d6", 6, 3, 4)] // 0-based roll 3 -> 4
    [InlineData("1d8+2", 8, 4, 7)] // 0-based roll 4 -> 5, plus constant 2 = 7
    public void Roll_ReturnsFormulaAndResult(string formula, int sides, int zeroBasedRoll, int expected)
    {
        SetupDiceRoll(sides, zeroBasedRoll);

        var result = _tools.Roll(formula);

        Assert.Equal(formula, result.Formula);
        Assert.Equal(expected, result.Result);
    }

    [Fact]
    public void Roll_RejectsMalformedDiceExpression()
    {
        Assert.Throws<ArgumentException>(() => _tools.Roll("not-a-die"));
    }
}
