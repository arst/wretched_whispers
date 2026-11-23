namespace WretchedWhispers.Core.Characters.Status;

/// <summary>What a rest did to one character. <paramref name="DamageTaken"/> is the infection's toll.</summary>
public readonly record struct RestOutcome(
    int HpHealed, int DamageTaken, int OmensRefreshed, InjuryKind? NewInjury, bool Died);
