namespace WretchedWhispers.Core.Combat.Attack;

public readonly record struct AttackResolutionOutcome(
    bool Hit,
    Damage Damage,
    bool Critical,
    bool Fumble,
    bool WeaponBroken,
    bool TargetArmorDegraded);