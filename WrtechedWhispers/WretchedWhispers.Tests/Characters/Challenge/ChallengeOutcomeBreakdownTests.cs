using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Challenge;

public sealed class ChallengeOutcomeBreakdownTests : TestBase
{
    [Fact]
    public void Challenge_ReportsRollModifierAndEffectiveDr()
    {
        var character = TestCharacters.Create(Dice, toughness: 2);
        // Dice.Roll(D20) = 1 + GenerateRandomRoll(20), so 13 here yields a roll of 14.
        SetupDiceRoll(20, 13);

        var outcome = character.Challenge(new Dr(12), AbilityKind.Toughness, Dice);

        Assert.Equal(14, outcome.Roll);
        Assert.Equal(character.Abilities.Toughness.Modifier, outcome.Modifier);
        Assert.Equal(12, outcome.EffectiveDr);
        Assert.Equal(14 + character.Abilities.Toughness.Modifier >= 12, outcome.IsSuccess);
    }
}
