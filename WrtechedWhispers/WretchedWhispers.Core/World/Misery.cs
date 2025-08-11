namespace WretchedWhispers.Core.World;

public sealed class Misery
{
    public Misery(string code, string? psalm = null)
    {
        Code = code;
        Psalm = psalm;
    }

    public string Code { get; }
    public string? Psalm { get; }
}