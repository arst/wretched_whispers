using System.ComponentModel;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record ScrollHitDto(string Adversary, int Damage, bool Killed);

[Description("What one scroll casting did. Everything numeric here has ALREADY been applied to the game state.")]
public sealed record CastOutcomeDto(
    [property: Description("True when the scroll's power went off")] bool Succeeded,
    [property: Description("Why it did not: 'Fizzle' is a failed casting; anything else means no casting happened")] string Reason,
    [property: Description("The scroll that was cast")] string? Scroll,
    [property: Description("What the power does, on a success. Narrate this. Numbers it names that are not in the fields below changed nothing in the game state")] string? Effect,
    [property: Description("Hit points the caster lost to a fizzle")] int HpLost,
    [property: Description("Hit points the caster regained")] int HpHealed,
    [property: Description("When above 0: the caster's next test is easier by this much")] int NextTestBonus,
    [property: Description("Adversaries of the current fight the power harmed, with the damage each took. Empty when the power harms no one or there is no fight")] IReadOnlyList<ScrollHitDto> AdversariesHit,
    [property: Description("The caster's hit points after the casting")] int CurrentHp)
{
    public static CastOutcomeDto From(ScrollCastResult result, int currentHp) => new(
        result.Outcome.Succeeded, result.Outcome.Reason, result.Outcome.PowerKey, result.Outcome.Effect,
        result.Outcome.HpLost, result.Outcome.HpHealed, result.Outcome.NextTestBonus,
        result.Hits.Select(h => new ScrollHitDto(h.AdversaryName, h.Damage, h.Killed)).ToList(),
        currentHp);
}
