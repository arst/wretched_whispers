using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.GameTools.Models;

public record NewAdversaryDto(
    [property: JsonPropertyName("Name")]
    [property: Description("The adversary's name")]
    string Name,
    [property: JsonPropertyName("HitPoints")]
    [property: Description("Hit points for the adversary")]
    int HitPoints,
    [property: JsonPropertyName("ArmorTier")]
    [property: Description("Level of armor protection the adversary has")]
    ArmorTierDto ArmorTier,
    [property: JsonPropertyName("Morale")]
    [property: Description("Morale score affecting the adversary's willingness to fight")]
    int Morale,
    [property: JsonPropertyName("WeaponDescription")]
    [property: Description("Description of the adversary's weapon")]
    string WeaponDescription,
    [property: JsonPropertyName("WeaponDamageDie")]
    [property: Description("Dice expression for weapon damage (e.g., '1d4', '1d6', '1d8')")]
    string WeaponDamageDie);