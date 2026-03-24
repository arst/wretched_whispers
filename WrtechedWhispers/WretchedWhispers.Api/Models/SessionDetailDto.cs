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
    string[]? CharacterInventory = null
);
