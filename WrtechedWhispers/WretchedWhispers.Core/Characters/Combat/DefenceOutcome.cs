namespace WretchedWhispers.Core.Characters.Combat;

public readonly record struct DefenceOutcome(
    int DamageDealt,
    bool Avoided,
    bool CriticalFreeAttack,
    bool FumbleDoubleDamage);