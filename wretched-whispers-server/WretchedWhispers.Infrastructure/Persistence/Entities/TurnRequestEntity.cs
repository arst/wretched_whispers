namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public sealed class TurnRequestEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string PlayerMessage { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TerminalError { get; set; }
}
