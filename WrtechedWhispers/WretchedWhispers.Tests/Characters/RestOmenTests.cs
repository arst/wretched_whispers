using Moq;
using Xunit;

namespace WretchedWhispers.Tests.Characters;

/// <summary>MORK BORG omen refresh: omens refill (d2) only after a full night's rest (8+ hours)
/// once all are spent.</summary>
public sealed class RestOmenTests : TestBase
{
    [Fact]
    public void FullNightRest_AllOmensSpent_RefillsD2()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 0);
        SetupDiceRolls(3 /* heal d6 -> 4 */, 1 /* omen d2 -> 2 */);

        var refreshed = character.Rest(8, Dice);

        Assert.Equal(2, refreshed);
        Assert.Equal(2, character.Omens.Count);
    }

    [Fact]
    public void PartialRest_NoRefill()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 0);
        SetupDiceRolls(2 /* heal d4 -> 3 */);

        var refreshed = character.Rest(4, Dice);

        Assert.Equal(0, refreshed);
        Assert.Equal(0, character.Omens.Count);
        MockRandomService.Verify(x => x.GenerateRandomRoll(2), Times.Never);
    }

    [Fact]
    public void FullNightRest_OmensRemaining_NoTopUp()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(3 /* heal d6 */);

        var refreshed = character.Rest(8, Dice);

        Assert.Equal(0, refreshed);
        Assert.Equal(1, character.Omens.Count);
    }

    [Fact]
    public void InfectedRest_DamagesInsteadOfHealing_NoRefill()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 0);
        character.Infect();
        SetupDiceRolls(2 /* infection d6 -> 3 damage */);

        var refreshed = character.Rest(8, Dice);

        Assert.Equal(0, refreshed);
        Assert.Equal(0, character.Omens.Count);
        Assert.Equal(17, character.Hp.Current);
    }
}
