namespace WretchedWhispers.Core.Characters.Inventory;

public sealed class Shield
{
    public bool IsBroken { get; private set; }

    public void Break()
    {
        IsBroken = true;
    }
}