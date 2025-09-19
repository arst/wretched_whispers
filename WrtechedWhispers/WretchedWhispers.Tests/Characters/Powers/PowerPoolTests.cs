using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Powers;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Powers;

public class PowerPoolTests : TestBase
{
    [Fact]
    public void Create_WithPositivePresence_ShouldInitializeCorrectly()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(2), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(2); // D4 roll returns 1+2=3

        // Act
        var powerPool = PowerPool.Create(abilities);

        // Assert
        Assert.Equal(5, powerPool.MaxUses); // 2 (Presence) + 3 (D4) = 5
        Assert.Equal(5, powerPool.UsesRemaining);
    }

    [Fact]
    public void Create_WithNegativePresence_ShouldUseZeroAsMinimum()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(-2), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(3); // D4 roll returns 1+3=4

        // Act
        var powerPool = PowerPool.Create(abilities);

        // Assert
        Assert.Equal(4, powerPool.MaxUses); // Max(0, -2) + 4 = 0 + 4 = 4
        Assert.Equal(4, powerPool.UsesRemaining);
    }

    [Fact]
    public void Create_WithZeroPresence_ShouldOnlyUseDiceRoll()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(0), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(1); // D4 roll returns 1+1=2

        // Act
        var powerPool = PowerPool.Create(abilities);

        // Assert
        Assert.Equal(2, powerPool.MaxUses); // 0 (Presence) + 2 (D4) = 2
        Assert.Equal(2, powerPool.UsesRemaining);
    }

    [Fact]
    public void ResetForNewDay_ShouldRecalculateMaxUsesAndResetRemaining()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(1), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(2); // Initial D4 roll returns 1+2=3
        var powerPool = PowerPool.Create(abilities);
        
        // Consume some uses
        powerPool.TryConsumeOne();
        powerPool.TryConsumeOne();
        Assert.Equal(2, powerPool.UsesRemaining);

        // Change the dice roll for reset
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(0); // New D4 roll returns 1+0=1

        // Act
        powerPool.ResetForNewDay(abilities);

        // Assert
        Assert.Equal(2, powerPool.MaxUses); // 1 (Presence) + 1 (D4) = 2
        Assert.Equal(2, powerPool.UsesRemaining); // Should be reset to MaxUses
    }

    [Fact]
    public void ResetForNewDay_WithChangedAbilities_ShouldUseNewPresenceModifier()
    {
        // Arrange
        var originalAbilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(1), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(1); // Initial D4 roll returns 1+1=2
        var powerPool = PowerPool.Create(originalAbilities);
        Assert.Equal(3, powerPool.MaxUses); // 1 + 2 = 3

        var newAbilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(3), // Presence increased
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(3); // New D4 roll returns 1+3=4

        // Act
        powerPool.ResetForNewDay(newAbilities);

        // Assert
        Assert.Equal(7, powerPool.MaxUses); // 3 (new Presence) + 4 (D4) = 7
        Assert.Equal(7, powerPool.UsesRemaining);
    }

    [Fact]
    public void TryConsumeOne_WithAvailableUses_ShouldConsumeAndReturnTrue()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(2), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(2); // D4 roll returns 1+2=3
        var powerPool = PowerPool.Create(abilities);
        Assert.Equal(5, powerPool.UsesRemaining);

        // Act
        var result = powerPool.TryConsumeOne();

        // Assert
        Assert.True(result);
        Assert.Equal(4, powerPool.UsesRemaining);
        Assert.Equal(5, powerPool.MaxUses); // MaxUses should remain unchanged
    }

    [Fact]
    public void TryConsumeOne_WithNoRemainingUses_ShouldReturnFalse()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(0), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(0); // D4 roll returns 1+0=1
        var powerPool = PowerPool.Create(abilities);
        Assert.Equal(1, powerPool.UsesRemaining);

        // Consume the only use
        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(0, powerPool.UsesRemaining);

        // Act - try to consume when empty
        var result = powerPool.TryConsumeOne();

        // Assert
        Assert.False(result);
        Assert.Equal(0, powerPool.UsesRemaining); // Should remain 0
    }

    [Fact]
    public void TryConsumeOne_MultipleConsumptions_ShouldDecrementCorrectly()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(1), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(3); // D4 roll returns 1+3=4
        var powerPool = PowerPool.Create(abilities);
        Assert.Equal(5, powerPool.UsesRemaining); // 1 + 4 = 5

        // Act & Assert - consume uses one by one
        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(4, powerPool.UsesRemaining);

        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(3, powerPool.UsesRemaining);

        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(2, powerPool.UsesRemaining);

        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(1, powerPool.UsesRemaining);

        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(0, powerPool.UsesRemaining);

        // Should fail when empty
        Assert.False(powerPool.TryConsumeOne());
        Assert.Equal(0, powerPool.UsesRemaining);
    }

    [Theory]
    [InlineData(-3, 0, 1)] // Negative presence should use 0, so 0 + (1+0) = 1
    [InlineData(-1, 1, 2)] // Negative presence should use 0, so 0 + (1+1) = 2
    [InlineData(0, 2, 3)]  // Zero presence, so 0 + (1+2) = 3
    [InlineData(1, 0, 2)]  // Positive presence, so 1 + (1+0) = 2
    [InlineData(3, 3, 7)]  // Positive presence, so 3 + (1+3) = 7
    [InlineData(6, 1, 8)]  // High presence, so 6 + (1+1) = 8
    public void Create_WithVariousPresenceValues_ShouldCalculateCorrectly(
        int presenceModifier, int diceRoll, int expectedMaxUses)
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(presenceModifier), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(diceRoll);

        // Act
        var powerPool = PowerPool.Create(abilities);

        // Assert
        Assert.Equal(expectedMaxUses, powerPool.MaxUses);
        Assert.Equal(expectedMaxUses, powerPool.UsesRemaining);
    }

    [Fact]
    public void PowerPool_CompleteWorkflow_ShouldMaintainConsistency()
    {
        // Arrange
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(2), // Presence
            new AbilityScore(0), // Strength
            new AbilityScore(0)  // Toughness
        );
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(2); // Initial D4 roll returns 1+2=3
        
        // Act & Assert - Create pool
        var powerPool = PowerPool.Create(abilities);
        Assert.Equal(5, powerPool.MaxUses);
        Assert.Equal(5, powerPool.UsesRemaining);

        // Use some power
        Assert.True(powerPool.TryConsumeOne());
        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(3, powerPool.UsesRemaining);

        // Reset for new day with different dice roll
        MockRandomService.Setup(x => x.GenerateRandomRoll(4)).Returns(0); // New D4 roll returns 1+0=1
        powerPool.ResetForNewDay(abilities);
        Assert.Equal(3, powerPool.MaxUses); // 2 + 1 = 3
        Assert.Equal(3, powerPool.UsesRemaining);

        // Use all remaining power
        Assert.True(powerPool.TryConsumeOne());
        Assert.True(powerPool.TryConsumeOne());
        Assert.True(powerPool.TryConsumeOne());
        Assert.Equal(0, powerPool.UsesRemaining);

        // Should fail to consume more
        Assert.False(powerPool.TryConsumeOne());
        Assert.Equal(0, powerPool.UsesRemaining);
    }
}
