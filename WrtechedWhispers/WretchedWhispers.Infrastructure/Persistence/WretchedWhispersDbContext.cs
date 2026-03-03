using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public class WretchedWhispersDbContext : IdentityUserContext<IdentityUser>
{
    public WretchedWhispersDbContext(DbContextOptions<WretchedWhispersDbContext> options)
        : base(options)
    {
    }

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    public DbSet<EncounterEntity> Encounters => Set<EncounterEntity>();
    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WretchedWhispersDbContext).Assembly);
    }
}
