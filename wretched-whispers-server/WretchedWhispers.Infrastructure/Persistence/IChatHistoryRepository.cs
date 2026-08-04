using Microsoft.Extensions.AI;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>Rolling summary of a chat session: the text and how many leading messages it covers.</summary>
public sealed record ChatSummary(string Text, int CoveredCount);
public sealed record ChatRecap(string Text, DateTime ActivityAt);

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatMessage>?> LoadSession(Guid sessionId, CancellationToken ct = default);
    Task SaveMessage(Guid sessionId, ChatMessage message, CancellationToken ct = default, Guid? turnId = null);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);
    Task<DateTime?> GetLastActivity(Guid campaignId, CancellationToken ct = default);
    Task<DateTime?> GetSessionLastActivity(Guid sessionId, CancellationToken ct = default);
    Task<DateTime?> GetLastOpened(Guid sessionId, CancellationToken ct = default);
    Task MarkOpened(Guid sessionId, DateTime openedAt, CancellationToken ct = default);
    Task<ChatRecap?> GetRecap(Guid sessionId, CancellationToken ct = default);
    Task SaveRecap(Guid sessionId, ChatRecap recap, CancellationToken ct = default);
    Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default);
    Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default);
}
