namespace WretchedWhispers.Core.Characters.Combat;

public readonly record struct AttackOutcome(
    bool Hit,
    Damage Damage,
    bool Critical,
    bool Fumble,
    bool WeaponBroken,
    bool TargetArmorDegraded
);