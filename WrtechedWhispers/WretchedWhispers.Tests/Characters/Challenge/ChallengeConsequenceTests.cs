using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Challenge;

public sealed class ChallengeConsequenceTests : TestBase
{
    [Theory]
    [InlineData(ChallengeConsequence.Minor, 2)]
    [InlineData(ChallengeConsequence.Serious, 4)]
    [InlineData(ChallengeConsequence.Deadly, 6)]
    public void SufferConsequence_RollsSeverityDie_AppliesDamage(ChallengeConsequence severity, int expectedSides)
    {
        var character = TestCharacters.Create(Dice);
        var hpBefore = character.Hp.Current;
        // Character creation itself rolls dice (incl. a d4), which would collide with the severity-die
        // Verify below. Clear recorded invocations so we count only the consequence roll.
        MockRandomService.Invocations.Clear();
        SetupDiceRoll(expectedSides, 0); // severity die -> 1 damage

        // Grim: Minor d2 / Serious d4 / Deadly d6 — matches the InlineData sides.
        var settings = DifficultyPresets.For(Difficulty.Grim);
        var damage = character.SufferConsequence(severity, settings, Dice);

        Assert.Equal(1, damage);
        Assert.Equal(hpBefore - 1, character.Hp.Current);
        MockRandomService.Verify(r => r.GenerateRandomRoll(expectedSides), Times.Once);
    }

    [Fact]
    public void SufferConsequence_None_NoRoll_NoDamage()
    {
        var character = TestCharacters.Create(Dice);
        var hpBefore = character.Hp.Current;
        var settings = DifficultyPresets.For(Difficulty.Grim);

        var damage = character.SufferConsequence(ChallengeConsequence.None, settings, Dice);

        Assert.Equal(0, damage);
        Assert.Equal(hpBefore, character.Hp.Current);
    }

    [Fact]
    public async Task ChallengePlayer_FailureWithConsequence_AppliesDamage_SavesCharacter()
    {
        var character = TestCharacters.Create(Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRolls(0 /* d20 fumble -> fail */, 3 /* d4 consequence -> 4 damage */);

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            ChallengeConsequence.Serious);

        Assert.False(result.Outcome.IsSuccess);
        Assert.Equal(4, result.DamageTaken);
        repo.Verify(r => r.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengePlayer_Success_NoConsequence_NoSave()
    {
        var character = TestCharacters.Create(Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRoll(20, 19); // natural 20 -> success

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            ChallengeConsequence.Deadly);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(0, result.DamageTaken);
        repo.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChallengePlayer_Failure_NoConsequence_NoDamage_NoSave()
    {
        var character = TestCharacters.Create(Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRoll(20, 0); // d20 fumble -> fail

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            ChallengeConsequence.None);

        Assert.False(result.Outcome.IsSuccess);
        Assert.Equal(0, result.DamageTaken);
        repo.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
