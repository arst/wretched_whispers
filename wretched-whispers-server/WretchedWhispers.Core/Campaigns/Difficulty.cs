using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>How punishing a session is. Chosen at creation, stored on the Campaign. String-serialized
/// so it round-trips readably in the campaign JSON blob and over the HTTP API.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Difficulty>))]
public enum Difficulty
{
    StoryMode,
    Grim,
    Doomed,
    Hardcore
}
