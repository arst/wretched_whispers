namespace WretchedWhispers.Core.Outcomes;

public sealed record UnconsciousBroken(int Rounds, int AwakenHp) : BrokenOutcome("Unconscious");