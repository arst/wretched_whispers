using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public class EncounterDto
{
    [Description("Unique identifier for the encounter")]
    public Guid Id { get; set; }

    [Description("The encounter's name")]
    public string Name { get; set; } = string.Empty;

    [Description("Description of the encounter setting and circumstances")]
    public string Description { get; set; } = string.Empty;

    [Description("Current disposition: Friendly (combat cannot start) or Hostile")]
    public string Disposition { get; set; } = string.Empty;

    [Description("Rolled Mörk Borg reaction when the encounter was created as Unknown: Kill, Angered, Indifferent, AlmostFriendly, or Helpful. Null when the type was pre-declared.")]
    public string? Reaction { get; set; }

    [Description("The raw 2d6 reaction roll behind Reaction. Null when the type was pre-declared.")]
    public int? ReactionRoll { get; set; }

    [Description("All adversaries in this encounter")]
    public List<AdversaryDto> Adversaries { get; set; } = new();

    [Description("Adversaries that are still alive and fighting")]
    public List<AdversaryDto> LivingAdversaries { get; set; } = new();

    [Description("Adversaries that have been killed or fled")]
    public List<AdversaryDto> DeadAdversaries { get; set; } = new();
}