using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public record NewAdversaryDto(
    [property: Description("The adversary's name")]
    string Name,
    [property: Description("Hit points for the adversary")]
    int HitPoints,
    [property: Description("Level of armor protection the adversary has")]
    ArmorTierDto ArmorTier,
    [property: Description("Morale score affecting the adversary's willingness to fight")]
    int Morale,
    [property: Description("Description of the adversary's weapon")]
    string WeaponDescription,
    [property: Description("Dice expression for weapon damage (e.g., '1d4', '1d6', '1d8')")]
    string WeaponDamageDie);