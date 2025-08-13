using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Characters.Weapon;

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
            WeaponKind.Femur => new Weapon(kind, DiceExpr.d4),
            WeaponKind.Staff => new Weapon(kind, DiceExpr.d4),
            WeaponKind.ShortSword => new Weapon(kind, DiceExpr.d4),
            WeaponKind.Knife => new Weapon(kind, DiceExpr.d4),
            WeaponKind.Warhammer => new Weapon(kind, DiceExpr.d6),
            WeaponKind.Sword => new Weapon(kind, DiceExpr.d6),
            WeaponKind.Bow => new Weapon(kind, DiceExpr.d6),
            WeaponKind.Flail => new Weapon(kind, DiceExpr.d8),
            WeaponKind.Crossbow => new Weapon(kind, DiceExpr.d8),
            WeaponKind.Zweihander => new Weapon(kind, DiceExpr.d10),
            _ => new Weapon(WeaponKind.Improvised, DiceExpr.d4)
        };
    }
}