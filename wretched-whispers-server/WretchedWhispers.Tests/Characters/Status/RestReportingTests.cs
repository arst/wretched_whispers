using WretchedWhispers.Core.Characters.Status;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Status;

/// <summary>A rest says what it did. The result used to carry omens only: a narrator kept telling a
/// healed character their old hit points for ten turns, and an infected one could be hurt, broken or
/// killed by a rest with nothing in the result to tell it from.</summary>
public class RestReportingTests : TestBase
{
    /// <summary>Every d4 rolls 3 (daily powers; Broken table: survives, injured; hit points on coming
    /// round) and every d6 rolls 6 (rest healing, infection damage, the injury table: a lost eye).</summary>
    private void EveryD4Rolls3AndEveryD6Rolls6()
    {
        SetupDiceRoll(4, 2);
        SetupDiceRoll(6, 5);
    }

    [Fact]
    public void Rest_WhenHurt_ReportsTheHpHealed()
    {
        EveryD4Rolls3AndEveryD6Rolls6();
        var hero = TestCharacters.Create(Dice, maxHp: 20);
        hero.Infect();
        hero.Rest(8, Dice); // the infection takes 6
        hero.Rest(8, Dice); // and 6 more
        hero.CureInfection();

        var rest = hero.Rest(4, Dice);

        Assert.Equal(3, rest.HpHealed);
        Assert.Equal(11, hero.Hp.Current);
        Assert.Equal(0, rest.DamageTaken);
    }

    [Fact]
    public void Rest_AtFullHealth_ReportsNothingHealed()
    {
        EveryD4Rolls3AndEveryD6Rolls6();
        var hero = TestCharacters.Create(Dice, maxHp: 20);

        Assert.Equal(0, hero.Rest(8, Dice).HpHealed);
    }

    [Fact]
    public void Rest_WhileInfected_ReportsTheDamage()
    {
        EveryD4Rolls3AndEveryD6Rolls6();
        var hero = TestCharacters.Create(Dice, maxHp: 20);
        hero.Infect();

        var rest = hero.Rest(8, Dice);

        Assert.Equal(6, rest.DamageTaken);
        Assert.Equal(0, rest.HpHealed);
        Assert.Null(rest.NewInjury);
        Assert.False(rest.Died);
        Assert.Equal(14, hero.Hp.Current);
    }

    [Fact]
    public void Rest_WhileInfected_ThatBreaks_NamesTheInjury()
    {
        EveryD4Rolls3AndEveryD6Rolls6();
        var hero = TestCharacters.Create(Dice, maxHp: 1);
        hero.Infect();

        var rest = hero.Rest(8, Dice);

        Assert.Equal(InjuryKind.LostEye, rest.NewInjury);
        Assert.False(rest.Died);
    }

    [Fact]
    public void Rest_WhileInfected_ThatKills_ReportsTheDeath()
    {
        SetupDiceRoll(4, 0); // Broken table: 1, dead
        var hero = TestCharacters.Create(Dice, maxHp: 1);
        hero.Infect();

        var rest = hero.Rest(8, Dice);

        Assert.True(rest.Died);
        Assert.Null(rest.NewInjury);
        Assert.True(hero.IsDead);
    }

    // A turn can chain past a death ("I jump, then sleep it off"); the tools for it are already built.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rest_WhenDead_ChangesNothingAndSaysDead(bool stillInfected)
    {
        SetupDiceRoll(4, 0); // Broken table: 1, dead
        var hero = TestCharacters.Create(Dice, maxHp: 1);
        hero.Infect();
        hero.Rest(8, Dice);
        if (!stillInfected) hero.CureInfection();
        SetupDiceRoll(4, 2); // were the table rolled again, the corpse would come round
        SetupDiceRoll(6, 5);

        var rest = hero.Rest(8, Dice);

        Assert.Equal(new RestOutcome(0, 0, 0, null, true), rest);
        Assert.Equal(0, hero.Hp.Current);
        Assert.True(hero.IsDead);
    }
}
