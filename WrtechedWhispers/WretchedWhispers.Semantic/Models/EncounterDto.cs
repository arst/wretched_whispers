namespace WretchedWhispers.Semantic.Models;

public class EncounterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<AdversaryDto> Adversaries { get; set; } = new();
    public List<AdversaryDto> LivingAdversaries { get; set; } = new();
    public List<AdversaryDto> DeadAdversaries { get; set; } = new();
}