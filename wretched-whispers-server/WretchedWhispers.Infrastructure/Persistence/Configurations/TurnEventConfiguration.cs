using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public sealed class TurnEventConfiguration : IEntityTypeConfiguration<TurnEventEntity>
{
    public void Configure(EntityTypeBuilder<TurnEventEntity> builder)
    {
        builder.ToTable("TurnEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Payload).IsRequired().HasColumnType("TEXT");
        builder.HasIndex(x => new { x.TurnId, x.Sequence }).IsUnique();
    }
}
