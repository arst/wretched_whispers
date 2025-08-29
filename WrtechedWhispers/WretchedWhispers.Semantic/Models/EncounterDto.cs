using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public class EncounterDto
{
    [JsonPropertyName("Id")]
    [Description("Unique identifier for the encounter")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    [Description("The encounter's name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("Description")]
    [Description("Description of the encounter setting and circumstances")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("Adversaries")]
    [Description("All adversaries in this encounter")]
    public List<AdversaryDto> Adversaries { get; set; } = new();
    
    [JsonPropertyName("LivingAdversaries")]
    [Description("Adversaries that are still alive and fighting")]
    public List<AdversaryDto> LivingAdversaries { get; set; } = new();
    
    [JsonPropertyName("DeadAdversaries")]
    [Description("Adversaries that have been killed or fled")]
    public List<AdversaryDto> DeadAdversaries { get; set; } = new();
}