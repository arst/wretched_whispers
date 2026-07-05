namespace WretchedWhispers.Core.Characters.Challenge;

public sealed record ChallengeResult(ChallengeOutcome Outcome, int DamageTaken, bool IsDead);
