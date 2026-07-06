using Microsoft.Extensions.AI;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>Rolling summary of a chat session: the text and how many leading messages it covers.</summary>
public sealed record ChatSummary(string Text, int CoveredCount);

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatMessage>?> LoadSession(Guid sessionId, CancellationToken ct = default);
    Task SaveMessage(Guid sessionId, ChatMessage message, CancellationToken ct = default);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);
    Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default);
    Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default);
}
