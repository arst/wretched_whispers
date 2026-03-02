using System.Text.Json.Serialization;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns.World;

public sealed class CalendarOfNechrubel
{
    [JsonConstructor]
    public CalendarOfNechrubel(List<Misery>? triggeredMiseries = null)
    {
        TriggeredMiseries = triggeredMiseries ?? [];
    }

    [JsonInclude] internal List<Misery> TriggeredMiseries { get; }

    [JsonIgnore] public IReadOnlyCollection<Misery> Miseries => TriggeredMiseries.AsReadOnly();

    [JsonIgnore] public bool WorldEnded => TriggeredMiseries.Count >= 7;

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
                if (TriggeredMiseries.All(m => m.Code != picked.Code))
                {
                    TriggeredMiseries.Add(picked);
                    break;
                }
            }
        }
    }
}