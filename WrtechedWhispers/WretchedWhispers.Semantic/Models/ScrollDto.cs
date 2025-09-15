using System.ComponentModel;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Posessions.Scrolls;

namespace WretchedWhispers.Semantic.Models;

public class ScrollDto
{
    [JsonPropertyName("School")]
    [Description("School of magic the scroll belongs to")]
    public ScrollSchool School { get; set; }

    [JsonPropertyName("Key")]
    [Description("Unique key identifying the specific scroll")]
    public string Key { get; set; } = string.Empty;
}