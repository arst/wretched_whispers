using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record PlayerAttackDto(
    string Target, bool Hit, int Damage, bool Critical, bool Fumble,
    bool WeaponBroken, bool TargetArmorDegraded, int BaseDamageRoll, int DamageReduction);

public sealed record FleeAttemptDto(bool Success, int Roll, int Modifier, int Dr);

public sealed record RetaliationDto(
    string AdversaryName, int DamageDealt, bool Avoided, bool CriticalFreeAttack, bool FumbleDoubleDamage,
    int OmenDamageReduction = 0);

public sealed record CombatRoundOutcomeDto(
    PlayerAttackDto? PlayerAttack,
    FleeAttemptDto? FleeAttempt,
    IReadOnlyList<RetaliationDto> Retaliations,
    IReadOnlyList<string> AdversariesFled,
    bool EncounterEnded,
    string EndReason,
    // Post-round player condition. PlayerBroken (0 HP, alive) is a survived hit, NOT death — the model
    // narrates a collapse. Death is only the PlayerDead EndReason.
    int PlayerCurrentHp,
    bool PlayerBroken)
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
                r.Outcome.CriticalFreeAttack, r.Outcome.FumbleDoubleDamage, r.Outcome.OmenDamageReduction))
            .ToList(),
        outcome.AdversariesFledThisRound,
        outcome.EncounterEnded,
        outcome.EndReason.ToString(),
        outcome.PlayerCurrentHp,
        outcome.PlayerBroken);
}
