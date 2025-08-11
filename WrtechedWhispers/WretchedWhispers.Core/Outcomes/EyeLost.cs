namespace WretchedWhispers.Core.Outcomes;

public sealed record EyeLost(int StunRounds) : BrokenOutcome("EyeLost");