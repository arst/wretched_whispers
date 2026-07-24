using WretchedWhispers.Core.Characters.Abilities;

namespace WretchedWhispers.Core.Characters;

/// <summary>One ability's roll in the Getting Better ritual. Delta is +1, -1, or 0 (capped,
/// floored, or loss disabled by difficulty).</summary>
public sealed record AbilityChange(AbilityKind Kind, int Roll, int Delta, int NewScore);

/// <summary>The full result of a Getting Better ritual: the 6d10 HP check (HpGained 0 when it
/// failed) and one entry per ability in Strength, Agility, Presence, Toughness order.</summary>
public sealed record GettingBetterOutcome(
    int HpRoll, int HpGained, int NewMaxHp, IReadOnlyList<AbilityChange> Abilities);
