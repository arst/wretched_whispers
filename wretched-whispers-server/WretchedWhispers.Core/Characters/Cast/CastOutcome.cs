using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Status;

namespace WretchedWhispers.Core.Characters.Cast;

/// <summary>What one casting did. <paramref name="Power"/> is set on a success with a scroll that has a
/// known power; what that power did to the caster is in <paramref name="HpHealed"/> and
/// <paramref name="NextTestBonus"/>, and what it does to others is applied by the encounter.
/// <paramref name="NewInjury"/> is the injury a fizzle gave when its cost broke the caster.</summary>
public readonly record struct CastOutcome(
    bool Succeeded, string Reason, string? PowerKey, int HpLost,
    ScrollPower? Power = null, int HpHealed = 0, int NextTestBonus = 0, InjuryKind? NewInjury = null)
{
    public string? Effect => Power?.Effect;

    public static CastOutcome Success(string key, ScrollPower? power = null, int hpHealed = 0, int nextTestBonus = 0)
    {
        return new CastOutcome(true, string.Empty, key, 0, power, hpHealed, nextTestBonus);
    }

    public static CastOutcome Fail(string reason)
    {
        return new CastOutcome(false, reason, null, 0);
    }

    public static CastOutcome Fizzle(string key, int hpLost, InjuryKind? injury = null)
    {
        return new CastOutcome(false, "Fizzle", key, hpLost, NewInjury: injury);
    }
}
