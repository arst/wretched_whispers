namespace WretchedWhispers.Api.Models;

public record SessionDetailDto(
    Guid SessionId,
    Guid CampaignId,
    string CampaignName,
    string Description,
    int CurrentDay,
    int CurrentHour,
    string Status,
    List<ChatMessageDto> Messages,
    int TotalMessages,
    int Page,
    int PageSize,
    string? CharacterName = null,
    int? CharacterHp = null,
    int? CharacterMaxHp = null,
    int? CharacterStrength = null,
    int? CharacterAgility = null,
    int? CharacterPresence = null,
    int? CharacterToughness = null,
    string? CharacterWeapon = null,
    string? CharacterArmor = null,
    string[]? CharacterInventory = null,
    // Phase 6 fields: injuries
    bool? CharacterHasLostEye = null,
    bool? CharacterHasStabbedLung = null,
    bool? CharacterHasBrokenHand = null,
    bool? CharacterHasCrushedFoot = null,
    bool? CharacterHasSeveredArm = null,
    bool? CharacterHasSmashedFace = null,
    // Phase 6 fields: status conditions
    bool? CharacterIsInfected = null,
    bool? CharacterIsDizzyFromMagic = null,
    bool? CharacterIsEncumbered = null,
    bool? CharacterIsDead = null,
    // Phase 6 fields: equipment condition
    string? CharacterArmorTier = null,
    bool? CharacterHasShield = null,
    bool? CharacterIsShieldBroken = null,
    // Phase 6 fields: world state
    bool? WorldEnded = null
);
