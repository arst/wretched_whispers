using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns.World;

public sealed class CalendarOfNechrubel
{
    private readonly List<Misery> _triggeredMiseries = [];

    public IReadOnlyCollection<Misery> Miseries => _triggeredMiseries.AsReadOnly();

    public bool WorldEnded => _triggeredMiseries.Count >= 7;

    public void DawnRoll(DiceExpr dawnDiceExpr)
    {
        if (WorldEnded)
            throw new InvalidOperationException("The world has already ended.");

        var dawnRollResult = Dice.Roll(dawnDiceExpr);

        if (dawnRollResult == 1)
        {
            var guard = 0;
            while (true)
            {
                var miseryIndex = Dice.Roll(DiceExpr.D6) * 10 + Dice.Roll(DiceExpr.D6);
                var picked = new Misery($"{miseryIndex}");
                guard++;
                if (guard >= 100)
                    throw new InvalidOperationException("Too many attempts to pick a misery, something is wrong.");
                if (_triggeredMiseries.All(m => m.Code != picked.Code))
                {
                    _triggeredMiseries.Add(picked);
                    break;
                }
            }
        }
    }
}