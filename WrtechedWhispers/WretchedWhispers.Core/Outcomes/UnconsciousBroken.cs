namespace WretchedWhispers.Core.Outcomes;

public sealed record UnconsciousBroken(int Rounds, int AwakenWithHp) : BrokenOutcome("Unconscious");