namespace WretchedWhispers.Core.Characters.Combat;

public readonly record struct DefenceOutcome(
    int DamageDealt,
    bool Avoided,
    bool CriticalFreeAttack,
    bool FumbleDoubleDamage,
    int OmenDamageReduction = 0,
    // The dodge test itself, so a hit taken is explainable instead of narrated blind: Avoided = Roll +
    // Modifier >= EffectiveDr (naturals override). Always Agility. EffectiveDr already carries armor,
    // encumbrance and injury penalties — usually why being hit repeatedly surprises the player.
    int Roll = 0,
    int Modifier = 0,
    int EffectiveDr = 0,
    // Breakdown of a hit that landed, mirroring AttackOutcome: the adversary's raw weapon-die roll
    // (before the fumble doubling) and the armor+shield reduction subtracted. DamageDealt =
    // max(0, BaseDamageRoll * (FumbleDoubleDamage ? 2 : 1) - ArmorReduction) - OmenDamageReduction.
    int BaseDamageRoll = 0,
    int ArmorReduction = 0);