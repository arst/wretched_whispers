using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Configurations;

public class TurnTraceConfiguration : IEntityTypeConfiguration<TurnTraceEntity>
{
    public void Configure(EntityTypeBuilder<TurnTraceEntity> builder)
    {
        builder.ToTable("TurnTraces");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasIndex(e => e.ChatSessionId);
        builder.HasIndex(e => e.CampaignId);
        builder.Property(e => e.Stage).IsRequired();
        builder.Property(e => e.PlayerMessage).HasColumnType("TEXT");
        builder.Property(e => e.GameStateJson).HasColumnType("TEXT");
        builder.Property(e => e.ToolCallsJson).HasColumnType("TEXT");
        builder.Property(e => e.ToolResultsJson).HasColumnType("TEXT");
        builder.Property(e => e.TurnDeltaJson).HasColumnType("TEXT");
        builder.Property(e => e.SuppressedNarrative).HasColumnType("TEXT");
        builder.Property(e => e.Narrative).HasColumnType("TEXT");
    }
}
