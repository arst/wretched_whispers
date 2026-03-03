using System.Text.Json;

namespace WretchedWhispers.Api.Models;

public record SseEvent(string EventType, object Data)
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string JsonData => JsonSerializer.Serialize(Data, CamelCaseOptions);
}
