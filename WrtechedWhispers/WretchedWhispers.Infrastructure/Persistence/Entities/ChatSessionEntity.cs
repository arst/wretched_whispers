namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public class ChatSessionEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public DateTime StartedAt { get; set; }

    public List<ChatMessageEntity> Messages { get; set; } = [];
}
