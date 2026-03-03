namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public class CampaignEntity
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}
