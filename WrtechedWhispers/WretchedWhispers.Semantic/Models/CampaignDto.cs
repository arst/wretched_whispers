namespace WretchedWhispers.Semantic.Models;

public record CampaignDto(
    Guid Id,
    string Name,
    string Description,
    int CurrentDay,
    int CurrentHour,
    List<MiseryDto> Miseries);

public record MiseryDto(string Code, string Psalm);