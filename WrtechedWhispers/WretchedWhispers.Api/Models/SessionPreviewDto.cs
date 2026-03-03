namespace WretchedWhispers.Api.Models;

public record SessionPreviewDto(
    Guid SessionId,
    string CampaignName,
    string Description,
    string? CharacterName,
    int? CurrentHp,
    int? MaxHp,
    string Status,
    DateTime? LastPlayed
);
