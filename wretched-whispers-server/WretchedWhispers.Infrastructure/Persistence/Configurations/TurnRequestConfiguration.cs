using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public sealed class TurnRequestConfiguration : IEntityTypeConfiguration<TurnRequestEntity>
{
    public void Configure(EntityTypeBuilder<TurnRequestEntity> builder)
    {
        builder.ToTable("TurnRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.PlayerMessage).IsRequired().HasColumnType("TEXT");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(16).HasConversion<string>();
        builder.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
