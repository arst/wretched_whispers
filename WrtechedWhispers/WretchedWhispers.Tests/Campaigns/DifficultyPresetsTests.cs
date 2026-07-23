using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class DifficultyPresetsTests
{
    [Theory]
    [InlineData(Difficulty.StoryMode, 8, 2, 2, 4, 8)]
    [InlineData(Difficulty.Grim, 0, 2, 4, 6, 6)]
    [InlineData(Difficulty.Doomed, 0, 2, 6, 10, 6)]
    [InlineData(Difficulty.Hardcore, 0, 4, 8, 12, 4)]
    public void For_returns_expected_settings(
        Difficulty level, int hpBonus, int minor, int serious, int deadly, int dawn)
    {
        var s = DifficultyPresets.For(level);

        Assert.Equal(hpBonus, s.StartingHpBonus);
        Assert.Equal(DiceExpr.D(1, minor), s.MinorDamage);
        Assert.Equal(DiceExpr.D(1, serious), s.SeriousDamage);
        Assert.Equal(DiceExpr.D(1, deadly), s.DeadlyDamage);
        Assert.Equal(DiceExpr.D(1, dawn), s.DawnDice);
        Assert.False(string.IsNullOrWhiteSpace(s.GmToneNote));
    }

    [Fact]
    public void Grim_matches_current_main_balance()
    {
        var s = DifficultyPresets.For(Difficulty.Grim);
        Assert.Equal(0, s.StartingHpBonus);
        Assert.Equal(DiceExpr.D(1, 4), s.SeriousDamage);
        Assert.Equal(DiceExpr.D(1, 6), s.DeadlyDamage);
    }

    [Fact]
    public void AbilityLossOnGettingBetter_DisabledOnlyInStoryMode()
    {
        Assert.False(DifficultyPresets.For(Difficulty.StoryMode).AbilityLossOnGettingBetter);
        Assert.True(DifficultyPresets.For(Difficulty.Grim).AbilityLossOnGettingBetter);
        Assert.True(DifficultyPresets.For(Difficulty.Doomed).AbilityLossOnGettingBetter);
        Assert.True(DifficultyPresets.For(Difficulty.Hardcore).AbilityLossOnGettingBetter);
    }
}
