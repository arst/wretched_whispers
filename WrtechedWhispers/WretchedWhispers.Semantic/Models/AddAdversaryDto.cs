namespace WretchedWhispers.Semantic.Models;

public record AddAdversaryDto(
    string Name,
    int HitPoints,
    int Morale,
    string ArmorType,
    string DamageDice,
    string AttackDescription);