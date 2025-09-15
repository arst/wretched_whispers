using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public record ChallengeOutcomeDto(
    [property: JsonPropertyName("IsSuccess")]
    [property: Description("Whether the challenge was successfully completed")]
    bool IsSuccess);