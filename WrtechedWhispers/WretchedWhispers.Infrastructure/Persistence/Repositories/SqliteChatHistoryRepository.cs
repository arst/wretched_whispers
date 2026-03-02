using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteChatHistoryRepository : IChatHistoryRepository
{
    private readonly WretchedWhispersDbContext _db;
    private static readonly JsonSerializerOptions ItemsJsonOptions = CreateItemsJsonOptions();

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
        return await _db.ChatSessions
            .Where(s => s.CampaignId == campaignId)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task SaveMessage(Guid sessionId, ChatMessageContent message, CancellationToken ct = default)
    {
        var orderIndex = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .CountAsync(ct);

        var entity = new ChatMessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = message.Role.Label,
            Content = message.Content,
            AuthorName = message.AuthorName,
            ItemsJson = SerializeItems(message),
            MetadataJson = SerializeMetadata(message.Metadata),
            Timestamp = DateTime.UtcNow,
            OrderIndex = orderIndex
        };

        _db.ChatMessages.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChatHistory?> LoadSession(Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.ChatSessions
            .AnyAsync(s => s.Id == sessionId, ct);

        if (!sessionExists)
            return null;

        var entities = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.OrderIndex)
            .ToListAsync(ct);

        if (entities.Count == 0)
            return new ChatHistory();

        var history = new ChatHistory();

        foreach (var entity in entities)
        {
            var role = new AuthorRole(entity.Role);
            var message = new ChatMessageContent(role, entity.Content)
            {
                AuthorName = entity.AuthorName
            };

            DeserializeItemsInto(message, entity.ItemsJson);
            DeserializeMetadataInto(message, entity.MetadataJson);

            history.Add(message);
        }

        return history;
    }

    private static string? SerializeItems(ChatMessageContent message)
    {
        if (message.Items is null || message.Items.Count == 0)
            return null;

        // Only serialize if there are non-text items, or if there's only a TextContent
        // that differs from the Content property (indicating Items was explicitly set)
        var hasNonTextItems = false;
        foreach (var item in message.Items)
        {
            if (item is not TextContent)
            {
                hasNonTextItems = true;
                break;
            }
        }

        if (!hasNonTextItems)
            return null;

        return JsonSerializer.Serialize(message.Items, ItemsJsonOptions);
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        return JsonSerializer.Serialize(metadata, ItemsJsonOptions);
    }

    private static void DeserializeItemsInto(ChatMessageContent message, string? itemsJson)
    {
        if (string.IsNullOrEmpty(itemsJson))
            return;

        var items = JsonSerializer.Deserialize<List<KernelContent>>(itemsJson, ItemsJsonOptions);
        if (items is null)
            return;

        message.Items.Clear();
        foreach (var item in items)
        {
            message.Items.Add(item);
        }
    }

    private static void DeserializeMetadataInto(ChatMessageContent message, string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson))
            return;

        var metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson, ItemsJsonOptions);
        if (metadata is null)
            return;

        foreach (var kvp in metadata)
        {
            message.Metadata ??= new Dictionary<string, object?>();
            ((Dictionary<string, object?>)message.Metadata)[kvp.Key] = kvp.Value;
        }
    }

    private static JsonSerializerOptions CreateItemsJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return options;
    }
}
