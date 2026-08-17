using WretchedWhispers.Core.Characters.Abilities;

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
    int DamageReduction = 0,
    // The to-hit test itself, so a miss is explainable instead of narrated blind: Hit = Roll + Modifier
    // >= EffectiveDr (naturals override). EffectiveDr already carries encumbrance and injury penalties,
    // which is usually why a miss surprises the player.
    int Roll = 0,
    int Modifier = 0,
    int EffectiveDr = 0,
    // Melee tests Strength, ranged tests Presence. Named rather than left to be inferred — a player
    // with good Agility and bad Strength cannot otherwise tell why their modifier is negative.
    AbilityKind Ability = AbilityKind.Strength
);
