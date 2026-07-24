using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Challenge;

/// <summary>Spending an omen lowers a test's DR by 4 — validated and applied atomically inside
/// the domain, spent before the roll.</summary>
public sealed class ChallengeOmenTests : TestBase
{
    [Fact]
    public void Challenge_WithOmen_LowersDrBy4_AndSpendsOmen()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRoll(20, 11); // d20 roll of 12: fails DR 14, passes DR 10

        var outcome = character.Challenge(new Dr(14), AbilityKind.Presence, Dice, spendOmenToLowerDr: true);

        Assert.Equal(10, outcome.EffectiveDr);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(0, character.Omens.Count);
    }

    [Fact]
    public void Challenge_WithOmen_NoOmensRemaining_Throws()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 0);

        Assert.Throws<ArgumentException>(() =>
            character.Challenge(new Dr(14), AbilityKind.Presence, Dice, spendOmenToLowerDr: true));
    }

    [Fact]
    public async Task ChallengePlayer_OmenSpentOnSuccess_StillSavesCharacter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRoll(20, 11); // roll 12 vs effective DR 8 -> success

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Presence, DifficultyPresets.For(Difficulty.Grim),
            spendOmenToLowerDr: true);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(0, character.Omens.Count);
        repo.Verify(r => r.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }
}
