namespace WretchedWhispers.Core.Characters.Challenge;

// CurrentHp is the character's HP AFTER any consequence damage. At 0 HP with IsDead false the
// character is Broken (survived the Broken table with an injury) — the model must be able to tell
// that apart from death so it narrates a collapse, not a fabricated death.
public sealed record ChallengeResult(ChallengeOutcome Outcome, int DamageTaken, bool IsDead, int CurrentHp);
