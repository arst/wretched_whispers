using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record PlayerAttackDto(
    string Target, bool Hit, int Damage, bool Critical, bool Fumble,
    bool WeaponBroken, bool TargetArmorDegraded, int BaseDamageRoll, int DamageReduction,
    // The to-hit test, so the narrator explains a miss from the real numbers instead of inventing a
    // reason. Roll + Modifier vs Dr; Dr already includes encumbrance and injury penalties.
    int Roll, int Modifier, int Dr, string Ability,
    // Why Dr is not 12, each cause with its size ("encumbered +2, injury +2"); null when it is 12.
    // Without it a narrator told to explain the number guessed the cause.
    string? DrBecause = null);

public sealed record FleeAttemptDto(bool Success, int Roll, int Modifier, int Dr, string? DrBecause = null);

// Every roll here is the PLAYER's Agility dodge against the adversary's blow; the adversary rolls only
// its damage. Named for it, as playerAttack is: as roll/dr/avoided the narrator told a dodge of 13 against
// 12 as the enemy rolling 13 and missing, and a fumbled dodge as the enemy fumbling (fourth simulated
// run); as dodgeRoll beside adversaryName, as the adversary dodging.
public sealed record RetaliationDto(
    string AdversaryName, int DamageDealt, bool PlayerDodged, bool PlayerDodgeCriticalFreeAttack,
    bool PlayerDodgeFumbleDoubleDamage, int OmenDamageReduction = 0,
    // When the dodge failed, the damage breakdown, so a hit taken is explained from the numbers.
    // PlayerDodgeDr already includes armor, encumbrance and injuries.
    int PlayerDodgeRoll = 0, int PlayerDodgeModifier = 0, int PlayerDodgeDr = 0, int BaseDamageRoll = 0,
    int ArmorReduction = 0,
    // The injury this hit gave when it broke the player, in Game State's words ("lost eye").
    string? Injury = null,
    // Why PlayerDodgeDr is not 12 ("armor +2"); null when it is 12.
    string? PlayerDodgeDrBecause = null);

public sealed record CombatRoundOutcomeDto(
    PlayerAttackDto? PlayerAttack,
    FleeAttemptDto? FleeAttempt,
    IReadOnlyList<RetaliationDto> Retaliations,
    IReadOnlyList<string> AdversariesFled,
    bool EncounterEnded,
    string EndReason,
    // Post-round player condition. PlayerBroken (dropped to 0, survived with an injury) is NOT death — the model
    // narrates a collapse.
    int PlayerCurrentHp,
    bool PlayerBroken)
{
    // Spelled out beside PlayerBroken: with only EndReason to go on, a death was told as "not yet dead".
    public bool PlayerDead => EndReason == nameof(EncounterEndReason.PlayerDead);

    /// <summary>Dead or broken, in words, as the first thing in the result. See <see cref="FateWords"/>.</summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string? Fate => FateWords.For(
        PlayerDead,
        PlayerBroken ? Retaliations.Select(r => r.Injury).FirstOrDefault(injury => injury is not null) ?? "a grievous wound" : null,
        PlayerCurrentHp);

    public static CombatRoundOutcomeDto From(CombatRoundOutcome outcome) => new(
        outcome.PlayerAttack is { } attack && outcome.PlayerAttackTarget is { } target
            ? new PlayerAttackDto(target, attack.Hit, attack.Damage.Amount, attack.Critical, attack.Fumble,
                attack.WeaponBroken, attack.TargetArmorDegraded, attack.BaseDamageRoll, attack.DamageReduction,
                attack.Roll, attack.Modifier, attack.EffectiveDr, attack.Ability.ToString(),
                Because(attack.DrReasons))
            : null,
        outcome.FleeAttempt is { } flee
            ? new FleeAttemptDto(flee.IsSuccess, flee.Roll, flee.Modifier, flee.EffectiveDr,
                Because(flee.DrReasons))
            : null,
        outcome.Retaliations
            .Select(r => new RetaliationDto(r.AdversaryName, r.Outcome.DamageDealt, r.Outcome.Avoided,
                r.Outcome.CriticalFreeAttack, r.Outcome.FumbleDoubleDamage, r.Outcome.OmenDamageReduction,
                r.Outcome.Roll, r.Outcome.Modifier, r.Outcome.EffectiveDr,
                r.Outcome.BaseDamageRoll, r.Outcome.ArmorReduction, r.Outcome.Injury?.DisplayName(),
                Because(r.Outcome.DrReasons)))
            .ToList(),
        outcome.AdversariesFledThisRound,
        outcome.EncounterEnded,
        outcome.EndReason.ToString(),
        outcome.PlayerCurrentHp,
        outcome.PlayerBroken);

    internal static string? Because(IReadOnlyList<string>? drReasons) =>
        drReasons is { Count: > 0 } ? string.Join(", ", drReasons) : null;
}
