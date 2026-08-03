using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public class WretchedWhispersDbContext : IdentityUserContext<IdentityUser>, IDataProtectionKeyContext
{
    public WretchedWhispersDbContext(DbContextOptions<WretchedWhispersDbContext> options)
        : base(options)
    {
    }

    // For PostgresWwDbContext, whose own DbContextOptions<PostgresWwDbContext> must reach the base.
    protected WretchedWhispersDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    public DbSet<EncounterEntity> Encounters => Set<EncounterEntity>();
    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<TurnTraceEntity> TurnTraces => Set<TurnTraceEntity>();

    // ASP.NET data-protection key ring, persisted so Identity bearer/refresh tokens survive
    // restarts and are readable by every instance sharing the database.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WretchedWhispersDbContext).Assembly);
    }
}
