using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.GameTools.Models;

public record AdvanceTimeOutcomeDto(
    [property: JsonPropertyName("Miseries")]
    [property: Description("List of new miseries that occurred during the time advancement")]
    List<string> Miseries,
    [property: JsonPropertyName("IsWorldEnded")]
    [property: Description("Whether the world has ended due to accumulated miseries")]
    bool IsWorldEnded,
    [property: JsonPropertyName("IsNewDawn")]
    [property: Description("Whether a new dawn has occurred, resetting daily resources")]
    bool IsNewDawn);