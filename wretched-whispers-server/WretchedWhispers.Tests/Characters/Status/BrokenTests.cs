using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Challenge;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Status;

/// <summary>MORK BORG: a character dropped to 0 who survives the Broken table comes round with d4 hit
/// points. They used to stay at 0, and in the first simulated run players spent 39 turns there, each new
/// hit a fresh roll on the table.</summary>
public class BrokenTests : TestBase
{
    private static readonly DifficultySettings Grim = DifficultyPresets.For(Difficulty.Grim);

    [Fact]
    public void SurvivingBroken_ComesRoundWithTheD4Rolled()
    {
        SetupDiceRoll(4, 2); // daily powers; Broken table 3: survives; comes round with 3
        SetupDiceRoll(6, 5); // deadly damage 6; injury table 6: a lost eye
        var hero = TestCharacters.Create(Dice, maxHp: 6);

        var outcome = hero.SufferConsequence(ChallengeConsequence.Deadly, Grim, Dice);

        Assert.Equal(6, outcome.Damage);
        Assert.False(hero.IsDead);
        Assert.True(hero.HasLostEye);
        Assert.Equal(3, hero.Hp.Current);
    }

    [Fact]
    public void SurvivingBroken_NeverComesRoundAboveTheMaximum()
    {
        SetupDiceRoll(4, 2);
        SetupDiceRoll(6, 5);
        var hero = TestCharacters.Create(Dice, maxHp: 1);

        hero.SufferConsequence(ChallengeConsequence.Deadly, Grim, Dice);

        Assert.Equal(1, hero.Hp.Current);
    }

    [Fact]
    public void DyingOnTheBrokenTable_StaysAtZeroAndDead()
    {
        SetupDiceRoll(4, 0); // Broken table 1: dead
        SetupDiceRoll(6, 5);
        var hero = TestCharacters.Create(Dice, maxHp: 6);

        hero.SufferConsequence(ChallengeConsequence.Deadly, Grim, Dice);

        Assert.True(hero.IsDead);
        Assert.Equal(0, hero.Hp.Current);
    }

    [Fact]
    public void BrokenASecondTime_RollsTheTableAgain()
    {
        SetupDiceRoll(4, 2);
        SetupDiceRoll(6, 5);
        var hero = TestCharacters.Create(Dice, maxHp: 6);
        hero.SufferConsequence(ChallengeConsequence.Deadly, Grim, Dice);
        SetupDiceRoll(4, 0); // this time the table says dead

        hero.SufferConsequence(ChallengeConsequence.Deadly, Grim, Dice);

        Assert.True(hero.IsDead);
    }
}
