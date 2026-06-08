using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.GameTools.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArmorTierDto
{
    None,
    Light,
    Medium,
    Heavy
}