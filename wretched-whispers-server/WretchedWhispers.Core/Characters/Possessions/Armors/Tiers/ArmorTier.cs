using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

public enum ArmorTier
{
    None,
    Light,
    Medium,
    Heavy
}

public static class ArmorTierExtensions
{
    public static int DefencePenalty(this ArmorTier tier) => tier switch
    {
        ArmorTier.Heavy => 2,
        _ => 0
    };

    public static int AgilityPenalty(this ArmorTier tier) => tier switch
    {
        ArmorTier.Medium => 2,
        ArmorTier.Heavy => 4,
        _ => 0
    };

    public static DiceExpr DamageReduction(this ArmorTier tier) => tier switch
    {
        ArmorTier.Light => DiceExpr.D2,
        ArmorTier.Medium => DiceExpr.D4,
        ArmorTier.Heavy => DiceExpr.D6,
        _ => DiceExpr.Zero
    };

    public static int RollDamageReduction(this ArmorTier tier, Dice dice)
    {
        var reduction = tier.DamageReduction();
        return reduction.Sides == 0 ? 0 : dice.Roll(reduction);
    }

    public static string DisplayName(this ArmorTier tier) => tier switch
    {
        ArmorTier.None => "None",
        ArmorTier.Light => "Light Armor",
        ArmorTier.Medium => "Medium Armor",
        ArmorTier.Heavy => "Heavy Armor",
        _ => "Unknown"
    };

    public static string Token(this ArmorTier tier) => tier.ToString().ToLowerInvariant();
}
