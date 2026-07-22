using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.GameTools.Models;

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

    [JsonPropertyName("Disposition")]
    [Description("Current disposition: Friendly (combat cannot start) or Hostile")]
    public string Disposition { get; set; } = string.Empty;

    [JsonPropertyName("Reaction")]
    [Description("Rolled Mörk Borg reaction when the encounter was created as Unknown: Kill, Angered, Indifferent, AlmostFriendly, or Helpful. Null when the type was pre-declared.")]
    public string? Reaction { get; set; }

    [JsonPropertyName("ReactionRoll")]
    [Description("The raw 2d6 reaction roll behind Reaction. Null when the type was pre-declared.")]
    public int? ReactionRoll { get; set; }

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