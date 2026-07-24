using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors;

public sealed class Armor
{
    public Armor(ArmorTier tier) : this(tier, tier)
    {
    }

    [JsonConstructor]
    private Armor(ArmorTier tier, ArmorTier originalTier)
    {
        Tier = tier;
        OriginalTier = originalTier;
    }

    [JsonInclude] public ArmorTier OriginalTier { get; private set; }

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

    public void Repair()
    {
        Tier = Tier switch
        {
            ArmorTier.Heavy => Tier,
            ArmorTier.Medium => OriginalTier is ArmorTier.Heavy ? ArmorTier.Heavy : Tier,
            ArmorTier.Light => OriginalTier is ArmorTier.Medium or ArmorTier.Heavy ? ArmorTier.Medium : Tier,
            ArmorTier.None => OriginalTier is ArmorTier.Light or ArmorTier.Medium or ArmorTier.Heavy
                ? OriginalTier
                : ArmorTier.None,
            _ => throw new ArgumentOutOfRangeException(nameof(Tier))
        };
    }
}
