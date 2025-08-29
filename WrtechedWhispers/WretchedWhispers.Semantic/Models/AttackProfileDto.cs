using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public class AttackProfileDto
{
    [JsonPropertyName("DamageDice")]
    [Description("Dice expression for weapon damage (e.g., '1d6', '2d4')")]
    public string DamageDice { get; set; } = string.Empty;
    
    [JsonPropertyName("Description")]
    [Description("Description of the weapon or attack method")]
    public string Description { get; set; } = string.Empty;
}