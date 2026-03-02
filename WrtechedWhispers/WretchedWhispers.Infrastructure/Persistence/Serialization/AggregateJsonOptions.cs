using System.Text.Json;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Infrastructure.Persistence.Serialization;

public static class AggregateJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
            Converters =
            {
                new ArmorTierConverter(),
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
        return options;
    }
}
