using WretchedWhispers.Core.Campaigns;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class DifficultyPresetsTests
{
    // Easiest to hardest — the monotonicity invariants below are relative to this order.
    private static readonly Difficulty[] EasiestToHardest =
        [Difficulty.StoryMode, Difficulty.Grim, Difficulty.Doomed, Difficulty.Hardcore];

    [Fact]
    public void For_HarderDifficulty_DamageDiceNeverShrink_AndDawnDieNeverGrows()
    {
        var settings = EasiestToHardest.Select(DifficultyPresets.For).ToArray();

        // All presets use single dice, so comparing by sides compares the whole expression.
        Assert.All(settings, s =>
        {
            Assert.Equal(1, s.MinorDamage.Count);
            Assert.Equal(1, s.SeriousDamage.Count);
            Assert.Equal(1, s.DeadlyDamage.Count);
            Assert.Equal(1, s.DawnDice.Count);
        });

        for (var i = 1; i < settings.Length; i++)
        {
            var easier = settings[i - 1];
            var harder = settings[i];
            Assert.True(harder.MinorDamage.Sides >= easier.MinorDamage.Sides);
            Assert.True(harder.SeriousDamage.Sides >= easier.SeriousDamage.Sides);
            Assert.True(harder.DeadlyDamage.Sides >= easier.DeadlyDamage.Sides);
            // A smaller dawn die means more rolls of 1 — the world ends faster.
            Assert.True(harder.DawnDice.Sides <= easier.DawnDice.Sides);
        }
    }

    [Fact]
    public void For_AllDifficulties_HaveGmToneNote()
    {
        Assert.All(EasiestToHardest,
            level => Assert.False(string.IsNullOrWhiteSpace(DifficultyPresets.For(level).GmToneNote)));
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
