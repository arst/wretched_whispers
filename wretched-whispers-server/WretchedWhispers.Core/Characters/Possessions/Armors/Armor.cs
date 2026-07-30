using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors;

public sealed class Armor
{
    // Old blobs carry a stale "originalTier" key from a repair mechanic that never got a caller;
    // deserialization ignores it.
    [JsonConstructor]
    public Armor(ArmorTier tier)
    {
        Tier = tier;
    }

    [JsonInclude] public ArmorTier Tier { get; private set; }

    public int DefencePenalty => Tier.DefencePenalty();

    public int AgilityPenalty => Tier.AgilityPenalty();

    public DiceExpr DamageReduction => Tier.DamageReduction();

    public void Degrade()
    {
        Tier = Tier switch
        {
            ArmorTier.Heavy => ArmorTier.Medium,
            ArmorTier.Medium => ArmorTier.Light,
            ArmorTier.Light => ArmorTier.None,
            ArmorTier.None => Tier,
            _ => throw new ArgumentOutOfRangeException(nameof(Tier))
        };
    }
}
