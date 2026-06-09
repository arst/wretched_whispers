namespace WretchedWhispers.Infrastructure.Persistence.Entities;

public class CampaignEntity
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Optimistic-concurrency token. Rotated on every save; EF includes the original value in the
    /// UPDATE's WHERE clause, so a turn that commits against a stale value throws
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>. This is the
    /// cross-instance backstop for two overlapping turns on one session (the in-memory
    /// SessionConcurrencyGuard only fast-fails within a single process). SQLite has no native
    /// rowversion, so the token is a Guid rotated by the repository rather than the database.
    /// </summary>
    public Guid Version { get; set; }
}
