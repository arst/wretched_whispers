using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Armors;

public class ArmorTests
{
    [Theory]
    [InlineData(ArmorTier.Heavy)]
    [InlineData(ArmorTier.Medium)]
    [InlineData(ArmorTier.Light)]
    [InlineData(ArmorTier.None)]
    public void Constructor_SetsTierCorrectly(ArmorTier tier)
    {
        var armor = new Armor(tier);
        Assert.Equal(tier, armor.Tier);
    }

    [Fact]
    public void Degrade_TransitionsCorrectly()
    {
        var armor = new Armor(ArmorTier.Heavy);
        armor.Degrade();
        Assert.Equal(ArmorTier.Medium, armor.Tier);
        armor.Degrade();
        Assert.Equal(ArmorTier.Light, armor.Tier);
        armor.Degrade();
        Assert.Equal(ArmorTier.None, armor.Tier);
        armor.Degrade();
        Assert.Equal(ArmorTier.None, armor.Tier); // No further degrade
    }

    [Fact]
    public void Repair_RestoresToOriginalTier()
    {
        var armor = new Armor(ArmorTier.Heavy);
        armor.Degrade(); // Medium
        armor.Degrade(); // Light
        armor.Repair();
        Assert.Equal(ArmorTier.Medium, armor.Tier);
        armor.Repair();
        Assert.Equal(ArmorTier.Heavy, armor.Tier);
    }

    [Fact]
    public void Repair_DoesNotUpgradePastOriginal()
    {
        var armor = new Armor(ArmorTier.Medium);
        armor.Degrade(); // Light
        armor.Repair();
        Assert.Equal(ArmorTier.Medium, armor.Tier);
        armor.Repair();
        Assert.Equal(ArmorTier.Medium, armor.Tier); // No further upgrade
    }

    [Fact]
    public void Repair_NoArmorEdgeCases()
    {
        var armor = new Armor(ArmorTier.None);
        armor.Repair();
        Assert.Equal(ArmorTier.None, armor.Tier);
    }

    [Fact]
    public void Properties_ReflectCurrentTier()
    {
        var armor = new Armor(ArmorTier.Heavy);
        Assert.Equal(ArmorTier.Heavy.DefencePenalty(), armor.DefencePenalty);
        Assert.Equal(ArmorTier.Heavy.AgilityPenalty(), armor.AgilityPenalty);
        Assert.Equal(ArmorTier.Heavy.DamageReduction(), armor.DamageReduction);
        armor.Degrade();
        Assert.Equal(ArmorTier.Medium.DefencePenalty(), armor.DefencePenalty);
    }
}
