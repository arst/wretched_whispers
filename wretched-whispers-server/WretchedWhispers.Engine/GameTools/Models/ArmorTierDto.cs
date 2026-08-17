using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.GameTools.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArmorTierDto
{
    None,
    Light,
    Medium,
    Heavy
}
