using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Engine.Models;

namespace WretchedWhispers.Api.Models;

public record ChatMessageDto(string Role, string? Content, string? AuthorName);

public record CreateSessionResponse(Guid SessionId, Guid CampaignId);

public record JournalEntryDto(string Category, string Text, int Day, int Hour);

public record PoiDto(string Name, string Type, int X, int Y, string? ConnectedTo);

public record SessionResumeDto(string? Recap);

public sealed record SubmitTurnRequest(Guid RequestId, string Message);

public sealed record TurnResponse(Guid TurnId, string? Error);

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

public record SessionDetailDto(
    Guid SessionId,
    Guid CampaignId,
    string CampaignName,
    string Description,
    int CurrentDay,
    int CurrentHour,
    string Status,
    Difficulty Difficulty,
    List<ChatMessageDto> Messages,
    int TotalMessages,
    int Page,
    int PageSize,
    StateUpdate State,
    bool RecapDue
);
