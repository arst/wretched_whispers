namespace WretchedWhispers.Core.Characters.Combat;

public readonly record struct AttackOutcome(
    bool Hit,
    Damage Damage,
    bool Critical,
    bool Fumble,
    bool WeaponBroken,
    bool TargetArmorDegraded,
    // Breakdown so the final Damage is auditable: the raw weapon-die roll (before the crit doubling
    // and armor), and the armor reduction subtracted. Final Damage = max(0, BaseDamageRoll * (Critical ? 2 : 1) - DamageReduction).
    int BaseDamageRoll = 0,
    int DamageReduction = 0
);