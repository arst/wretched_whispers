namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? AuthorName { get; set; }
    public string? ItemsJson { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime Timestamp { get; set; }
    public int OrderIndex { get; set; }

    public ChatSessionEntity Session { get; set; } = null!;
}
