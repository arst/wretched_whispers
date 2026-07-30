using System.Text.Json.Serialization;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Weapons;

public sealed class Weapon
{
    [JsonConstructor]
    private Weapon(WeaponKind kind, DiceExpr damageDie)
    {
        Kind = kind;
        DamageDie = damageDie;
    }

    public WeaponKind Kind { get; }

    public DiceExpr DamageDie { get; }

    public bool IsTwoHanded => Kind == WeaponKind.Zweihander;

    public bool IsRanged => Kind is WeaponKind.Bow or WeaponKind.Crossbow;

    public static Weapon Create(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Femur or WeaponKind.Staff or WeaponKind.ShortSword or WeaponKind.Knife
                or WeaponKind.Fangs => new Weapon(kind, damageDie: DiceExpr.D4),
            WeaponKind.Warhammer or WeaponKind.Sword or WeaponKind.Bow
                or WeaponKind.Claws => new Weapon(kind, damageDie: DiceExpr.D6),
            WeaponKind.Flail or WeaponKind.Crossbow => new Weapon(kind, damageDie: DiceExpr.D8),
            WeaponKind.Zweihander => new Weapon(kind, damageDie: DiceExpr.D10),
            _ => new Weapon(WeaponKind.Improvised, damageDie: DiceExpr.D4)
        };
    }
}