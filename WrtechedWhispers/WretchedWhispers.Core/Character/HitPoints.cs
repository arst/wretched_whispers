namespace WretchedWhispers.Core.Character;

public readonly record struct HitPoints(int Current, int Max)
{
    public bool IsZero => Current <= 0;

    public HitPoints Heal(int amount)
    {
        return new HitPoints(Math.Min(Current + Math.Max(0, amount), Max), Max);
    }

    public HitPoints Damage(int amount)
    {
        return new HitPoints(Math.Max(0, Current - Math.Max(0, amount)), Max);
    }
}