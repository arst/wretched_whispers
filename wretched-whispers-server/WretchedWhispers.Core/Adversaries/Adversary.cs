using System.Text.Json.Serialization;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

namespace WretchedWhispers.Core.Adversaries;

public sealed class Adversary
{
    [JsonConstructor]
    public Adversary(Guid id, string name, HitPoints hp, Armor armor, int morale, AttackProfile attack, bool isFled = false)
    {
        Id = id;
        Name = name;
        Hp = hp;
        Armor = armor;
        Morale = morale;
        Attack = attack;
        IsFled = isFled;
    }

    public Adversary(string name, HitPoints hp, Armor armor, int morale, AttackProfile attack)
        : this(Guid.NewGuid(), name, hp, armor, morale, attack)
    {
    }

    /// <summary>Builds an adversary from GM-invented stats, scaled to the campaign's difficulty. The GM
    /// stats a creature for the fiction and has no idea what the character can actually do; forgiving
    /// difficulties shrink hit points and cap armor here so the fight is winnable in a sane number of
    /// rounds. Armor is the load-bearing cap: it throttles the rate of damage, so no amount of hit-point
    /// scaling rescues a d4 weapon from a d6-reduction hide.</summary>
    public static Adversary Create(string name, int hitPoints, ArmorTier armorTier, int morale,
        AttackProfile attack, DifficultySettings settings)
    {
        // Always leave something to kill, however forgiving the scale.
        var scaledHp = Math.Max(1, (int)Math.Round(hitPoints * settings.AdversaryHpScale,
            MidpointRounding.AwayFromZero));
        // ArmorTier is ordered None < Light < Medium < Heavy, so the cap is a clamp.
        var cappedTier = (ArmorTier)Math.Min((int)armorTier, (int)settings.MaxAdversaryArmor);

        return new Adversary(name, new HitPoints(scaledHp, scaledHp), new Armor(cappedTier), morale, attack);
    }

    public Guid Id { get; }

    public string Name { get; }

    [JsonInclude] public HitPoints Hp { get; private set; }

    [JsonInclude] public Armor Armor { get; private set; }

    public int Morale { get; }

    public AttackProfile Attack { get; }

    [JsonIgnore] public bool IsDead => Hp.Current <= 0;

    [JsonInclude] public bool IsFled { get; private set; }

    public void ReceiveDamage(int amount)
    {
        Hp = Hp.Damage(amount);
    }

    public void Retreat()
    {
        IsFled = true;
    }
}