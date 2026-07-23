using WretchedWhispers.Core.Characters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record AbilityChangeDto(string Ability, int Roll, int Delta, int NewScore);

/// <summary>Result of the Getting Better ritual. Negative ability deltas are the RAW "or worse" --
/// narrate the regression, don't soften it into nothing. HpGained 0 means the 6d10 check failed.</summary>
public sealed record GettingBetterOutcomeDto(
    int HpRoll, int HpGained, int NewMaxHp, IReadOnlyList<AbilityChangeDto> Abilities)
{
    public static GettingBetterOutcomeDto From(GettingBetterOutcome outcome) => new(
        outcome.HpRoll, outcome.HpGained, outcome.NewMaxHp,
        outcome.Abilities
            .Select(a => new AbilityChangeDto(a.Kind.ToString(), a.Roll, a.Delta, a.NewScore))
            .ToList());
}
