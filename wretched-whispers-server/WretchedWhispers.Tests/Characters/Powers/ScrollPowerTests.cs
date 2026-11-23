using System.Text.Json;
using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Powers;

/// <summary>A scroll is named for one of the book's powers, and a successful cast applies what that
/// power does to the caster. Before this every scroll was called "random" and did nothing.</summary>
public class ScrollPowerTests : TestBase
{
    private static Scroll ScrollOf(string power, ScrollSchool school = ScrollSchool.Sacred) =>
        new(Guid.NewGuid(), school, power);

    [Fact]
    public void BothSchools_HaveTenPowersEach()
    {
        Assert.Equal(10, ScrollPowers.All.Count(p => p.School == ScrollSchool.Unclean));
        Assert.Equal(10, ScrollPowers.All.Count(p => p.School == ScrollSchool.Sacred));
        Assert.All(ScrollPowers.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Effect)));
    }

    [Fact]
    public void Roll_PicksFromTheSchoolsOwnTable()
    {
        SetupDiceRolls(9); // d10 = 10

        var power = ScrollPowers.Roll(ScrollSchool.Unclean, Dice);

        Assert.Equal("Death", power.Name);
        Assert.Equal(ScrollSchool.Unclean, power.School);
    }

    [Fact]
    public void Cast_HealingPower_HealsTheCaster()
    {
        // d4 daily powers; three wounds of 2; d20 = 15 against DR 12; d10 = 4 healed.
        SetupDiceRolls(0, 1, 1, 1, 14, 3);
        var scroll = ScrollOf("Grace of a Dead Saint");
        var hero = TestCharacters.Create(Dice, maxHp: 20, scrolls: scroll);
        var grim = DifficultyPresets.For(Difficulty.Grim);
        for (var i = 0; i < 3; i++) hero.SufferConsequence(ChallengeConsequence.Minor, grim, Dice);
        var woundedAt = hero.Hp.Current;

        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.True(outcome.Succeeded);
        Assert.Equal(4, outcome.HpHealed);
        Assert.Equal(woundedAt + 4, hero.Hp.Current);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Effect));
    }

    [Fact]
    public void Cast_Blessing_LowersTheNextTestOnce()
    {
        // d4 daily powers; d20 = 15 to cast; d6 = 4 blessing; then two tests rolling 8.
        SetupDiceRolls(0, 14, 3, 7, 7);
        var scroll = ScrollOf("Grace for a Sinner");
        var hero = TestCharacters.Create(Dice, scrolls: scroll);

        var outcome = hero.Cast(scroll.Id, Dice);
        var blessed = hero.Challenge(new Dr(12), AbilityKind.Strength, Dice);
        var unblessed = hero.Challenge(new Dr(12), AbilityKind.Strength, Dice);

        Assert.Equal(4, outcome.NextTestBonus);
        Assert.Equal(8, blessed.EffectiveDr);
        Assert.True(blessed.IsSuccess);
        Assert.Equal(12, unblessed.EffectiveDr);
        Assert.Equal(0, hero.NextTestBonus);
    }

    [Fact]
    public void Cast_NarrativePower_ReturnsItsEffectAndChangesNoNumbers()
    {
        SetupDiceRolls(0, 14);
        var scroll = ScrollOf("Bestial Speech");
        var hero = TestCharacters.Create(Dice, scrolls: scroll);

        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.True(outcome.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Effect));
        Assert.Equal(0, outcome.HpHealed);
        Assert.Equal(0, outcome.NextTestBonus);
    }

    // The repository hands back a fresh character on every read, so a blessing that is only cleared in
    // memory comes back with the next load and eases every test after it.
    [Fact]
    public async Task Blessing_IsSpentInWhatIsSaved_EvenWhenTheTestPasses()
    {
        // d4 daily powers; d20 = 15 to cast; d6 = 4 blessing; then two tests rolling 20.
        SetupDiceRolls(0, 14, 3, 19, 19);
        var scroll = ScrollOf("Grace for a Sinner");
        var hero = TestCharacters.Create(Dice, scrolls: scroll);
        hero.Cast(scroll.Id, Dice);
        var json = AggregateJsonOptions.Create();
        var stored = JsonSerializer.Serialize(hero, json);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => JsonSerializer.Deserialize<Character>(stored, json));
        repo.Setup(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()))
            .Callback((Character saved, CancellationToken _) => stored = JsonSerializer.Serialize(saved, json));
        var service = new CharacterService(repo.Object, Dice);
        var grim = DifficultyPresets.For(Difficulty.Grim);

        var blessed = await service.ChallengePlayer(hero.Id, new Dr(12), AbilityKind.Strength, grim);
        var unblessed = await service.ChallengePlayer(hero.Id, new Dr(12), AbilityKind.Strength, grim);

        Assert.Equal(8, blessed.Outcome.EffectiveDr);
        Assert.Equal(12, unblessed.Outcome.EffectiveDr);
    }

    [Fact]
    public void Cast_ByADeadCharacter_DoesNothing()
    {
        SetupDiceRoll(4, 0); // daily powers 1; Broken table 1: dead
        var scroll = ScrollOf("Grace of a Dead Saint");
        var hero = TestCharacters.Create(Dice, maxHp: 1, scrolls: scroll);
        hero.SufferConsequence(ChallengeConsequence.Serious, DifficultyPresets.For(Difficulty.Grim), Dice);
        Assert.True(hero.IsDead);
        var castingsLeft = hero.Powers.UsesRemaining;

        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, outcome.HpHealed);
        Assert.Equal(0, hero.Hp.Current);
        Assert.Equal(castingsLeft, hero.Powers.UsesRemaining);
    }

    // A character saved before scrolls had powers still carries one called "random".
    [Fact]
    public void Cast_ScrollNamedRandom_StillSucceedsWithNoEffect()
    {
        SetupDiceRolls(0, 14);
        var scroll = ScrollOf("random", ScrollSchool.Unclean);
        var hero = TestCharacters.Create(Dice, scrolls: scroll);

        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Effect);
    }
}
