using System.ComponentModel;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Possessions.Weapons;

namespace WretchedWhispers.Semantic.Models;

public class CharacterDto
{
    [JsonPropertyName("Id")]
    [Description("Unique identifier for the character")]
    public Guid Id { get; set; }

    [JsonPropertyName("Name")]
    [Description("The character's name")]
    public string Name { get; set; } = string.Empty;

    // Abilities
    [JsonPropertyName("Agility")]
    [Description("Character's agility ability score, affects speed and dexterity")]
    public int Agility { get; set; }

    [JsonPropertyName("Presence")]
    [Description("Character's presence ability score, affects social interactions and magic")]
    public int Presence { get; set; }

    [JsonPropertyName("Strength")]
    [Description("Character's strength ability score, affects combat and physical tasks")]
    public int Strength { get; set; }

    [JsonPropertyName("Toughness")]
    [Description("Character's toughness ability score, affects health and endurance")]
    public int Toughness { get; set; }

    // Hit Points
    [JsonPropertyName("CurrentHp")]
    [Description("Character's current hit points")]
    public int CurrentHp { get; set; }

    [JsonPropertyName("MaxHp")]
    [Description("Character's maximum hit points")]
    public int MaxHp { get; set; }

    // Resources
    [JsonPropertyName("Silver")]
    [Description("Amount of silver coins the character has")]
    public int Silver { get; set; }

    [JsonPropertyName("FoodDays")]
    [Description("Number of days worth of food the character has")]
    public int FoodDays { get; set; }

    // Equipment
    [JsonPropertyName("WeaponKind")]
    [Description("Type of weapon the character is wielding")]
    public WeaponKind WeaponKind { get; set; }

    [JsonPropertyName("ArmorTier")]
    [Description("Level of armor protection the character has")]
    public ArmorTierDto ArmorTier { get; set; }

    [JsonPropertyName("HasShield")]
    [Description("Whether the character has a shield")]
    public bool HasShield { get; set; }

    [JsonPropertyName("IsShieldBroken")]
    [Description("Whether the character's shield is broken")]
    public bool IsShieldBroken { get; set; }

    // Resources
    [JsonPropertyName("OmenCount")]
    [Description("Number of omens the character has accumulated")]
    public int OmenCount { get; set; }

    [JsonPropertyName("PowersUsed")]
    [Description("Number of powers/spells used today")]
    public int PowersUsed { get; set; }

    [JsonPropertyName("PowersMax")]
    [Description("Maximum number of powers/spells per day")]
    public int PowersMax { get; set; }

    // Status Effects
    [JsonPropertyName("IsInfected")]
    [Description("Whether the character is infected with disease")]
    public bool IsInfected { get; set; }

    [JsonPropertyName("IsDizzyFromMagic")]
    [Description("Whether the character is dizzy from magical effects")]
    public bool IsDizzyFromMagic { get; set; }

    [JsonPropertyName("IsEncumbered")]
    [Description("Whether the character is carrying too much weight")]
    public bool IsEncumbered { get; set; }

    [JsonPropertyName("IsDead")]
    [Description("Whether the character is dead")]
    public bool IsDead { get; set; }

    // Injuries/Conditions
    [JsonPropertyName("HasLostEye")]
    [Description("Whether the character has lost an eye")]
    public bool HasLostEye { get; set; }

    [JsonPropertyName("HasStabbedLung")]
    [Description("Whether the character has a stabbed lung")]
    public bool HasStabbedLung { get; set; }

    [JsonPropertyName("HasBrokenHand")]
    [Description("Whether the character has a broken hand")]
    public bool HasBrokenHand { get; set; }

    [JsonPropertyName("HasCrushedFoot")]
    [Description("Whether the character has a crushed foot")]
    public bool HasCrushedFoot { get; set; }

    [JsonPropertyName("HasSeveredArm")]
    [Description("Whether the character has a severed arm")]
    public bool HasSeveredArm { get; set; }

    [JsonPropertyName("HasSmashedFace")]
    [Description("Whether the character has a smashed face")]
    public bool HasSmashedFace { get; set; }

    // Known Scrolls
    [JsonPropertyName("Scrolls")]
    [Description("List of magical scrolls the character possesses")]
    public List<ScrollDto> Scrolls { get; set; } = new();

    [JsonPropertyName("Inventory")]
    [Description("Character's inventory containing items and equipment")]
    public required InventoryDto Inventory { get; set; }
}