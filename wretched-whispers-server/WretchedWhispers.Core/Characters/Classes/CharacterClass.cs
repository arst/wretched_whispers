using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters.Classes;

/// <summary>What kind of wretch this is. Chosen (or rolled) at creation, stored on the Character.
/// String-serialized so it round-trips readably in the character JSON blob and over the HTTP API.
/// <para>
/// <see cref="Classless"/> is 0 on purpose: every character saved before classes existed deserializes
/// to it, and <see cref="ClassPresets"/> maps it to the original classless-scum numbers.
/// </para></summary>
[JsonConverter(typeof(JsonStringEnumConverter<CharacterClass>))]
public enum CharacterClass
{
    Classless = 0,
    FangedDeserter,
    GutterbornScum,
    EsotericHermit,
    OccultHerbmaster,
    HereticalPriest,
    CursedSkinwalker
}
