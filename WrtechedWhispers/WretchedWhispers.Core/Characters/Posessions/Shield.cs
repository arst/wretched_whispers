namespace WretchedWhispers.Core.Characters.Posessions;

public sealed class Shield
{
    public bool IsBroken { get; private set; }

    public void Break()
    {
        IsBroken = true;
    }
}