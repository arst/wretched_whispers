using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public class AdversaryDto
{
    [JsonPropertyName("Id")]
    [Description("Unique identifier for the adversary")]
    public Guid Id { get; set; }

    [JsonPropertyName("Name")]
    [Description("The adversary's name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("CurrentHp")]
    [Description("Adversary's current hit points")]
    public int CurrentHp { get; set; }

    [JsonPropertyName("MaxHp")]
    [Description("Adversary's maximum hit points")]
    public int MaxHp { get; set; }

    [JsonPropertyName("ArmorTier")]
    [Description("Level of armor protection the adversary has")]
    public ArmorTierDto ArmorTier { get; set; }

    [JsonPropertyName("Morale")]
    [Description("Adversary's morale score, affects willingness to fight")]
    public int Morale { get; set; }

    [JsonPropertyName("Attack")]
    [Description("Adversary's attack profile including weapon and damage")]
    public AttackProfileDto Attack { get; set; } = new();

    [JsonPropertyName("IsDead")]
    [Description("Whether the adversary is dead")]
    public bool IsDead { get; set; }

    [JsonPropertyName("IsFled")]
    [Description("Whether the adversary has fled from combat")]
    public bool IsFled { get; set; }
}