namespace WretchedWhispers.Core.Character;

public sealed class Omens
{
    public Omens(int count)
    {
        Count = Math.Max(0, count);
    }

    public int Count { get; private set; }

    public bool TrySpend()
    {
        if (Count <= 0) return false;
        Count--;
        return true;
    }

    public void Refill(int n)
    {
        Count += Math.Max(0, n);
    }
}