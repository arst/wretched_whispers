namespace WretchedWhispers.Core.Campaigns.World;

public sealed class Misery(string code, string psalm = "")
{
    public string Code { get; } = code;
    public string Psalm { get; } = psalm;
}