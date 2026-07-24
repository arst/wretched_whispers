using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public class EncounterEntityConfiguration : IEntityTypeConfiguration<EncounterEntity>
{
    public void Configure(EntityTypeBuilder<EncounterEntity> builder)
    {
        builder.ToTable("Encounters");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Data).IsRequired().HasColumnType("TEXT");
    }
}
