using Moq;
using WretchedWhispers.Core.Campaigns.World;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns.World;

public class CalendarOfNechrubelTests : TestBase
{
    private static CalendarOfNechrubel CreateCalendar()
    {
        return new CalendarOfNechrubel();
    }

    [Fact]
    public void Constructor_InitializesEmptyState()
    {
        // Arrange & Act
        var calendar = CreateCalendar();

        // Assert
        Assert.Empty(calendar.Miseries);
        Assert.False(calendar.WorldEnded);
    }

    [Fact]
    public void DawnRoll_WithRollOf1_DoesNotTriggerWorldEnd()
    {
        // Arrange
        var calendar = CreateCalendar();
        SetupDiceRolls(0, 2, 3); // Dawn roll = 1, then misery rolls = 3+1=4, 4

        // Act
        calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        Assert.Single(calendar.Miseries);
        Assert.False(calendar.WorldEnded);
        Assert.Equal("34", calendar.Miseries.First().Code);
    }

    [Fact]
    public void DawnRoll_CallMultipleTimes_AccumulatesMiseries()
    {
        // Arrange
        var calendar = CreateCalendar();
        SetupDiceRolls(0, 0, 1, 0, 2, 3, 0, 4, 5); // Dawn=1, misery=1+1=2, Dawn=1, misery=3+1=4, etc.

        // Act
        calendar.DawnRoll(DiceExpr.D20, Dice);
        calendar.DawnRoll(DiceExpr.D20, Dice);
        calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        Assert.Equal(3, calendar.Miseries.Count);
        Assert.False(calendar.WorldEnded);
        var codes = calendar.Miseries.Select(m => m.Code).ToList();
        Assert.Contains("12", codes);
        Assert.Contains("34", codes);
        Assert.Contains("56", codes);
    }

    [Fact]
    public void DawnRoll_AvoidsDuplicateMiseries()
    {
        // Arrange
        var calendar = CreateCalendar();
        // Dawn=1, first misery attempt=34, second misery attempt=34 (duplicate), third attempt=56
        SetupDiceRolls(0, 0, 3, 0, 0, 3, 1, 4); // Dawn=2, misery=2+1=3, 4+1=5, etc.

        // Act
        calendar.DawnRoll(DiceExpr.D20, Dice);
        calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        Assert.Equal(2, calendar.Miseries.Count);
        var codes = calendar.Miseries.Select(m => m.Code).ToList();
        Assert.Contains("14", codes);
        Assert.Contains("25", codes);
    }

    [Fact]
    public void WorldEnded_WhenSevenMiseriesTriggered_ReturnsTrue()
    {
        // Arrange
        var calendar = CreateCalendar();
        var rolls = new List<int>();

        // Setup 7 dawn rolls (all return 1) and unique misery codes
        for (var i = 0; i < 7; i++)
        {
            rolls.Add(0); // Dawn roll
            rolls.Add(i + 1); // First die for misery
            rolls.Add(1); // Second die for misery (results in codes 11, 21, 31, etc.)
        }

        SetupDiceRolls(rolls.ToArray());

        // Act - Perform 7 dawn rolls
        for (var i = 0; i < 7; i++) calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        Assert.True(calendar.WorldEnded);
        Assert.Equal(7, calendar.Miseries.Count);
    }

    [Fact]
    public void DawnRoll_WhenWorldEnded_ThrowsInvalidOperationException()
    {
        // Arrange
        var calendar = CreateCalendar();
        var rolls = new List<int>();

        // Setup 7 dawn rolls to end the world
        for (var i = 0; i < 7; i++)
        {
            rolls.Add(0); // Dawn roll
            rolls.Add(i + 1); // First die for misery
            rolls.Add(1); // Second die for misery
        }

        SetupDiceRolls(rolls.ToArray());

        // Trigger world end
        for (var i = 0; i < 7; i++) calendar.DawnRoll(DiceExpr.D20, Dice);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => calendar.DawnRoll(DiceExpr.D20, Dice));
        Assert.Equal("The world has already ended.", exception.Message);
    }

    [Fact]
    public void DawnRoll_WithVariousDiceExpressions_WorksCorrectly()
    {
        // Arrange
        var calendar = CreateCalendar();
        SetupDiceRolls(0, 3, 4); // Dawn=1, misery=4+1=5, 5+1=6

        // Act
        calendar.DawnRoll(DiceExpr.D12, Dice); // Using different dice expression

        // Assert
        Assert.Single(calendar.Miseries);
        Assert.False(calendar.WorldEnded);
        Assert.Equal("45", calendar.Miseries.First().Code);
    }

    [Fact]
    public void DawnRoll_TooManyAttemptsToPickMisery_ThrowsInvalidOperationException()
    {
        // Arrange
        var calendar = new CalendarOfNechrubel();
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(0); // Always return 1 for both dice

        // Act
        calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        var ex = Assert.Throws<InvalidOperationException>(() => calendar.DawnRoll(DiceExpr.D20, Dice));
        Assert.Equal("Too many attempts to pick a misery, something is wrong.", ex.Message);
    }
}