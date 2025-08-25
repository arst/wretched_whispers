namespace WretchedWhispers.Semantic.Models;

public record CharacterAttackOutcomeDto(
    bool IsHit,
    int DamageDealt,
    bool IsCritical,
    bool IsFumble,
    bool IsWeaponBroken,
    bool IsTargetArmorDegraded);