namespace WretchedWhispers.Core.Combat.Defence;

public readonly record struct DefenceResolutionOutcome(
    bool Avoided,
    bool CriticalFreeAttack,
    bool FumbleDoubleDamage
);