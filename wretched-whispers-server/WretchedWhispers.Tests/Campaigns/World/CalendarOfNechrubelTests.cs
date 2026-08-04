using Moq;
using WretchedWhispers.Core.Campaigns.World;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns.World;

public class CalendarOfNechrubelTests : TestBase
{
    [Fact]
    public void DawnRoll_RollOf1_TriggersMiseryWithPsalm_WorldIntact()
    {
        // Arrange
        var calendar = new CalendarOfNechrubel();
        SetupDiceRolls(0, 2, 3); // dawn d20 = 1 (triggers), misery d6s = 3 and 4 -> code "34"

        // Act
        var triggered = calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        // Regression: a triggered misery must carry a psalm. The AdvanceTime outcome maps m.Psalm,
        // so an empty psalm surfaced to the player/model as the bug "Miseries": [""].
        Assert.NotNull(triggered);
        Assert.False(string.IsNullOrWhiteSpace(triggered.Psalm));
        Assert.Contains("First Misery", triggered.Psalm); // first misery of the descent
        Assert.Equal("34", Assert.Single(calendar.Miseries).Code);
        Assert.False(calendar.WorldEnded);
    }

    [Fact]
    public void DawnRoll_WhenNoMiseryTriggered_ReturnsNull()
    {
        var calendar = new CalendarOfNechrubel();
        SetupDiceRolls(4); // Dawn roll = 5 on the d20 — not a 1, no misery

        var triggered = calendar.DawnRoll(DiceExpr.D20, Dice);

        Assert.Null(triggered);
        Assert.Empty(calendar.Miseries);
    }

    [Fact]
    public void DawnRoll_CallMultipleTimes_AccumulatesMiseries()
    {
        // Arrange: three dawns each rolling 1; misery d6 pairs give codes "12", "34", "56".
        var calendar = new CalendarOfNechrubel();
        SetupDiceRolls(0, 0, 1, 0, 2, 3, 0, 4, 5);

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
        var calendar = new CalendarOfNechrubel();
        // First dawn: roll of 1 triggers, misery d6s 1 and 4 -> code "14".
        // Second dawn: roll of 1 triggers; the first pick rolls d6s 1 and 4 again -> "14" is a
        // duplicate and is rejected, so the loop retries with d6s 2 and 5 -> code "25" accepted.
        SetupDiceRolls(0, 0, 3, 0, 0, 3, 1, 4);

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
        var calendar = new CalendarOfNechrubel();

        TriggerAllMiseries(calendar);

        Assert.True(calendar.WorldEnded);
        Assert.Equal(7, calendar.Miseries.Count);
    }

    [Fact]
    public void DawnRoll_WhenWorldEnded_ThrowsInvalidOperationException()
    {
        var calendar = new CalendarOfNechrubel();
        TriggerAllMiseries(calendar);

        Assert.Throws<InvalidOperationException>(() => calendar.DawnRoll(DiceExpr.D20, Dice));
    }

    [Fact]
    public void DawnRoll_TooManyAttemptsToPickMisery_ThrowsInvalidOperationException()
    {
        // Arrange: every roll is a 1, so the second dawn keeps picking the already-taken code "11".
        var calendar = new CalendarOfNechrubel();
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(0);

        // Act
        calendar.DawnRoll(DiceExpr.D20, Dice);

        // Assert
        Assert.Throws<InvalidOperationException>(() => calendar.DawnRoll(DiceExpr.D20, Dice));
    }

    /// <summary>Runs seven dawns that each roll a 1 and trigger a unique misery, ending the world.</summary>
    private void TriggerAllMiseries(CalendarOfNechrubel calendar)
    {
        var rolls = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            rolls.Add(0); // dawn roll -> 1 (triggers)
            rolls.Add(i); // misery die 1 -> i+1: codes "12".."72", all unique
            rolls.Add(1); // misery die 2 -> 2
        }

        SetupDiceRolls(rolls.ToArray());

        for (var i = 0; i < 7; i++) calendar.DawnRoll(DiceExpr.D20, Dice);
    }
}
