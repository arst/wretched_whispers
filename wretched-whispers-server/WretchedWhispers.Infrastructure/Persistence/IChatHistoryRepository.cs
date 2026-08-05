using Microsoft.Extensions.AI;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>Rolling summary of a chat session: the text and how many leading messages it covers.</summary>
public sealed record ChatSummary(string Text, int CoveredCount);
public sealed record ChatRecap(string Text, DateTime ActivityAt);

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatMessage>?> LoadSession(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// One page of a chronicle plus its total length, paged in the database. <see cref="LoadSession"/>
    /// materialises the entire history — which is what the turn loop needs, and what a paged read
    /// must never do. Null when the chronicle does not exist.
    /// </summary>
    Task<(IReadOnlyList<ChatMessage> Messages, int Total)?> LoadSessionPage(
        Guid sessionId, int skip, int take, CancellationToken ct = default);

    Task SaveMessage(Guid sessionId, ChatMessage message, CancellationToken ct = default, Guid? turnId = null);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);

    /// <summary>
    /// The campaign's active chronicle — the newest one, belonging to the living wretch. Null when
    /// the campaign has none yet. Every caller that reached for
    /// <see cref="GetSessionsForCampaign"/> only ever wanted this.
    /// </summary>
    Task<Guid?> GetActiveChronicle(Guid campaignId, CancellationToken ct = default);

    Task<DateTime?> GetLastActivity(Guid campaignId, CancellationToken ct = default);

    /// <summary>
    /// Last activity for many campaigns in one query, keyed by campaign id. The session list needs
    /// it for every card, and one <see cref="GetLastActivity"/> per campaign made listing N
    /// campaigns cost N round trips.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActivityForCampaigns(
        IReadOnlyCollection<Guid> campaignIds, CancellationToken ct = default);
    Task<DateTime?> GetSessionLastActivity(Guid sessionId, CancellationToken ct = default);
    Task<DateTime?> GetLastOpened(Guid sessionId, CancellationToken ct = default);
    Task MarkOpened(Guid sessionId, DateTime openedAt, CancellationToken ct = default);
    Task<ChatRecap?> GetRecap(Guid sessionId, CancellationToken ct = default);
    Task SaveRecap(Guid sessionId, ChatRecap recap, CancellationToken ct = default);
    Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default);
    Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default);
}
