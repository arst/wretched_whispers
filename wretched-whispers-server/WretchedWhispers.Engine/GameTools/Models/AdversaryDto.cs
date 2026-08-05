using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public class AdversaryDto
{
    [Description("Unique identifier for the adversary")]
    public Guid Id { get; set; }

    [Description("The adversary's name")]
    public string Name { get; set; } = string.Empty;

    [Description("Adversary's current hit points")]
    public int CurrentHp { get; set; }

    [Description("Adversary's maximum hit points")]
    public int MaxHp { get; set; }

    [Description("Level of armor protection the adversary has")]
    public ArmorTierDto ArmorTier { get; set; }

    [Description("Adversary's morale score, affects willingness to fight")]
    public int Morale { get; set; }

    [Description("Adversary's attack profile including weapon and damage")]
    public AttackProfileDto Attack { get; set; } = new();

    [Description("Whether the adversary is dead")]
    public bool IsDead { get; set; }

    [Description("Whether the adversary has fled from combat")]
    public bool IsFled { get; set; }
}