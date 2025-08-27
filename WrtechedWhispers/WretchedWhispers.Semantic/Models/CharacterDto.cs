using WretchedWhispers.Core.Characters.Posessions.Weapon;

namespace WretchedWhispers.Semantic.Models;

public class CharacterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Abilities
    public int Agility { get; set; }
    public int Presence { get; set; }
    public int Strength { get; set; }
    public int Toughness { get; set; }

    // Hit Points
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }

    // Resources
    public int Silver { get; set; }
    public int FoodDays { get; set; }
    
    // Equipment
    public WeaponKind WeaponKind { get; set; }
    public ArmorTierDto ArmorTier { get; set; }
    public bool HasShield { get; set; }
    public bool IsShieldBroken { get; set; }

    // Resources
    public int OmenCount { get; set; }
    public int PowersUsed { get; set; }
    public int PowersMax { get; set; }

    // Status Effects
    public bool IsInfected { get; set; }
    public bool IsDizzyFromMagic { get; set; }
    public bool IsEncumbered { get; set; }
    public bool IsDead { get; set; }

    // Injuries/Conditions
    public bool HasLostEye { get; set; }
    public bool HasStabbedLung { get; set; }
    public bool HasBrokenHand { get; set; }
    public bool HasCrushedFoot { get; set; }
    public bool HasSeveredArm { get; set; }
    public bool HasSmashedFace { get; set; }

    // Known Scrolls
    public List<ScrollDto> Scrolls { get; set; } = new();

    public required InventoryDto Inventory { get; set; }
}