namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public sealed class TurnEventEntity
{
    public Guid Id { get; set; }
    public Guid TurnId { get; set; }
    public long Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
