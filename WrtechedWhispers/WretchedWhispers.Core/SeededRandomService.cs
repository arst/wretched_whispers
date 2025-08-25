namespace WretchedWhispers.Core;

public sealed class SeededRandomService : IRandomService
{
    private readonly Random _rng;

    public SeededRandomService(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public int GenerateRandomRoll(int sides)
    {
        return 1 + _rng.Next(sides);
    }
}