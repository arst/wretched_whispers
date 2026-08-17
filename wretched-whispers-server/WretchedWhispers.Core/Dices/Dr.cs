namespace WretchedWhispers.Core.Dices;

public readonly record struct Dr(int Value)
{
    public static implicit operator Dr(int v)
    {
        return new Dr(v);
    }

    public static implicit operator int(Dr d)
    {
        return d.Value;
    }
}
