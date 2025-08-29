using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public record MiseryDto(
    [property: JsonPropertyName("Code")]
    [property: Description("Unique code identifier for this misery")]
    string Code, 
    
    [property: JsonPropertyName("Psalm")]
    [property: Description("The psalm or description text of this misery")]
    string Psalm);
