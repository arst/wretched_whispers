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
    int PageSize
);
