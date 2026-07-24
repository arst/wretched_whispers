using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters.Possessions;

public sealed class Shield
{
    [JsonInclude] public bool IsBroken { get; private set; }

    public void Break()
    {
        IsBroken = true;
    }
}