using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters.Possessions;

public sealed class Shield
{
    // ponytail: nothing breaks shields yet, so this stays false — it is already on the wire and in the
    // prompt, so it stays until the break-to-ignore-one-attack rule gives it a writer.
    [JsonInclude] public bool IsBroken { get; private set; }
}
