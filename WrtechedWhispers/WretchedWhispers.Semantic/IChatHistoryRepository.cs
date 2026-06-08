using Microsoft.Extensions.AI;

namespace WretchedWhispers.Semantic;

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatMessage>?> LoadSession(Guid sessionId, CancellationToken ct = default);
    Task SaveMessage(Guid sessionId, ChatMessage message, CancellationToken ct = default);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);
}
