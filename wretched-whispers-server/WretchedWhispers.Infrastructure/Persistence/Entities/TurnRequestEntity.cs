namespace WretchedWhispers.Infrastructure.Persistence.Entities;

/// <summary>Stored as its name via HasConversion&lt;string&gt; — same column values the string
/// literals used, with compile-time checking at every comparison.</summary>
public enum TurnStatus { Pending, Running, Completed, Failed }

public sealed class TurnRequestEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    public string PlayerMessage { get; set; } = string.Empty;
    public TurnStatus Status { get; set; } = TurnStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TerminalError { get; set; }
}
