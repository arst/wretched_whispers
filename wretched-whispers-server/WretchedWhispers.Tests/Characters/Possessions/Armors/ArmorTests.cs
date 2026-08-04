using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Armors;

public class ArmorTests
{
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
}
