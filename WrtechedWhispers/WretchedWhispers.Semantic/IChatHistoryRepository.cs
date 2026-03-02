using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace WretchedWhispers.Semantic;

public interface IChatHistoryRepository
{
    Task<ChatHistory?> LoadSession(Guid sessionId, CancellationToken ct = default);
    Task SaveMessage(Guid sessionId, ChatMessageContent message, CancellationToken ct = default);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);
}
