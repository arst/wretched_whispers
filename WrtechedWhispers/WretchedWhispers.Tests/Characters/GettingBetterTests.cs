using Moq;
using Xunit;

namespace WretchedWhispers.Tests.Characters;

/// <summary>MORK BORG "Getting Better": post-adventure ritual, gated by a full night's rest.
/// Dice mock is 0-based (SetupDiceRolls value 3 = die shows 4).</summary>
public sealed class GettingBetterTests : TestBase
{
    [Fact]
    public void NewCharacter_CannotGetBetter()
    {
        var character = TestCharacters.Create(Dice);

        Assert.False(character.CanGetBetter);
    }

    [Fact]
    public void FullNightRest_EnablesGettingBetter()
    {
        // startingOmens 1 so the full rest doesn't also roll the omen-refill d2.
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d6 */);

        character.Rest(8, Dice);

        Assert.True(character.CanGetBetter);
    }

    [Fact]
    public void PartialRest_DoesNotEnableGettingBetter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d4 */);

        character.Rest(4, Dice);

        Assert.False(character.CanGetBetter);
    }

    [Fact]
    public void InfectedFullRest_DoesNotEnableGettingBetter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        character.Infect();
        SetupDiceRolls(0 /* infection damage d6 */);

        character.Rest(8, Dice);

        Assert.False(character.CanGetBetter);
    }
}
