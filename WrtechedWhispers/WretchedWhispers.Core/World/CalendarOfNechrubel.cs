namespace WretchedWhispers.Core.World;

public sealed class CalendarOfNechrubel
{
    private readonly HashSet<string> _triggered = new();
    public int TriggeredCount => _triggered.Count;
    public bool WorldEnded => _triggered.Count >= 7; // 7:7

    public Misery? DawnRoll(IRandomService rng, Func<int, int> dieChooser)
    {
        if (WorldEnded) return null;

        // If the die chosen yields 1, a Misery is activated
        var die = dieChooser.Invoke(TriggeredCount); // app decides which die to use today
        var roll = rng.D(die);
        if (roll != 1) return null;

        if (TriggeredCount == 6)
        {
            // 7th misery must be 7:7 — world ends
            var m = new Misery("7:7", "The world finally dies");
            _triggered.Add(m.Code);
            return m;
        }

        // roll d66 for a new misery; ensure uniqueness
        Misery picked;
        var guard = 0;
        do
        {
            var d66 = rng.D(6) * 10 + rng.D(6);
            picked = new Misery($"{d66}");
            guard++;
        } while (_triggered.Contains(picked.Code) && guard < 100);

        _triggered.Add(picked.Code);
        return picked;
    }
}