namespace WretchedWhispers.Core.Combat.Defence;

public readonly record struct DefenceOutcome(
    bool Avoided,
    bool CriticalFreeAttack,
    bool FumbleDoubleDamage,
    bool ArmorDegraded
);