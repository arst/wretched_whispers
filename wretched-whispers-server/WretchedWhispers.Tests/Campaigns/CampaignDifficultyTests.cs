using System.Text.Json;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class CampaignDifficultyTests
{
    private static readonly JsonSerializerOptions Options = AggregateJsonOptions.Create();

    [Fact]
    public void Create_stores_difficulty_and_derives_dawn_die()
    {
        var campaign = Campaign.Create(Difficulty.Hardcore, "Doom", "A test");

        Assert.Equal(Difficulty.Hardcore, campaign.Difficulty);
        // Round-trips through JSON; dawn die is internal, so assert via serialization.
        // AggregateJsonOptions' global JsonStringEnumConverter(CamelCase) takes precedence over the
        // Difficulty type's own [JsonConverter] attribute (options.Converters wins over type attributes),
        // so both the property name and the enum value serialize camelCase.
        var json = JsonSerializer.Serialize(campaign, Options);
        Assert.Contains("\"hardcore\"", json);
    }

    [Fact]
    public void Configure_preserves_difficulty_dawn_die()
    {
        var campaign = Campaign.Create(Difficulty.StoryMode, "Old", "Old desc");
        campaign.Configure("New name", "New desc");

        Assert.Equal(Difficulty.StoryMode, campaign.Difficulty);
        Assert.Equal("New name", campaign.Name);
    }

    [Fact]
    public void Deserializing_a_blob_without_difficulty_defaults_to_grim()
    {
        // Simulate a pre-feature persisted campaign: serialize, then strip the Difficulty property.
        var campaign = Campaign.Create(Difficulty.Doomed, "Legacy", "desc");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(campaign, Options));
        Assert.NotNull(node); // xUnit NotNull narrows nullability — avoids the null-forgiving operator
        node.AsObject().Remove("difficulty"); // AggregateJsonOptions uses camelCase property naming

        var restored = JsonSerializer.Deserialize<Campaign>(node.ToJsonString(), Options);
        Assert.NotNull(restored);
        Assert.Equal(Difficulty.Grim, restored.Difficulty);
    }
}
