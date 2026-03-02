using System.Text.Json;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

namespace WretchedWhispers.Infrastructure.Persistence.Serialization;

public class ArmorTierConverter : JsonConverter<ArmorTier>
{
    public override ArmorTier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var discriminator = doc.RootElement.GetProperty("$type").GetString();
        return discriminator switch
        {
            "heavy" => HeavyArmorTier.Instance,
            "medium" => MediumArmorTier.Instance,
            "light" => LightArmorTier.Instance,
            "none" => NoArmorTier.Instance,
            _ => throw new JsonException($"Unknown armor tier: {discriminator}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ArmorTier value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value switch
        {
            HeavyArmorTier => "heavy",
            MediumArmorTier => "medium",
            LightArmorTier => "light",
            NoArmorTier => "none",
            _ => throw new JsonException($"Unknown armor tier type: {value.GetType()}")
        });
        writer.WriteNumber("defencePenalty", value.DefencePenalty);
        writer.WriteNumber("agilityPenalty", value.AgilityPenalty);
        writer.WriteEndObject();
    }
}
