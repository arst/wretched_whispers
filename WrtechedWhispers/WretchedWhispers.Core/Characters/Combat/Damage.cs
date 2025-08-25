namespace WretchedWhispers.Core.Characters.Combat;

public readonly record struct Damage(int Amount)
{
    public static Damage Zero => new(0);

    public static Damage From(int a)
    {
        return new Damage(Math.Max(0, a));
    }

    public static Damage operator +(Damage a, Damage b)
    {
        return new Damage(a.Amount + b.Amount);
    }
}