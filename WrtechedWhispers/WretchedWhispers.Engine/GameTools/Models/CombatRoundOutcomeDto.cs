using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record PlayerAttackDto(
    string Target, bool Hit, int Damage, bool Critical, bool Fumble,
    bool WeaponBroken, bool TargetArmorDegraded, int BaseDamageRoll, int DamageReduction);

public sealed record FleeAttemptDto(bool Success, int Roll, int Modifier, int Dr);

public sealed record RetaliationDto(
    string AdversaryName, int DamageDealt, bool Avoided, bool CriticalFreeAttack, bool FumbleDoubleDamage);

public sealed record CombatRoundOutcomeDto(
    PlayerAttackDto? PlayerAttack,
    FleeAttemptDto? FleeAttempt,
    IReadOnlyList<RetaliationDto> Retaliations,
    IReadOnlyList<string> AdversariesFled,
    bool EncounterEnded,
    string EndReason)
{
    public static CombatRoundOutcomeDto From(CombatRoundOutcome outcome) => new(
        outcome.PlayerAttack is { } attack && outcome.PlayerAttackTarget is { } target
            ? new PlayerAttackDto(target, attack.Hit, attack.Damage.Amount, attack.Critical, attack.Fumble,
                attack.WeaponBroken, attack.TargetArmorDegraded, attack.BaseDamageRoll, attack.DamageReduction)
            : null,
        outcome.FleeAttempt is { } flee
            ? new FleeAttemptDto(flee.IsSuccess, flee.Roll, flee.Modifier, flee.EffectiveDr)
            : null,
        outcome.Retaliations
            .Select(r => new RetaliationDto(r.AdversaryName, r.Outcome.DamageDealt, r.Outcome.Avoided,
                r.Outcome.CriticalFreeAttack, r.Outcome.FumbleDoubleDamage))
            .ToList(),
        outcome.AdversariesFledThisRound,
        outcome.EncounterEnded,
        outcome.EndReason.ToString());
}
