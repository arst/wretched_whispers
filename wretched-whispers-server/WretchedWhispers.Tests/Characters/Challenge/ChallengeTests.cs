using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Challenge;

public sealed class ChallengeTests : TestBase
{
    private readonly Mock<ICharactersRepository> _repo = new();

    private CharacterService NewService(Character character)
    {
        _repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        return new CharacterService(_repo.Object, Dice);
    }

    [Fact]
    public void Challenge_ReportsRollModifierAndEffectiveDr()
    {
        var character = TestCharacters.Create(Dice, toughness: 2);
        // Dice.Roll(D20) = 1 + GenerateRandomRoll(20), so 13 here yields a roll of 14.
        SetupDiceRoll(20, 13);

        var outcome = character.Challenge(new Dr(12), AbilityKind.Toughness, Dice);

        Assert.Equal(14, outcome.Roll);
        Assert.Equal(2, outcome.Modifier);
        Assert.Equal(12, outcome.EffectiveDr);
        Assert.True(outcome.IsSuccess);
    }

    // Spending an omen lowers a test's DR by 4 — validated and applied atomically inside
    // the domain, spent before the roll.

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

        Assert.Throws<InvalidOperationException>(() =>
            character.Challenge(new Dr(14), AbilityKind.Presence, Dice, spendOmenToLowerDr: true));
    }

    [Fact]
    public async Task ChallengePlayer_OmenSpentOnSuccess_StillSavesCharacter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        var service = NewService(character);
        SetupDiceRoll(20, 11); // roll 12 vs effective DR 8 -> success

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Presence, DifficultyPresets.For(Difficulty.Grim),
            spendOmenToLowerDr: true);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(0, character.Omens.Count);
        _repo.Verify(r => r.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

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
        var service = NewService(character);
        SetupDiceRolls(0 /* d20 fumble -> fail */, 3 /* d4 consequence -> 4 damage */);

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            ChallengeConsequence.Serious);

        Assert.False(result.Outcome.IsSuccess);
        Assert.Equal(4, result.DamageTaken);
        _repo.Verify(r => r.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengePlayer_ConsequenceZeroesHp_SurvivesBroken_ReportsAliveAndZeroHp()
    {
        // The playtest bug: a DR-12 test fails, fall damage zeroes a 3-HP wretch, the Broken table
        // rolls an injury (not death) — the character is ALIVE at 0 HP. ChallengeResult must report
        // IsDead=false and CurrentHp=0 so the narrator can't fabricate a death.
        var character = TestCharacters.Create(Dice, maxHp: 3);
        var service = NewService(character);
        // d20 -> 7 (fail vs 12); Deadly d6 -> 3 damage (3 HP -> 0); Broken d4 -> 3 (injury branch, survives);
        // injury d6 -> 3 (SmashedFace).
        SetupDiceRolls(6, 2, 2, 2);

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            ChallengeConsequence.Deadly);

        Assert.False(result.Outcome.IsSuccess);
        Assert.Equal(3, result.DamageTaken);
        Assert.False(result.IsDead);
        Assert.Equal(0, result.CurrentHp);
        Assert.True(character.HasSmashedFace); // survived Broken with an injury
    }

    [Theory]
    [InlineData(19, ChallengeConsequence.Deadly, true)] // success -> configured consequence never applies
    [InlineData(0, ChallengeConsequence.None, false)] // failure -> no consequence configured
    public async Task ChallengePlayer_NoConsequenceApplies_NoDamage_NoSave(
        int d20ZeroBased, ChallengeConsequence consequence, bool expectSuccess)
    {
        var character = TestCharacters.Create(Dice);
        var service = NewService(character);
        SetupDiceRoll(20, d20ZeroBased);

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
            consequence);

        Assert.Equal(expectSuccess, result.Outcome.IsSuccess);
        Assert.Equal(0, result.DamageTaken);
        _repo.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
