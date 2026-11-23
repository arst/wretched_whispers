using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Encounters;

/// <summary>A scroll that harms others harms the adversaries of the fight in progress, in the game
/// state and not only in the telling.</summary>
public sealed class CastScrollTests : TestBase
{
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();

    private EncounterService CreateService() =>
        new(Dice, _charactersRepo.Object, _encountersRepo.Object);

    /// <summary>Script the dice before calling: building the caster rolls a d4 for daily powers.</summary>
    private (Encounter encounter, Character caster, Scroll scroll) Arrange(
        string power, int adversaries, int adversaryHp, int casterHp = 20)
    {
        var encounter = Encounter.Create("Fight", "desc", EncounterType.Hostile, Dice);
        for (var i = 0; i < adversaries; i++)
            encounter.AddAdversary(new Adversary(
                $"Ghoul {i + 1}", new HitPoints(adversaryHp, adversaryHp), new Armor(ArmorTier.None), 7,
                new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();

        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, power);
        var caster = TestCharacters.Create(Dice, maxHp: casterHp, scrolls: scroll);
        _encountersRepo.Setup(r => r.Get(encounter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(caster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caster);
        return (encounter, caster, scroll);
    }

    [Fact]
    public async Task CastScroll_DamagingPower_HurtsTheAdversariesOfTheActiveFight()
    {
        // d4 daily powers; d20 = 15 to cast; d2 = 2 creatures; d8 = 3, then d8 = 6.
        SetupDiceRolls(0, 14, 1, 2, 5);
        var (encounter, caster, scroll) = Arrange("Palms Open the Southern Gate", adversaries: 2, adversaryHp: 4);

        var result = await CreateService().CastScroll(caster.Id, scroll.Id, encounter.Id);

        Assert.True(result.Outcome.Succeeded);
        Assert.Equal(
            [new ScrollHit("Ghoul 1", 3, false), new ScrollHit("Ghoul 2", 6, true)],
            result.Hits);
        Assert.Equal(1, encounter.Adversaries[0].Hp.Current);
        Assert.True(encounter.Adversaries[1].IsDead);
        _encountersRepo.Verify(r => r.Save(encounter, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CastScroll_SplitPower_SharesItsTotalAmongEveryoneStanding()
    {
        // d4 daily powers; d20 = 15 to cast; 4d10 = 1 + 1 + 1 + 2 = 5.
        SetupDiceRolls(0, 14, 0, 0, 0, 1);
        var (encounter, caster, scroll) = Arrange("Death", adversaries: 2, adversaryHp: 100);

        var result = await CreateService().CastScroll(caster.Id, scroll.Id, encounter.Id);

        Assert.Equal(
            [new ScrollHit("Ghoul 1", 3, false), new ScrollHit("Ghoul 2", 2, false)],
            result.Hits);
    }

    [Fact]
    public async Task CastScroll_DamagingPower_WithNoFight_HitsNothing()
    {
        SetupDiceRolls(0, 14);
        var (_, caster, scroll) = Arrange("Palms Open the Southern Gate", adversaries: 1, adversaryHp: 4);

        var result = await CreateService().CastScroll(caster.Id, scroll.Id, encounterId: null);

        Assert.True(result.Outcome.Succeeded);
        Assert.Empty(result.Hits);
        _charactersRepo.Verify(r => r.Save(caster, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CastScroll_Fizzle_HitsNothing()
    {
        // d4 daily powers; d20 = 2 fails; d2 = 1 HP lost.
        SetupDiceRolls(0, 1, 0);
        var (encounter, caster, scroll) = Arrange("Death", adversaries: 1, adversaryHp: 4);

        var result = await CreateService().CastScroll(caster.Id, scroll.Id, encounter.Id);

        Assert.False(result.Outcome.Succeeded);
        Assert.Empty(result.Hits);
        Assert.Equal(4, encounter.Adversaries[0].Hp.Current);
    }

    [Fact]
    public async Task CastScroll_FizzleThatKillsTheCaster_SaysSo()
    {
        SetupDiceRoll(4, 0); // daily powers 1; Broken table 1: dead
        SetupDiceRoll(20, 1); // Presence test: 2 against DR 12
        SetupDiceRoll(2, 0); // the fizzle costs 1
        var (encounter, caster, scroll) = Arrange("Death", adversaries: 1, adversaryHp: 4, casterHp: 1);

        var result = await CreateService().CastScroll(caster.Id, scroll.Id, encounter.Id);

        Assert.False(result.Outcome.Succeeded);
        Assert.True(result.CasterDead);
        Assert.Equal(0, result.CasterHp);
    }

    // If the round that should follow a killing cast is skipped, the next attack finds nobody to hit.
    // That used to throw and leave the fight open with no one in it.
    [Fact]
    public async Task Attack_WithNobodyLeftStanding_EndsTheFight()
    {
        SetupDiceRolls(0, 14, 9, 9, 9, 9);
        var (encounter, caster, scroll) = Arrange("Death", adversaries: 1, adversaryHp: 4);
        var service = CreateService();
        await service.CastScroll(caster.Id, scroll.Id, encounter.Id);

        var round = await service.ResolveRound(encounter.Id, caster.Id, PlayerRoundAction.Attack, "Ghoul 1");

        Assert.Null(round.PlayerAttack);
        Assert.Equal(EncounterEndReason.AllDefeated, round.EndReason);
        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public async Task CastScroll_KillingEveryAdversary_LeavesTheRoundToEndTheFight()
    {
        // d4 daily powers; d20 = 15 to cast; 4d10 = 40.
        SetupDiceRolls(0, 14, 9, 9, 9, 9);
        var (encounter, caster, scroll) = Arrange("Death", adversaries: 1, adversaryHp: 4);
        var service = CreateService();

        await service.CastScroll(caster.Id, scroll.Id, encounter.Id);

        Assert.True(encounter.Adversaries[0].IsDead);
        Assert.False(encounter.IsEnded);

        var round = await service.ResolveRound(encounter.Id, caster.Id, PlayerRoundAction.Other);

        Assert.Equal(EncounterEndReason.AllDefeated, round.EndReason);
        Assert.True(encounter.IsEnded);
    }
}
