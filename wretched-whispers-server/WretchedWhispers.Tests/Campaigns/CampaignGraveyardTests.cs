using System.Text.Json;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignGraveyardTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    private static Campaign StartedCampaignWith(Guid characterId)
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.JoinGame(characterId);
        campaign.Start();
        return campaign;
    }

    [Fact]
    public void BuryCharacter_MovesPlayerToGraveyard_StampedWithCurrentDay()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);

        campaign.BuryCharacter(characterId, "Grimnir");

        Assert.Empty(campaign.Players);
        var fallen = Assert.Single(campaign.FallenCharacters);
        Assert.Equal(characterId, fallen.Id);
        Assert.Equal("Grimnir", fallen.Name);
        Assert.Equal(campaign.CurrentDay, fallen.DayDied);
    }

    [Fact]
    public void BuryCharacter_RecordsJournalEntry()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);

        campaign.BuryCharacter(characterId, "Grimnir");

        Assert.Contains(campaign.JournalEntries,
            e => e.Category == JournalCategory.Event && e.Text.Contains("Grimnir"));
    }

    [Fact]
    public void BuryCharacter_UnknownId_Throws()
    {
        var campaign = StartedCampaignWith(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => campaign.BuryCharacter(Guid.NewGuid(), "Nobody"));
    }

    [Fact]
    public void Graveyard_RoundTripsThroughJson()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);
        campaign.BuryCharacter(characterId, "Grimnir");

        var json = JsonSerializer.Serialize(campaign, _options);
        var restored = JsonSerializer.Deserialize<Campaign>(json, _options);

        Assert.NotNull(restored);
        var fallen = Assert.Single(restored.FallenCharacters);
        Assert.Equal("Grimnir", fallen.Name);
    }

    [Fact]
    public void Campaign_DeserializesFromBlobWithoutFallenField()
    {
        // Backward compat: blobs persisted before the graveyard existed must still load.
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        var json = JsonSerializer.Serialize(campaign, _options);
        using var doc = JsonDocument.Parse(json);
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (prop.Name != "fallen")
                stripped[prop.Name] = prop.Value;
        var legacyJson = JsonSerializer.Serialize(stripped);

        var restored = JsonSerializer.Deserialize<Campaign>(legacyJson, _options);

        Assert.NotNull(restored);
        Assert.Empty(restored.FallenCharacters);
    }
}
