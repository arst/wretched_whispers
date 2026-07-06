using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Encounters;

public sealed class ResolveRoundTests : TestBase
{
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();

    private EncounterService CreateService() =>
        new(Dice, _charactersRepo.Object, _encountersRepo.Object);

    private (Encounter encounter, Character character) Arrange(int adversaries = 1, int adversaryHp = 4)
    {
        var encounter = Encounter.Create("Fight", "desc", EncounterType.Hostile, Dice);
        for (var i = 0; i < adversaries; i++)
            encounter.AddAdversary(new Adversary(
                $"Ghoul {i + 1}", new HitPoints(adversaryHp, adversaryHp), new Armor(ArmorTier.None), 7,
                new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();

        var character = TestCharacters.Create(Dice);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        return (encounter, character);
    }

    [Fact]
    public async Task Attack_ResolvesPlayerAttack_ThenEveryLivingAdversaryRetaliates()
    {
        var (encounter, character) = Arrange(adversaries: 2, adversaryHp: 100);
        // d20 attack (hit but no kill vs 100hp), weapon dmg, then per-adversary defence rolls.
        // Feed generous rolls; assertions below don't depend on exact damage numbers.
        SetupDiceRolls(14, 2, 9, 1, 9, 1);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Ghoul 1");

        Assert.NotNull(outcome.PlayerAttack);
        Assert.Equal("Ghoul 1", outcome.PlayerAttackTarget);
        Assert.Equal(2, outcome.Retaliations.Count);
        Assert.False(outcome.EncounterEnded);
        _encountersRepo.Verify(r => r.Save(encounter), Times.Once);
        _charactersRepo.Verify(r => r.Save(character), Times.Once);
    }

    [Fact]
    public async Task Attack_KillsLastAdversary_AutoEnds_NoRetaliation()
    {
        var (encounter, character) = Arrange(adversaries: 1, adversaryHp: 1);
        // nat-20 hit, d6 damage well past 1 HP, then ProcessPlayerAttackOutcome's morale check
        // (single-adversary group below 30% HP) rolls 2d6 even for a dead target: 6+6=12 >= morale 7.
        SetupDiceRolls(19, 5, 5, 5);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Ghoul 1");

        Assert.True(outcome.EncounterEnded);
        Assert.Equal(EncounterEndReason.AllDefeated, outcome.EndReason);
        Assert.Empty(outcome.Retaliations);
        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public async Task Flee_Success_EndsEncounter_NoRetaliation()
    {
        var (encounter, character) = Arrange();
        SetupDiceRoll(20, 19); // guaranteed flee success (nat 20)

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Flee, null);

        Assert.NotNull(outcome.FleeAttempt);
        Assert.True(outcome.FleeAttempt.IsSuccess);
        Assert.True(outcome.EncounterEnded);
        Assert.Equal(EncounterEndReason.PlayerFled, outcome.EndReason);
        Assert.Empty(outcome.Retaliations);
        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public async Task Flee_Failure_WastesRound_RetaliationHappens()
    {
        var (encounter, character) = Arrange();
        SetupDiceRolls(0, 1, 0); // nat-1 flee fail, then adversary attack rolls

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Flee, null);

        Assert.False(outcome.FleeAttempt.IsSuccess);
        Assert.Single(outcome.Retaliations);
        Assert.False(encounter.IsEnded);
    }

    [Fact]
    public async Task Other_RunsRetaliationOnly()
    {
        var (encounter, character) = Arrange();
        SetupDiceRolls(1, 0); // adversary attack rolls only

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Other, null);

        Assert.Null(outcome.PlayerAttack);
        Assert.Null(outcome.FleeAttempt);
        Assert.Single(outcome.Retaliations);
    }

    [Fact]
    public async Task Attack_UnknownTargetName_FallsBackToFirstLiving()
    {
        var (encounter, character) = Arrange(adversaries: 1, adversaryHp: 100);
        SetupDiceRolls(14, 2, 9, 1);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Nonexistent");

        Assert.Equal("Ghoul 1", outcome.PlayerAttackTarget);
    }

    [Fact]
    public async Task NotStartedEncounter_Throws()
    {
        var encounter = Encounter.Create("Idle", "desc", EncounterType.Hostile, Dice);
        var character = TestCharacters.Create(Dice);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().ResolveRound(encounter.Id, character.Id, PlayerRoundAction.Attack, null));
    }

    [Fact]
    public async Task Flee_WithArmorAgilityPenalty_ComputesEffectiveDrExactly()
    {
        var abilities = new Abilities(
            agility: new AbilityScore(0), presence: new AbilityScore(0),
            strength: new AbilityScore(0), toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 10, FoodDays: 3, Container: "backpack (7 items)",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Sword),
            Armor: new Armor(ArmorTier.Heavy),
            Shield: null, Scrolls: []);
        var character = Character.Create(Guid.NewGuid(), "ArmoredHero", 20, abilities, equipment, Dice);

        var encounter = Encounter.Create("Fight", "desc", EncounterType.Hostile, Dice);
        encounter.AddAdversary(new Adversary(
            "Ghoul 1", new HitPoints(4, 4), new Armor(ArmorTier.None), 7,
            new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        SetupDiceRoll(20, 9); // arbitrary flee roll; only EffectiveDr is asserted here

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Flee, null);

        Assert.NotNull(outcome.FleeAttempt);
        Assert.Equal(12 + ArmorTier.Heavy.AgilityPenalty(), outcome.FleeAttempt.EffectiveDr);
    }

    [Fact]
    public async Task Retaliation_StopsWhenPlayerDies_ReportsPlayerDead_LeavesEncounterOpen()
    {
        // 1-HP character so a single d4 claw kills; two adversaries so the break is observable.
        var abilities = new Abilities(
            agility: new AbilityScore(0), presence: new AbilityScore(0),
            strength: new AbilityScore(0), toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 10, FoodDays: 3, Container: "backpack (7 items)",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Sword),
            Armor: new Armor(ArmorTier.None),
            Shield: null, Scrolls: []);
        var character = Character.Create(Guid.NewGuid(), "FragileHero", 1, abilities, equipment, Dice);

        var encounter = Encounter.Create("Fight", "desc", EncounterType.Hostile, Dice);
        for (var i = 0; i < 2; i++)
            encounter.AddAdversary(new Adversary(
                $"Ghoul {i + 1}", new HitPoints(4, 4), new Armor(ArmorTier.None), 7,
                new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        // Ghoul 1's retaliation: d20 defence 5 (fail vs DR 12), d4 claw damage 1 (1 HP -> 0),
        // injury-table d4 = 1 -> InjuryKind.None -> death. Ghoul 2 never rolls: the loop breaks.
        SetupDiceRolls(4, 0, 0);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Other, null);

        Assert.True(character.IsDead);
        Assert.Single(outcome.Retaliations); // fewer than the 2 living adversaries
        Assert.Equal(EncounterEndReason.PlayerDead, outcome.EndReason);
        Assert.True(outcome.EncounterEnded);
        Assert.False(encounter.IsEnded); // stage derivation handles player death, not the encounter
    }

    [Fact]
    public async Task EndedEncounter_Throws()
    {
        var (encounter, character) = Arrange();
        encounter.EndByPlayerEscape();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().ResolveRound(encounter.Id, character.Id, PlayerRoundAction.Attack, null));
    }
}
