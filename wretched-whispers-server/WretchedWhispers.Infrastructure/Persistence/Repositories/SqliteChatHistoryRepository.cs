using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteChatHistoryRepository : IChatHistoryRepository
{
    private readonly WretchedWhispersDbContext _db;

    // Microsoft.Extensions.AI provides polymorphic (de)serialization for AIContent
    // (TextContent / FunctionCallContent / FunctionResultContent / ...).
    private static readonly JsonSerializerOptions ContentJsonOptions = AIJsonUtilities.DefaultOptions;

    public SqliteChatHistoryRepository(WretchedWhispersDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default)
    {
        var session = new ChatSessionEntity
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            StartedAt = DateTime.UtcNow
        };

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default)
    {
        // Newest-first: the head of the list is the ACTIVE chronicle (one chat session per wretch).
        return await _db.ChatSessions
            .Where(s => s.CampaignId == campaignId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetLastActivity(Guid campaignId, CancellationToken ct = default)
    {
        var sessionIds = _db.ChatSessions
            .Where(s => s.CampaignId == campaignId)
            .Select(s => s.Id);

        var lastMessage = await _db.ChatMessages
            .Where(m => sessionIds.Contains(m.SessionId))
            .MaxAsync(m => (DateTime?)m.Timestamp, ct);

        // A session with no messages yet still counts as activity (just created).
        return lastMessage ?? await _db.ChatSessions
            .Where(s => s.CampaignId == campaignId)
            .MaxAsync(s => (DateTime?)s.StartedAt, ct);
    }

    public async Task SaveMessage(Guid sessionId, ChatMessage message, CancellationToken ct = default)
    {
        var orderIndex = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .CountAsync(ct);

        var entity = new ChatMessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = message.Role.Value,
            Content = message.Text,
            AuthorName = message.AuthorName,
            ItemsJson = SerializeContents(message),
            MetadataJson = SerializeMetadata(message.AdditionalProperties),
            Timestamp = DateTime.UtcNow,
            OrderIndex = orderIndex
        };

        _db.ChatMessages.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>?> LoadSession(Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.ChatSessions
            .AnyAsync(s => s.Id == sessionId, ct);

        if (!sessionExists)
            return null;

        var entities = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.OrderIndex)
            .ToListAsync(ct);

        var history = new List<ChatMessage>(entities.Count);

        foreach (var entity in entities)
        {
            var role = new ChatRole(entity.Role);
            var contents = DeserializeContents(entity.ItemsJson);

            var message = contents is { Count: > 0 }
                ? new ChatMessage(role, contents)
                : new ChatMessage(role, entity.Content);

            message.AuthorName = entity.AuthorName;
            ApplyMetadata(message, entity.MetadataJson);

            history.Add(message);
        }

        return history;
    }

    /// <summary>
    /// Persist the full content list only when it carries non-text content (function calls/results).
    /// Plain-text messages are reconstructed from the Content column, so serializing their single
    /// TextContent would be redundant.
    /// </summary>
    private static string? SerializeContents(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
            return null;

        var hasNonTextContent = message.Contents.Any(c => c is not TextContent);
        if (!hasNonTextContent)
            return null;

        return JsonSerializer.Serialize(message.Contents, ContentJsonOptions);
    }

    private static IList<AIContent>? DeserializeContents(string? itemsJson)
    {
        if (string.IsNullOrEmpty(itemsJson))
            return null;

        return JsonSerializer.Deserialize<IList<AIContent>>(itemsJson, ContentJsonOptions);
    }

    private static string? SerializeMetadata(AdditionalPropertiesDictionary? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        return JsonSerializer.Serialize(metadata, ContentJsonOptions);
    }

    public async Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        return session?.SummaryText is null
            ? null
            : new ChatSummary(session.SummaryText, session.SummaryCoveredCount);
    }

    public async Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default)
    {
        var session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Chat session {sessionId} not found");
        session.SummaryText = summary.Text;
        session.SummaryCoveredCount = summary.CoveredCount;
        await _db.SaveChangesAsync(ct);
    }

    private static void ApplyMetadata(ChatMessage message, string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson))
            return;

        var metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson, ContentJsonOptions);
        if (metadata is null || metadata.Count == 0)
            return;

        message.AdditionalProperties = new AdditionalPropertiesDictionary(metadata);
    }
}
