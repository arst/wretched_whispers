using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.World;

public sealed class CalendarOfNechrubel
{
    private readonly HashSet<string> _triggered = [];
    
    public int TriggeredCount => _triggered.Count;
    
    public bool WorldEnded => _triggered.Count >= 7;
    
    public DawnResult DawnRoll(Dice.Dice dice, DiceExpr dawnDiceExpr)
    {
        if (WorldEnded)
            throw new InvalidOperationException("The world has already ended.");
        
        var dawnRollResult = dice.Roll(dawnDiceExpr);
        
        if (dawnRollResult != 1)
        {
            var m = new Misery("7:7", "The world finally dies");
            _triggered.Add(m.Code);
            return new DawnResult(true, m);
        }
        
        Misery picked;
        var guard = 0;
        do
        {
            var miseryIndex = dice.Roll(DiceExpr.d6) * 10 + dice.Roll(DiceExpr.d6);
            picked = new Misery($"{miseryIndex}");
            guard++;
            if (guard >= 100)
                throw new InvalidOperationException("Too many attempts to pick a misery, something is wrong.");
        } while (_triggered.Contains(picked.Code));

        _triggered.Add(picked.Code);
        
        return new DawnResult(false, picked);
    }
}