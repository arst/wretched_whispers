using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

public record CharacterAttackOutcomeDto(
    [property: JsonPropertyName("IsHit")]
    [property: Description("Whether the attack successfully hit the target")]
    bool IsHit,
    
    [property: JsonPropertyName("DamageDealt")]
    [property: Description("Amount of damage dealt by the attack")]
    int DamageDealt,
    
    [property: JsonPropertyName("IsCritical")]
    [property: Description("Whether the attack was a critical hit")]
    bool IsCritical,
    
    [property: JsonPropertyName("IsFumble")]
    [property: Description("Whether the attack was a fumble/failure")]
    bool IsFumble,
    
    [property: JsonPropertyName("IsWeaponBroken")]
    [property: Description("Whether the weapon broke during the attack")]
    bool IsWeaponBroken,
    
    [property: JsonPropertyName("IsTargetArmorDegraded")]
    [property: Description("Whether the target's armor was degraded by the attack")]
    bool IsTargetArmorDegraded);