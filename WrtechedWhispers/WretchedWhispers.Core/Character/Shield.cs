namespace WretchedWhispers.Core.Character;

public sealed class Shield
{
    public bool IsBroken { get; private set; }

    public void Break()
    {
        IsBroken = true;
    }
}