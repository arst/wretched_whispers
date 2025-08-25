namespace WretchedWhispers.Semantic.Models;

public record NewAdversaryDto(
    string Name,
    int HitPoints,
    string ArmorTier,
    int Morale,
    string WeaponDescription,
    string WeaponDamageDie);