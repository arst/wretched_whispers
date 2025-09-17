using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors;

public sealed class Armor(ArmorTier tier)
{
    private ArmorTier OriginalTier { get; } = tier;

    public ArmorTier Tier { get; private set; } = tier;

    public int DefencePenalty => Tier.DefencePenalty;

    public int AgilityPenalty => Tier.AgilityPenalty;

    public DiceExpr DamageReduction => Tier.DamageReduction;

    public void Degrade()
    {
        Tier = Tier switch
        {
            HeavyArmorTier => MediumArmorTier.Instance,
            MediumArmorTier => LightArmorTier.Instance,
            LightArmorTier => NoArmorTier.Instance,
            NoArmorTier => Tier,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public void Repair()
    {
        Tier = Tier switch
        {
            HeavyArmorTier => Tier,
            MediumArmorTier => OriginalTier is HeavyArmorTier ? HeavyArmorTier.Instance : Tier,
            LightArmorTier => OriginalTier is MediumArmorTier or HeavyArmorTier ? MediumArmorTier.Instance : Tier,
            NoArmorTier => OriginalTier is LightArmorTier or MediumArmorTier or HeavyArmorTier
                ? OriginalTier
                : NoArmorTier.Instance,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}