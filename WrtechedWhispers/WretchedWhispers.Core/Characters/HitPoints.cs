namespace WretchedWhispers.Core.Characters;

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

    /// <summary>MORK BORG "Getting Better": only the maximum grows; current HP is untouched (rest heals).</summary>
    public HitPoints IncreaseMax(int amount)
    {
        return this with { Max = Max + Math.Max(0, amount) };
    }
}