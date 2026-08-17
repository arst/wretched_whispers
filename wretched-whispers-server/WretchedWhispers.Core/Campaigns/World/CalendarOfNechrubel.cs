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

    // Original doom-metal verses (NOT the copyrighted MÖRK BORG rulebook psalms) — one per step of the
    // descent toward the end, escalating in dread; the seventh unmakes the world. The domain hands the
    // narrator the verse; the narrator renders the horror in the scene.
    private static readonly string[] Psalms =
    [
        "The First Misery of the Calendar of Nechrubel is read: the sun sickens and no longer warms the stones, and frost creeps into the marrow of the living.",
        "The Second Misery is read: the wells turn black and brackish, and what crawls up from them at night is not water.",
        "The Third Misery is read: the beasts turn upon their keepers, and the crows grow fat and bold upon the faithful.",
        "The Fourth Misery is read: a plague of weeping sores walks the roads, and the dead lie unburied for want of living hands.",
        "The Fifth Misery is read: the stars fall from their sockets one by one, and the night keeps no more mercy than the day.",
        "The Sixth Misery is read: the earth splits open and the old buried things climb free, ravenous after their long sleep.",
        "The Seventh and final Misery is read: the last psalm is spoken and the world is unmade — nothing remains but the dark and the end of all things.",
    ];

    private static string MiseryPsalm(int ordinal) =>
        ordinal >= 1 && ordinal <= Psalms.Length
            ? Psalms[ordinal - 1]
            : $"A Misery of the Calendar of Nechrubel befalls the dying world.";
}
