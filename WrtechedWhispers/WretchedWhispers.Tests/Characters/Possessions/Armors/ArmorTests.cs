using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Armors;

public class ArmorTests
{
    [Theory]
    [InlineData(typeof(HeavyArmorTier))]
    [InlineData(typeof(MediumArmorTier))]
    [InlineData(typeof(LightArmorTier))]
    [InlineData(typeof(NoArmorTier))]
    public void Constructor_SetsTierCorrectly(Type tierType)
    {
        var tier = (ArmorTier)Activator.CreateInstance(tierType, true)!;
        var armor = new Armor(tier);
        Assert.Equal(tier, armor.Tier);
    }

    [Fact]
    public void Degrade_TransitionsCorrectly()
    {
        var armor = new Armor(HeavyArmorTier.Instance);
        armor.Degrade();
        Assert.IsType<MediumArmorTier>(armor.Tier);
        armor.Degrade();
        Assert.IsType<LightArmorTier>(armor.Tier);
        armor.Degrade();
        Assert.IsType<NoArmorTier>(armor.Tier);
        armor.Degrade();
        Assert.IsType<NoArmorTier>(armor.Tier); // No further degrade
    }

    [Fact]
    public void Repair_RestoresToOriginalTier()
    {
        var armor = new Armor(HeavyArmorTier.Instance);
        armor.Degrade(); // Medium
        armor.Degrade(); // Light
        armor.Repair();
        Assert.IsType<MediumArmorTier>(armor.Tier);
        armor.Repair();
        Assert.IsType<HeavyArmorTier>(armor.Tier);
    }

    [Fact]
    public void Repair_DoesNotUpgradePastOriginal()
    {
        var armor = new Armor(MediumArmorTier.Instance);
        armor.Degrade(); // Light
        armor.Repair();
        Assert.IsType<MediumArmorTier>(armor.Tier);
        armor.Repair();
        Assert.IsType<MediumArmorTier>(armor.Tier); // No further upgrade
    }

    [Fact]
    public void Repair_NoArmorEdgeCases()
    {
        var armor = new Armor(NoArmorTier.Instance);
        armor.Repair();
        Assert.IsType<NoArmorTier>(armor.Tier);
    }

    [Fact]
    public void Properties_ReflectCurrentTier()
    {
        var armor = new Armor(HeavyArmorTier.Instance);
        Assert.Equal(HeavyArmorTier.Instance.DefencePenalty, armor.DefencePenalty);
        Assert.Equal(HeavyArmorTier.Instance.AgilityPenalty, armor.AgilityPenalty);
        Assert.Equal(HeavyArmorTier.Instance.DamageReduction, armor.DamageReduction);
        armor.Degrade();
        Assert.Equal(MediumArmorTier.Instance.DefencePenalty, armor.DefencePenalty);
    }

    [Fact]
    public void Degrade_ThrowsOnUnknownTier()
    {
        var armor = new Armor(new DummyTier());
        Assert.Throws<ArgumentOutOfRangeException>(() => armor.Degrade());
    }

    [Fact]
    public void Repair_ThrowsOnUnknownTier()
    {
        var armor = new Armor(new DummyTier());
        Assert.Throws<ArgumentOutOfRangeException>(() => armor.Repair());
    }

    private class DummyTier : ArmorTier
    {
        public override int DefencePenalty => 0;
        public override int AgilityPenalty => 0;
        public override DiceExpr DamageReduction => new();
    }
}