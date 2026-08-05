using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Encounters;

/// <summary>
/// The GM invents adversary stats for the fiction and cannot know what the character can actually do.
/// <see cref="Adversary.Create"/> is where that gap is closed, so these pin the scaling itself rather
/// than the specific preset numbers (those live in DifficultyPresetsTests).
/// </summary>
public sealed class AdversaryScalingTests
{
    private static readonly AttackProfile Claws = new("claws", DiceExpr.D4);

    private static Adversary Create(Difficulty difficulty, int hitPoints = 10,
        ArmorTier armorTier = ArmorTier.Heavy) =>
        Adversary.Create("Ghoul", hitPoints, armorTier, morale: 7, Claws, DifficultyPresets.For(difficulty));

    [Theory]
    [InlineData(Difficulty.StoryMode, 10, 5)]
    [InlineData(Difficulty.Grim, 10, 8)] // 7.5 rounds away from zero
    [InlineData(Difficulty.Doomed, 10, 10)]
    [InlineData(Difficulty.Hardcore, 10, 10)]
    public void HitPoints_ScaleWithDifficulty(Difficulty difficulty, int requested, int expected)
    {
        var adversary = Create(difficulty, requested);

        Assert.Equal(expected, adversary.Hp.Max);
        Assert.Equal(expected, adversary.Hp.Current);
    }

    [Theory]
    [InlineData(Difficulty.StoryMode, ArmorTier.None)]
    [InlineData(Difficulty.Grim, ArmorTier.Light)]
    [InlineData(Difficulty.Doomed, ArmorTier.Heavy)]
    [InlineData(Difficulty.Hardcore, ArmorTier.Heavy)]
    public void Armor_IsCappedByDifficulty(Difficulty difficulty, ArmorTier expected)
    {
        var adversary = Create(difficulty, armorTier: ArmorTier.Heavy);

        Assert.Equal(expected, adversary.Armor.Tier);
    }

    [Fact]
    public void Armor_BelowTheCap_IsLeftAlone()
    {
        // The cap is a ceiling, not a target: a deliberately unarmored thing stays unarmored.
        var adversary = Create(Difficulty.Doomed, armorTier: ArmorTier.None);

        Assert.Equal(ArmorTier.None, adversary.Armor.Tier);
    }

    [Fact]
    public void HitPoints_NeverScaleBelowOne()
    {
        // Halving a 1 HP vermin must still leave something to kill, not a corpse that is already dead.
        var adversary = Create(Difficulty.StoryMode, hitPoints: 1);

        Assert.Equal(1, adversary.Hp.Max);
        Assert.False(adversary.IsDead);
    }

    [Fact]
    public void NameMoraleAndAttack_AreNeverTouched()
    {
        var adversary = Create(Difficulty.StoryMode);

        Assert.Equal("Ghoul", adversary.Name);
        Assert.Equal(7, adversary.Morale);
        Assert.Equal(Claws, adversary.Attack);
    }
}
