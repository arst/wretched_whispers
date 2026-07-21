using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;

namespace WretchedWhispers.Core.Encounters;

/// <summary>The player's declared action for one combat round. 'Other' means the player's action was
/// already resolved by a different tool this turn (scroll, item) — the round still runs retaliation.</summary>
public enum PlayerRoundAction { Attack, Flee, Other }

/// <summary>Pre-declared omen spend for one combat round. One omen per round at most.</summary>
public enum CombatOmenUse { None, MaxDamage, ReduceDamageTaken }

public enum EncounterEndReason { None, AllDefeated, PlayerFled, PlayerDead }

public sealed record AdversaryRetaliation(string AdversaryName, DefenceOutcome Outcome);

/// <summary>Everything that happened in one domain-resolved combat round, in resolution order:
/// player action, adversary retaliation, morale flights, and whether the encounter ended.</summary>
public sealed record CombatRoundOutcome(
    AttackOutcome? PlayerAttack,
    string? PlayerAttackTarget,
    ChallengeOutcome? FleeAttempt,
    IReadOnlyList<AdversaryRetaliation> Retaliations,
    IReadOnlyList<string> AdversariesFledThisRound,
    bool EncounterEnded,
    EncounterEndReason EndReason);
