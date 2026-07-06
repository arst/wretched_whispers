namespace WretchedWhispers.Api.GameTools.Models;

public record ChallengeOutcomeDto(bool IsSuccess, int Roll, int Modifier, int Dr, int DamageTaken, bool IsDead);