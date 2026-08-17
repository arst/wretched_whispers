using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Infrastructure;

public sealed class SeededRandomService(int? seed = null) : IRandomService
{
    private readonly Random _rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;

    public int GenerateRandomRoll(int sides)
    {
        return _rng.Next(sides);
    }
}
