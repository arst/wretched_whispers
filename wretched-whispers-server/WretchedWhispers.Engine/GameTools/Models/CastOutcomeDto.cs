using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.GameTools.Models;

public record CastOutcomeDto
{
    [JsonPropertyName("Succeeded")]
    [Description("Whether the spell casting was successful")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("Reason")]
    [Description("Explanation of what happened during the casting attempt")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("PowerKey")]
    [Description("Key of the power that was cast, if successful")]
    public string? PowerKey { get; init; }

    [JsonPropertyName("HpLost")]
    [Description("Hit points lost during the casting attempt")]
    public int HpLost { get; init; }
}