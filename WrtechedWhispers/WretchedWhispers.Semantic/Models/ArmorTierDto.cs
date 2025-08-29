using System.Text.Json.Serialization;

namespace WretchedWhispers.Semantic.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArmorTierDto
{
    None,
    Light,
    Medium,
    Heavy
}