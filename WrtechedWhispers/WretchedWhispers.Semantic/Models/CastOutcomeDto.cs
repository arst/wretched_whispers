namespace WretchedWhispers.Semantic.Models;

public record CastOutcomeDto
{
    public bool Succeeded { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? PowerKey { get; init; }
    public int HpLost { get; init; }
}