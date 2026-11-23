using WretchedWhispers.Core.Characters.Status;

namespace WretchedWhispers.Core.Characters.Challenge;

// CurrentHp is the character's HP AFTER any consequence damage. With NewInjury set the character was
// Broken (dropped to 0, survived the Broken table with that injury) — the model must be able to tell
// that apart from death so it narrates a collapse, not a fabricated death.
public sealed record ChallengeResult(
    ChallengeOutcome Outcome, int DamageTaken, bool IsDead, int CurrentHp, InjuryKind? NewInjury = null);

/// <summary>What a failed test cost: the damage rolled and, when it broke the character, the injury.</summary>
public readonly record struct ConsequenceOutcome(int Damage, InjuryKind? Injury);
