using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChatMessageEntity> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasIndex(e => e.SessionId);
        builder.Property(e => e.Role).IsRequired();
        builder.Property(e => e.Content).HasColumnType("TEXT");
        builder.Property(e => e.ItemsJson).HasColumnType("TEXT");
        builder.Property(e => e.MetadataJson).HasColumnType("TEXT");
    }
}
