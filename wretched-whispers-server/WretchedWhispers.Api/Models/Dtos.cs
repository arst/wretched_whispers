using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Engine.Models;

namespace WretchedWhispers.Api.Models;

// Every response body on this API is one of these records. Anonymous types used to cover about half
// the surface, which kept the contract out of the compiler's reach — and out of OpenAPI, and out of
// reach of the hand-written TypeScript that mirrors it.

public sealed record ChatMessageDto(string Role, string? Content, string? AuthorName);

/// <summary>A session IS a campaign — one id, not two. The pair of identical Guids this used to
/// return was a leftover from when the two were meant to diverge.</summary>
public sealed record CreateSessionResponse(Guid SessionId);

public sealed record JournalEntryDto(string Category, string Text, int Day, int Hour);

public sealed record FallenCharacterDto(string Name, int DayDied);

public sealed record SessionJournalDto(
    IReadOnlyList<JournalEntryDto> Entries,
    IReadOnlyList<FallenCharacterDto> Fallen);

public sealed record PoiDto(string Name, string Type, int X, int Y, string? ConnectedTo);

public sealed record SessionMapDto(IReadOnlyList<PoiDto> Pois, string? CurrentLocationName);

public sealed record SessionResumeDto(string? Recap);

/// <summary>The outcome of a lifecycle action (abandon, successor): the session's status afterwards,
/// in the same vocabulary as the list card's status.</summary>
public sealed record SessionStatusDto(string Status);

public sealed record SessionMessagesDto(
    IReadOnlyList<ChatMessageDto> Messages,
    int TotalMessages,
    int Page,
    int PageSize);

public sealed record TurnResponse(Guid TurnId, string? Error);

public sealed record CsrfTokenDto(string Token);

public sealed record CurrentUserDto(string UserId);

public sealed record SettingsDto(string Provider, string Model, string BaseUrl, bool HasKey);

public sealed record SessionPreviewDto(
    Guid SessionId,
    string CampaignName,
    string Description,
    string? CharacterName,
    int? CurrentHp,
    int? MaxHp,
    string Status,
    Difficulty Difficulty,
    DateTime? LastPlayed,
    // Null for classless wretches — the card shows no class line.
    string? CharacterClass);

public sealed record SessionDetailDto(
    Guid SessionId,
    string CampaignName,
    string Description,
    int CurrentDay,
    int CurrentHour,
    string Status,
    Difficulty Difficulty,
    IReadOnlyList<ChatMessageDto> Messages,
    int TotalMessages,
    int Page,
    int PageSize,
    StateUpdate State,
    bool RecapDue);
