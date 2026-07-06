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

    /// <summary>
    /// Rolls the dawn die. On a 1 a new Misery is triggered and returned; otherwise returns null.
    /// The Misery carries a factual psalm line stating its place in the descent (the seventh ends the
    /// world) — the domain states the fact; the narrator dresses it in horror.
    /// </summary>
    public Misery? DawnRoll(DiceExpr dawnDiceExpr, Dice dice)
    {
        if (WorldEnded)
            throw new InvalidOperationException("The world has already ended.");

        var dawnRollResult = dice.Roll(dawnDiceExpr);
        if (dawnRollResult != 1)
            return null;

        var ordinal = TriggeredMiseries.Count + 1; // 1..7; the seventh completes the Calendar and ends the world
        var guard = 0;
        while (true)
        {
            var miseryIndex = dice.Roll(DiceExpr.D6) * 10 + dice.Roll(DiceExpr.D6);
            var picked = new Misery($"{miseryIndex}", MiseryPsalm(ordinal));
            guard++;
            if (guard >= 100)
                throw new InvalidOperationException("Too many attempts to pick a misery, something is wrong.");
            if (TriggeredMiseries.All(m => m.Code != picked.Code))
            {
                TriggeredMiseries.Add(picked);
                return picked;
            }
        }
    }

    private static readonly string[] Ordinals =
        ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh"];

    private static string MiseryPsalm(int ordinal)
    {
        var name = ordinal >= 1 && ordinal <= Ordinals.Length ? Ordinals[ordinal - 1] : $"{ordinal}th";
        return ordinal >= 7
            ? "The Seventh and final Misery of the Calendar of Nechrubel is read — the Calendar is complete and the world ends."
            : $"The {name} Misery of the Calendar of Nechrubel befalls the dying world.";
    }
}