using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Status;

/// <summary>Whatever breaks the character says which injury the Broken table gave. Results used to carry
/// damage and death only, so 8 of 18 injuries in the first simulated run went untold: the narrator had
/// nothing to tell them from.</summary>
public class InjuryReportingTests : TestBase
{
    private static readonly DifficultySettings Grim = DifficultyPresets.For(Difficulty.Grim);

    /// <summary>Every d4 rolls 3 (daily powers; any d4 of damage; Broken table: survives, injured) and
    /// every d6 rolls 6 (the injury table: a lost eye).</summary>
    private void BreakWithALostEye()
    {
        SetupDiceRoll(4, 2);
        SetupDiceRoll(6, 5);
    }

    [Fact]
    public void SufferConsequence_DamageThatBreaks_ReportsTheInjury()
    {
        BreakWithALostEye();
        var hero = TestCharacters.Create(Dice, maxHp: 1);

        var outcome = hero.SufferConsequence(ChallengeConsequence.Serious, Grim, Dice);

        Assert.Equal(InjuryKind.LostEye, outcome.Injury);
        Assert.True(hero.HasLostEye);
        Assert.False(hero.IsDead);
    }

    [Fact]
    public void SufferConsequence_Death_ReportsNoInjury()
    {
        SetupDiceRoll(4, 0); // Broken table: 1, dead
        var hero = TestCharacters.Create(Dice, maxHp: 1);

        var outcome = hero.SufferConsequence(ChallengeConsequence.Serious, Grim, Dice);

        Assert.Null(outcome.Injury);
        Assert.True(hero.IsDead);
    }

    [Fact]
    public void SufferConsequence_NoBreak_ReportsNoInjury()
    {
        BreakWithALostEye();
        var hero = TestCharacters.Create(Dice, maxHp: 20);

        var outcome = hero.SufferConsequence(ChallengeConsequence.Serious, Grim, Dice);

        Assert.Equal(3, outcome.Damage);
        Assert.Null(outcome.Injury);
    }

    [Fact]
    public void Defend_HitThatBreaks_ReportsTheInjury()
    {
        BreakWithALostEye();
        SetupDiceRoll(20, 4); // dodge: 5 against DR 12
        var hero = TestCharacters.Create(Dice, maxHp: 1);

        var defence = hero.Defend(DiceExpr.D4, Dice);

        Assert.Equal(InjuryKind.LostEye, defence.Injury);
    }

    [Fact]
    public void Cast_FizzleThatBreaks_ReportsTheInjury()
    {
        BreakWithALostEye();
        SetupDiceRoll(20, 1); // Presence test: 2 against DR 12
        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech");
        var hero = TestCharacters.Create(Dice, maxHp: 1, scrolls: scroll);

        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.False(outcome.Succeeded);
        Assert.Equal(InjuryKind.LostEye, outcome.NewInjury);
    }

    [Fact]
    public async Task ChallengePlayer_DamageThatBreaks_ReportsTheInjury()
    {
        BreakWithALostEye();
        SetupDiceRoll(20, 4); // the test: 5 against DR 12
        var hero = TestCharacters.Create(Dice, maxHp: 1);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await new CharacterService(repo.Object, Dice).ChallengePlayer(
            hero.Id, new Dr(12), AbilityKind.Strength, Grim, ChallengeConsequence.Serious);

        Assert.Equal(InjuryKind.LostEye, result.NewInjury);
        Assert.False(result.IsDead);
    }

    [Theory]
    [InlineData(InjuryKind.LostEye, "lost eye")]
    [InlineData(InjuryKind.StabbedLung, "stabbed lung")]
    [InlineData(InjuryKind.BrokenHand, "broken hand")]
    [InlineData(InjuryKind.CrushedFoot, "crushed foot")]
    [InlineData(InjuryKind.SeveredArm, "severed arm")]
    [InlineData(InjuryKind.SmashedFace, "smashed face")]
    public void EveryInjury_HasTheNameGameStateUses(InjuryKind injury, string name)
    {
        Assert.Equal(name, injury.DisplayName());
    }
}
