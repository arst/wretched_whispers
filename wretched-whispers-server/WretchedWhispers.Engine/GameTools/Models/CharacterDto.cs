using System.ComponentModel;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Possessions.Weapons;

namespace WretchedWhispers.Engine.GameTools.Models;

public class CharacterDto
{
    [Description("The character's name")]
    public string Name { get; set; } = string.Empty;

    // Abilities
    [Description("Character's agility ability score, affects speed and dexterity")]
    public int Agility { get; set; }

    [Description("Character's presence ability score, affects social interactions and magic")]
    public int Presence { get; set; }

    [Description("Character's strength ability score, affects combat and physical tasks")]
    public int Strength { get; set; }

    [Description("Character's toughness ability score, affects health and endurance")]
    public int Toughness { get; set; }

    // Hit Points
    [Description("Character's current hit points")]
    public int CurrentHp { get; set; }

    [Description("Character's maximum hit points")]
    public int MaxHp { get; set; }

    // Resources
    [Description("Amount of silver coins the character has")]
    public int Silver { get; set; }

    [Description("Number of days worth of food the character has")]
    public int FoodDays { get; set; }

    // Equipment
    [Description("Type of weapon the character is wielding")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeaponKind WeaponKind { get; set; }

    [Description("Level of armor protection the character has")]
    public ArmorTierDto ArmorTier { get; set; }

    [Description("Whether the character has a shield")]
    public bool HasShield { get; set; }

    [Description("Whether the character's shield is broken")]
    public bool IsShieldBroken { get; set; }

    // Resources
    [Description("Number of omens the character has accumulated")]
    public int OmenCount { get; set; }

    [Description("Scroll castings left today. Spent ONLY by CastScroll, and only if the character owns a scroll — there is no other way to use one. A character with no scrolls cannot spend these.")]
    public int PowersRemaining { get; set; }

    [Description("Scroll castings per day (Presence + d4, re-rolled at dawn)")]
    public int PowersMax { get; set; }

    // Status Effects
    [Description("Whether the character is infected with disease")]
    public bool IsInfected { get; set; }

    [Description("Whether the character is dizzy from magical effects")]
    public bool IsDizzyFromMagic { get; set; }

    [Description("Carrying more than Strength+8 items: +2 DR on every Strength and Agility test, which includes attacking and dodging. Dropping items clears it.")]
    public bool IsEncumbered { get; set; }

    [Description("Whether the character is dead")]
    public bool IsDead { get; set; }

    // Injuries/Conditions
    [Description("Whether the character has lost an eye")]
    public bool HasLostEye { get; set; }

    [Description("Whether the character has a stabbed lung")]
    public bool HasStabbedLung { get; set; }

    [Description("Whether the character has a broken hand")]
    public bool HasBrokenHand { get; set; }

    [Description("Whether the character has a crushed foot")]
    public bool HasCrushedFoot { get; set; }

    [Description("Whether the character has a severed arm")]
    public bool HasSeveredArm { get; set; }

    [Description("Whether the character has a smashed face")]
    public bool HasSmashedFace { get; set; }

    // Known Scrolls
    [Description("List of magical scrolls the character possesses")]
    public List<ScrollDto> Scrolls { get; set; } = new();

    [Description("Character's inventory containing items and equipment")]
    public required InventoryDto Inventory { get; set; }
}