using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Powers;
using Xunit;
// Disambiguate from the sibling test namespace WretchedWhispers.Tests.Characters.Abilities.
using AbilitySet = WretchedWhispers.Core.Characters.Abilities.Abilities;

namespace WretchedWhispers.Tests.Characters.Powers;

public class PowerPoolTests : TestBase
{
    private static AbilitySet AbilitiesWithPresence(int presence) => new(
        new AbilityScore(0), new AbilityScore(presence), new AbilityScore(0), new AbilityScore(0));

    [Theory]
    [InlineData(-3, 0, 1)] // Negative presence should use 0, so 0 + (1+0) = 1
    [InlineData(-1, 1, 2)] // Negative presence should use 0, so 0 + (1+1) = 2
    [InlineData(0, 2, 3)] // Zero presence, so 0 + (1+2) = 3
    [InlineData(1, 0, 2)] // Positive presence, so 1 + (1+0) = 2
    [InlineData(3, 3, 7)] // Positive presence, so 3 + (1+3) = 7
    [InlineData(6, 1, 8)] // High presence, so 6 + (1+1) = 8
    public void Create_WithVariousPresenceValues_ShouldCalculateCorrectly(
        int presenceModifier, int diceRoll, int expectedMaxUses)
    {
        SetupDiceRoll(4, diceRoll);

        var powerPool = PowerPool.Create(AbilitiesWithPresence(presenceModifier), Dice);

        Assert.Equal(expectedMaxUses, powerPool.MaxUses);
        Assert.Equal(expectedMaxUses, powerPool.UsesRemaining);
    }

    [Theory]
    [InlineData(2, 1, 0, 2)] // same presence: 1 + d4(1) = 2
    [InlineData(1, 3, 3, 7)] // presence raised: 3 + d4(4) = 7
    public void ResetForNewDay_RerollsMaxUsesAndRefillsRemaining(
        int initialD4, int newPresence, int newD4, int expectedMax)
    {
        SetupDiceRoll(4, initialD4);
        var powerPool = PowerPool.Create(AbilitiesWithPresence(1), Dice);
        powerPool.TryConsumeOne();
        SetupDiceRoll(4, newD4);

        powerPool.ResetForNewDay(AbilitiesWithPresence(newPresence), Dice);

        Assert.Equal(expectedMax, powerPool.MaxUses);
        Assert.Equal(expectedMax, powerPool.UsesRemaining);
    }

    [Fact]
    public void TryConsumeOne_WithAvailableUses_ShouldConsumeAndReturnTrue()
    {
        SetupDiceRoll(4, 2); // max = 2 (Presence) + 3 (d4) = 5
        var powerPool = PowerPool.Create(AbilitiesWithPresence(2), Dice);

        var result = powerPool.TryConsumeOne();

        Assert.True(result);
        Assert.Equal(4, powerPool.UsesRemaining);
        Assert.Equal(5, powerPool.MaxUses); // MaxUses should remain unchanged
    }

    [Fact]
    public void TryConsumeOne_WithNoRemainingUses_ShouldReturnFalse()
    {
        SetupDiceRoll(4, 0); // max = 0 (Presence) + 1 (d4) = 1
        var powerPool = PowerPool.Create(AbilitiesWithPresence(0), Dice);
        Assert.True(powerPool.TryConsumeOne()); // consume the only use

        var result = powerPool.TryConsumeOne();

        Assert.False(result);
        Assert.Equal(0, powerPool.UsesRemaining);
    }
}
