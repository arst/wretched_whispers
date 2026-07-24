using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Campaigns.World;

public sealed class Misery
{
    [JsonConstructor]
    public Misery(string code, string psalm = "")
    {
        Code = code;
        Psalm = psalm;
    }

    public string Code { get; }
    public string Psalm { get; }
}