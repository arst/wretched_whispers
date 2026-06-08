using System.Text.Json;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Api.GameTools;
using Xunit;

namespace WretchedWhispers.Tests;

public sealed class DiceToolsTests : TestBase
{
    private readonly DiceTools _plugin;

    public DiceToolsTests()
    {
        _plugin = new DiceTools(Dice);
    }

    [Fact]
    public void Roll_D6_ReturnsDiceRollResultWithFormulaAndResultInRange()
    {
        // Arrange: d6 with roll of 3 (0-based -> result = 4)
        SetupDiceRoll(6, 3);

        // Act
        var result = _plugin.Roll("d6");

        // Assert
        Assert.IsType<DiceRollResult>(result);
        Assert.Equal("d6", result.Formula);
        Assert.InRange(result.Result, 1, 6);
        Assert.Equal(4, result.Result);
    }

    [Fact]
    public void Roll_1d8Plus2_ReturnsDiceRollResultWithFormulaAndResultInRange()
    {
        // Arrange: d8 with roll of 4 (0-based -> 5, plus constant 2 = 7)
        SetupDiceRoll(8, 4);

        // Act
        var result = _plugin.Roll("1d8+2");

        // Assert
        Assert.IsType<DiceRollResult>(result);
        Assert.Equal("1d8+2", result.Formula);
        Assert.InRange(result.Result, 3, 10);
        Assert.Equal(7, result.Result);
    }

    [Fact]
    public void DiceRollResult_SerializesToCamelCaseJson()
    {
        // Arrange
        var rollResult = new DiceRollResult("d6", 4);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(rollResult, options);

        // Assert
        Assert.Contains("\"formula\":\"d6\"", json);
        Assert.Contains("\"result\":4", json);
    }
}
