using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignMapTests
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    [Fact]
    public void RecordPointOfInterest_StampsCampaignDay()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");

        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);

        var poi = Assert.Single(campaign.Pois);
        Assert.Equal(PoiType.Town, poi.Type);
        Assert.Equal(48, poi.X);
        Assert.Equal(30, poi.Y);
        Assert.Null(poi.ConnectedTo);
        Assert.Equal(campaign.CurrentDay, poi.Day);
    }

    [Fact]
    public void RecordPointOfInterest_ClampsCoordinatesToGrid()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");

        campaign.RecordPointOfInterest(PoiType.Ruin, "Edge of the World", -20, 150);

        var poi = Assert.Single(campaign.Pois);
        Assert.Equal(0, poi.X);
        Assert.Equal(100, poi.Y);
    }

    [Fact]
    public void RecordPointOfInterest_EmptyName_Throws()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        Assert.Throws<ArgumentException>(() => campaign.RecordPointOfInterest(PoiType.Camp, "  ", 10, 10));
    }

    [Fact]
    public void RecordPointOfInterest_DuplicateName_ThrowsCaseInsensitive()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);

        Assert.Throws<ArgumentException>(() => campaign.RecordPointOfInterest(PoiType.Ruin, "galgenbeck", 10, 10));
    }

    [Fact]
    public void RecordPointOfInterest_ConnectionStoresCanonicalName()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);

        campaign.RecordPointOfInterest(PoiType.Dungeon, "Rot-Black Sludge", 60, 42, "galgenbeck");

        Assert.Equal("Galgenbeck", campaign.Pois[1].ConnectedTo);
    }

    [Fact]
    public void RecordPointOfInterest_UnknownConnection_Throws()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        Assert.Throws<ArgumentException>(() =>
            campaign.RecordPointOfInterest(PoiType.Dungeon, "Rot-Black Sludge", 60, 42, "Nowhere"));
    }

    [Fact]
    public void SetPartyLocation_KnownPoi_SetsCanonicalName()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);

        campaign.SetPartyLocation("GALGENBECK");

        Assert.Equal("Galgenbeck", campaign.CurrentLocationName);
    }

    [Fact]
    public void SetPartyLocation_UnknownPoi_Throws()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        Assert.Throws<ArgumentException>(() => campaign.SetPartyLocation("Nowhere"));
    }

    [Fact]
    public void Map_SurvivesJsonRoundTrip()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 48, 30);
        campaign.RecordPointOfInterest(PoiType.Dungeon, "Rot-Black Sludge", 60, 42, "Galgenbeck");
        campaign.SetPartyLocation("Galgenbeck");

        var json = JsonSerializer.Serialize(campaign, _options);
        var restored = JsonSerializer.Deserialize<Campaign>(json, _options);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Pois.Count);
        Assert.Equal("Galgenbeck", restored.Pois[1].ConnectedTo);
        Assert.Equal("Galgenbeck", restored.CurrentLocationName);
    }

    [Fact]
    public void OldCampaignBlobWithoutMapFields_DeserializesToEmptyMap()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        var json = JsonSerializer.Serialize(campaign, _options);

        // Simulate a blob persisted before the map feature existed.
        using var doc = JsonDocument.Parse(json);
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (prop.Name is not ("pointsOfInterest" or "currentLocationName"))
                stripped[prop.Name] = prop.Value;
        var oldJson = JsonSerializer.Serialize(stripped, _options);

        var restored = JsonSerializer.Deserialize<Campaign>(oldJson, _options);

        Assert.NotNull(restored);
        Assert.Empty(restored.Pois);
        Assert.Null(restored.CurrentLocationName);
    }
}
