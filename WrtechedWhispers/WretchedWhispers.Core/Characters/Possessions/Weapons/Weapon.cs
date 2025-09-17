using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Weapons;

public sealed class Weapon
{
    private Weapon(WeaponKind kind, DiceExpr dmg)
    {
        Kind = kind;
        DamageDie = dmg;
    }

    public WeaponKind Kind { get; }

    public DiceExpr DamageDie { get; }

    public bool IsTwoHanded => Kind == WeaponKind.Zweihander;

    public bool IsRanged => Kind is WeaponKind.Bow or WeaponKind.Crossbow;

    public static Weapon Create(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Femur or WeaponKind.Staff or WeaponKind.ShortSword or WeaponKind.Knife => new Weapon(kind,
                DiceExpr.D4),
            WeaponKind.Warhammer or WeaponKind.Sword or WeaponKind.Bow => new Weapon(kind, DiceExpr.D6),
            WeaponKind.Flail or WeaponKind.Crossbow => new Weapon(kind, DiceExpr.D8),
            WeaponKind.Zweihander => new Weapon(kind, DiceExpr.D10),
            _ => new Weapon(WeaponKind.Improvised, DiceExpr.D4)
        };
    }
}