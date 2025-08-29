using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public record CampaignDto(
    [property: JsonPropertyName("Id")]
    [property: Description("Unique identifier for the campaign")]
    Guid Id,
    
    [property: JsonPropertyName("Name")]
    [property: Description("The campaign's name")]
    string Name,
    
    [property: JsonPropertyName("Description")]
    [property: Description("Description of the campaign setting and context")]
    string Description,
    
    [property: JsonPropertyName("CurrentDay")]
    [property: Description("Current day number in the campaign")]
    int CurrentDay,
    
    [property: JsonPropertyName("CurrentHour")]
    [property: Description("Current hour of the day (0-23)")]
    int CurrentHour,
    
    [property: JsonPropertyName("Miseries")]
    [property: Description("List of miseries that have befallen the world")]
    List<MiseryDto> Miseries);