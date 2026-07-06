using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignJournalTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    [Fact]
    public void RecordJournalEntry_StampsCampaignDayAndHour()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");

        campaign.RecordJournalEntry(JournalCategory.Npc, "Grimlod the flagellant, met at the gallows");

        var entry = Assert.Single(campaign.JournalEntries);
        Assert.Equal(JournalCategory.Npc, entry.Category);
        Assert.Equal(campaign.CurrentDay, entry.Day);
        Assert.Equal(campaign.CurrentHour, entry.Hour);
    }

    [Fact]
    public void RecordJournalEntry_EmptyText_Throws()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");
        Assert.Throws<ArgumentException>(() => campaign.RecordJournalEntry(JournalCategory.Event, "  "));
    }

    [Fact]
    public void Journal_SurvivesJsonRoundTrip()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");
        campaign.RecordJournalEntry(JournalCategory.Promise, "Swore to bring the hangman's rope by dawn");

        // Round-trip with the same serializer setup the campaign repository uses.
        var json = JsonSerializer.Serialize(campaign, _options);
        var restored = JsonSerializer.Deserialize<Campaign>(json, _options);

        Assert.NotNull(restored);
        var entry = Assert.Single(restored.JournalEntries);
        Assert.Equal("Swore to bring the hangman's rope by dawn", entry.Text);
    }
}
