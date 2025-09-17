namespace WretchedWhispers.Core.Characters.Possessions;

public sealed class Shield
{
    public bool IsBroken { get; private set; }

    public void Break()
    {
        IsBroken = true;
    }
}