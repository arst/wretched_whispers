using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns.World;

public sealed class CalendarOfNechrubel
{
    private readonly HashSet<string> _triggered = [];

    private readonly List<Misery> _triggeredMiseries = [];

    public IReadOnlyCollection<Misery> Miseries => _triggeredMiseries.AsReadOnly();

    public bool WorldEnded => _triggered.Count >= 7;

    public void DawnRoll(DiceExpr dawnDiceExpr)
    {
        if (WorldEnded)
            throw new InvalidOperationException("The world has already ended.");

        var dawnRollResult = Dice.Roll(dawnDiceExpr);

        if (dawnRollResult != 1)
        {
            var m = new Misery("7:7", "The world finally dies");
            _triggered.Add(m.Code);
            _triggeredMiseries.Add(m);
        }

        Misery picked;
        var guard = 0;
        do
        {
            var miseryIndex = Dice.Roll(DiceExpr.D6) * 10 + Dice.Roll(DiceExpr.D6);
            picked = new Misery($"{miseryIndex}");
            guard++;
            if (guard >= 100)
                throw new InvalidOperationException("Too many attempts to pick a misery, something is wrong.");
        } while (_triggered.Contains(picked.Code));

        _triggered.Add(picked.Code);
        _triggeredMiseries.Add(picked);
    }
}