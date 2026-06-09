using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public class CampaignEntityConfiguration : IEntityTypeConfiguration<CampaignEntity>
{
    public void Configure(EntityTypeBuilder<CampaignEntity> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Data).IsRequired().HasColumnType("TEXT");

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(450);
        builder.HasIndex(e => e.UserId);

        // Optimistic concurrency: the original Version value is matched in the UPDATE's WHERE clause.
        // SqliteCampaignsRepository rotates it on every save (SQLite has no native rowversion).
        builder.Property(e => e.Version).IsConcurrencyToken();
    }
}
