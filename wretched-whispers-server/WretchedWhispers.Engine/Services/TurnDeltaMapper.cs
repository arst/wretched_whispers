using WretchedWhispers.Engine.Models;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Computes the authoritative per-turn <see cref="TurnDelta"/> as a pure diff of two
/// <see cref="StateUpdate"/> snapshots — the domain state before the turn and after commit. No model
/// output is consulted, so the result cannot be fabricated; a turn that narrated an outcome without
/// calling the tool that applies it diffs to zero.
/// </summary>
public static class TurnDeltaMapper
{
    public static TurnDelta Compute(StateUpdate before, StateUpdate after)
    {
        // Inventory is a multiset of item descriptions: added = after − before, removed = before − after,
        // so buying a second torch shows "+Torch" and using one of two shows "−Torch".
        var itemsAdded = MultisetDifference(after.CharacterInventory, before.CharacterInventory);
        var itemsRemoved = MultisetDifference(before.CharacterInventory, after.CharacterInventory);

        var afflictions = new List<string>();
        AddIfGained(afflictions, "Lost eye", before.HasLostEye, after.HasLostEye);
        AddIfGained(afflictions, "Stabbed lung", before.HasStabbedLung, after.HasStabbedLung);
        AddIfGained(afflictions, "Broken hand", before.HasBrokenHand, after.HasBrokenHand);
        AddIfGained(afflictions, "Crushed foot", before.HasCrushedFoot, after.HasCrushedFoot);
        AddIfGained(afflictions, "Severed arm", before.HasSeveredArm, after.HasSeveredArm);
        AddIfGained(afflictions, "Smashed face", before.HasSmashedFace, after.HasSmashedFace);
        AddIfGained(afflictions, "Infected", before.IsInfected, after.IsInfected);
        AddIfGained(afflictions, "Shield broken", before.IsShieldBroken, after.IsShieldBroken);
        // Crossing the carry limit silently costs +2 DR on every Strength and Agility test — including
        // every attack and every dodge — so the turn that picks up one item too many has to say so.
        AddIfGained(afflictions, "Encumbered (DR +2 Strength/Agility)", before.IsEncumbered, after.IsEncumbered);

        return new TurnDelta(
            SilverChange: (after.CharacterSilver ?? 0) - (before.CharacterSilver ?? 0),
            HpChange: (after.CharacterHp ?? 0) - (before.CharacterHp ?? 0),
            ItemsAdded: itemsAdded,
            ItemsRemoved: itemsRemoved,
            HoursElapsed: TotalHours(after) - TotalHours(before),
            StrengthChange: (after.CharacterStrength ?? 0) - (before.CharacterStrength ?? 0),
            AgilityChange: (after.CharacterAgility ?? 0) - (before.CharacterAgility ?? 0),
            PresenceChange: (after.CharacterPresence ?? 0) - (before.CharacterPresence ?? 0),
            ToughnessChange: (after.CharacterToughness ?? 0) - (before.CharacterToughness ?? 0),
            MiseryChange: after.MiseryCount - before.MiseryCount,
            NewAfflictions: afflictions.ToArray(),
            Died: !before.IsDead && after.IsDead,
            WorldEnded: !before.WorldEnded && after.WorldEnded);
    }

    private static int TotalHours(StateUpdate s) => s.CurrentDay * 24 + s.CurrentHour;

    private static void AddIfGained(List<string> into, string label, bool before, bool after)
    {
        if (!before && after) into.Add(label);
    }

    // Elements in `from` that are not accounted for by `subtract`, honouring duplicate counts.
    private static string[] MultisetDifference(string[]? from, string[]? subtract)
    {
        if (from is null || from.Length == 0) return [];
        if (subtract is null || subtract.Length == 0) return from.ToArray();

        var counts = new Dictionary<string, int>();
        foreach (var item in subtract)
            counts[item] = counts.GetValueOrDefault(item) + 1;

        var result = new List<string>();
        foreach (var item in from)
        {
            if (counts.GetValueOrDefault(item) > 0)
                counts[item]--;
            else
                result.Add(item);
        }

        return result.ToArray();
    }
}
