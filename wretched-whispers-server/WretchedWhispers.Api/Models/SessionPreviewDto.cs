using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Api.Models;

public record SessionPreviewDto(
    Guid SessionId,
    string CampaignName,
    string Description,
    string? CharacterName,
    int? CurrentHp,
    int? MaxHp,
    string Status,
    Difficulty Difficulty,
    DateTime? LastPlayed,
    // Trailing and optional to keep existing positional call sites compiling. Null for classless wretches.
    string? CharacterClass = null
);
