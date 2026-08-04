using System.Linq;
using Moq;
using Xunit;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;

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

    /// <summary>A character who has earned the ritual: created, then given one full night's rest.
    /// startingOmens 1 avoids the omen-refill d2; the second SetupDiceRolls call in each test
    /// replaces this queue with the ritual's own rolls.</summary>
    private WretchedWhispers.Core.Characters.Character CreateRested(
        int strength = 0, int agility = 0, int presence = 0, int toughness = 0, int maxHp = 20)
    {
        var character = TestCharacters.Create(Dice, agility: agility, presence: presence,
            strength: strength, toughness: toughness, startingOmens: 1, maxHp: maxHp);
        SetupDiceRolls(0 /* heal d6 */);
        character.Rest(8, Dice);
        return character;
    }

    [Fact]
    public void GetBetter_WithoutRest_Throws()
    {
        var character = TestCharacters.Create(Dice);

        Assert.Throws<InvalidOperationException>(() => character.GetBetter(Dice, allowAbilityLoss: true));
    }

    [Fact]
    public void GetBetter_HpRollMeetsMax_IncreasesMaxOnly()
    {
        var character = CreateRested(); // maxHp 20, current 20, all abilities 0
        // 6d10: six 4s = 24 >= 20 -> passes; HP gain d6 -> 3; then 4 ability d6 -> all 1 (>= 0 -> +1).
        SetupDiceRolls(3, 3, 3, 3, 3, 3, /* hp d6 */ 2, /* abilities */ 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(24, outcome.HpRoll);
        Assert.Equal(3, outcome.HpGained);
        Assert.Equal(23, outcome.NewMaxHp);
        Assert.Equal(23, character.Hp.Max);
        Assert.Equal(20, character.Hp.Current); // RAW: only the maximum grows
    }

    [Fact]
    public void GetBetter_HpRollExactlyEqualsMax_StillIncreases()
    {
        // Meet-or-beat: 6d10 of six 4s = 24 EXACTLY equals max 24 -> the check passes.
        var character = CreateRested(maxHp: 24);
        SetupDiceRolls(3, 3, 3, 3, 3, 3, /* hp d6 */ 2, /* abilities */ 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(24, outcome.HpRoll);
        Assert.Equal(3, outcome.HpGained);
        Assert.Equal(27, character.Hp.Max);
    }

    [Fact]
    public void GetBetter_HpRollBelowMax_NoHpChange()
    {
        var character = CreateRested(); // maxHp 20
        // 6d10: six 1s = 6 < 20 -> no HP d6 is rolled; next four rolls are the ability d6s.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* abilities */ 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(6, outcome.HpRoll);
        Assert.Equal(0, outcome.HpGained);
        Assert.Equal(20, character.Hp.Max);
    }

    [Fact]
    public void GetBetter_AbilityRollMeetsScore_ImprovesByOne()
    {
        var character = CreateRested(); // all abilities 0
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // d6 = 1 >= 0 -> +1 each

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.All(outcome.Abilities, a => Assert.Equal(1, a.Delta));
        Assert.Equal(1, character.Abilities.Strength.Modifier);
        Assert.Equal(1, character.Abilities.Agility.Modifier);
        Assert.Equal(1, character.Abilities.Presence.Modifier);
        Assert.Equal(1, character.Abilities.Toughness.Modifier);
        // Strength +1 recalculates carrying capacity: 2 * (1 + 8).
        Assert.Equal(18, character.Inventory.MaxCapacity);
    }

    [Fact]
    public void GetBetter_AbilityRollBelowScore_LossAllowed_Degrades()
    {
        var character = CreateRested(strength: 3);
        // HP check fails (six 1s). Strength rolls first: d6 = 1 < 3 -> -1. Others (0): +1.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* str */ 0, /* agi, pre, tou */ 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(2, character.Abilities.Strength.Modifier);
        var strength = outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength);
        Assert.Equal(-1, strength.Delta);
        Assert.Equal(2, strength.NewScore);
        Assert.Equal(20, character.Inventory.MaxCapacity); // 2 * (2 + 8)
    }

    [Fact]
    public void GetBetter_AbilityRollBelowScore_LossDisabled_Unchanged()
    {
        var character = CreateRested(strength: 3); // StoryMode behaviour
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: false);

        Assert.Equal(3, character.Abilities.Strength.Modifier);
        Assert.Equal(0, outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength).Delta);
    }

    [Fact]
    public void GetBetter_AbilityAtCap_RollMeetsScore_Unchanged()
    {
        var character = CreateRested(strength: 6);
        // Strength d6 = 6 >= 6 -> would improve, but +6 is the cap.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* str */ 5, /* others */ 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(6, character.Abilities.Strength.Modifier);
        Assert.Equal(0, outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength).Delta);
    }

    [Fact]
    public void GetBetter_ConsumesTheRestGate()
    {
        var character = CreateRested();
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.False(character.CanGetBetter);
        Assert.Throws<InvalidOperationException>(() => character.GetBetter(Dice, allowAbilityLoss: true));
    }

    [Fact]
    public async Task Service_GetBetter_RollsAndSavesOnce()
    {
        var character = CreateRested();
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // 6d10 fail, four ability d6s

        var outcome = await service.GetBetter(character.Id, allowAbilityLoss: true);

        Assert.Equal(6, outcome.HpRoll);
        Assert.False(character.CanGetBetter);
        repo.Verify(r => r.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Service_GetBetter_UnknownCharacter_Throws()
    {
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WretchedWhispers.Core.Characters.Character?)null);
        var service = new CharacterService(repo.Object, Dice);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetBetter(Guid.NewGuid(), allowAbilityLoss: true));
    }
}
