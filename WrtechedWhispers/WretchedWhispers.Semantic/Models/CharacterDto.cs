using WretchedWhispers.Core.Characters.Weapon;

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

    // Gear
    public string Container { get; set; } = string.Empty;
    public string Gear1 { get; set; } = string.Empty;
    public string Gear2 { get; set; } = string.Empty;

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

    // Known Scrolls
    public List<ScrollDto> KnownScrolls { get; set; } = new();
}