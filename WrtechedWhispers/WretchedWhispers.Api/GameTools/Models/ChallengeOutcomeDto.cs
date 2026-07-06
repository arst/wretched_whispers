namespace WretchedWhispers.Api.GameTools.Models;

public record ChallengeOutcomeDto(bool IsSuccess, int Roll, int Modifier, int Total, int Dr, int DamageTaken, bool IsDead);