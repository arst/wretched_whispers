namespace WretchedWhispers.Engine.GameTools.Models;

public record ChallengeOutcomeDto(bool IsSuccess, int Roll, int Modifier, int Total, int Dr, int DamageTaken, bool IsDead);