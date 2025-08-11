namespace WretchedWhispers.Core.Character;

public readonly record struct HitPoints(int Current, int Max)
{
    public bool IsZero => Current <= 0;

    public HitPoints Heal(int amount)
    {
        return this with { Current = Math.Min(Current + Math.Max(0, amount), Max) };
    }

    public HitPoints Damage(int amount)
    {
        return this with { Current = Math.Max(0, Current - Math.Max(0, amount)) };
    }
}