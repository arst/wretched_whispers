namespace WretchedWhispers.Semantic.Models;

public class AdversaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public ArmorTierDto ArmorTier { get; set; }
    public int Morale { get; set; }
    public AttackProfileDto Attack { get; set; } = new();
    public bool IsDead { get; set; }
    public bool IsFled { get; set; }
}