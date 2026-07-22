using System.Text;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.Services;

public sealed class SessionContext
{
    public Guid SessionId { get; init; }
    public Guid? CharacterId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? ActiveEncounterId { get; private set; }

    // Domain objects loaded at start of turn
    public Character? Character { get; set; }
    public Campaign? Campaign { get; set; }
    public Encounter? ActiveEncounter { get; set; }

    public SessionStage DeriveStage()
    {
        // Ended takes priority: death, world ended, campaign ended
        if (Character is not null && Character.IsDead)
            return SessionStage.Ended;
        if (Campaign is not null && Campaign.WorldEnded)
            return SessionStage.Ended;
        if (Campaign is not null && Campaign.IsEnded)
            return SessionStage.Ended;

        // No character yet
        if (CharacterId is null)
            return SessionStage.CharacterCreation;

        // Character exists but campaign not active (not started yet)
        if (Campaign is null || !Campaign.IsActive())
            return SessionStage.CampaignSetup;

        // Active encounter = combat
        if (ActiveEncounter is not null && ActiveEncounter.IsStarted && !ActiveEncounter.IsEnded)
            return SessionStage.Combat;

        // Last encounter ended but not resolved = resolution
        if (ActiveEncounter is not null && ActiveEncounter.IsEnded && !ActiveEncounter.IsResolved)
            return SessionStage.Resolution;

        return SessionStage.Exploration;
    }

    // The UI's coarse status is a pure function of the derived stage, so the two can never disagree
    // (a dead character is Ended → "ended", not "in-progress"). Do NOT re-derive status from campaign
    // flags alone: death lives on the Character, and DeriveStage is the single source of terminal truth.
    public static string StatusFor(SessionStage stage) => stage switch
    {
        SessionStage.CharacterCreation => "character-creation",
        SessionStage.Ended => "ended",
        _ => "in-progress"
    };

    // "fallen" is the one status that is not a pure function of the stage: the stage is Ended, but the
    // death is recoverable — the player may bury the wretch and roll a new one. World-ended and an
    // explicitly ended campaign remain terminal.
    public string DeriveStatus()
    {
        var stage = DeriveStage();
        if (stage == SessionStage.Ended
            && Character is { IsDead: true }
            && Campaign is { WorldEnded: false, IsEnded: false })
            return "fallen";
        return StatusFor(stage);
    }

    public void SetCharacterId(Guid id) => CharacterId = id;
    public void SetCampaignId(Guid id) => CampaignId = id;
    public void SetActiveEncounterId(Guid id) => ActiveEncounterId = id;

    public void ClearActiveEncounter()
    {
        ActiveEncounterId = null;
        ActiveEncounter = null;
    }

    public string FormatSnapshot()
    {
        var sb = new StringBuilder();
        if (Character is not null)
        {
            sb.AppendLine($"Character: {Character.Name}");
            sb.AppendLine($"  HP: {Character.Hp.Current}/{Character.Hp.Max}");
            sb.AppendLine($"  Strength: {Character.Abilities.Strength.Modifier}, Agility: {Character.Abilities.Agility.Modifier}");
            sb.AppendLine($"  Presence: {Character.Abilities.Presence.Modifier}, Toughness: {Character.Abilities.Toughness.Modifier}");
            sb.AppendLine($"  Weapon: {Character.Weapon.Kind} ({Character.Weapon.DamageDie})");
            sb.AppendLine($"  Armor: {Character.Armor.Tier.DisplayName()}");
            sb.AppendLine($"  Shield: {(Character.Shield is null ? "none" : Character.Shield.IsBroken ? "broken" : "intact")}");
            sb.AppendLine($"  Silver: {Character.Silver}");
            sb.AppendLine($"  Food: {Character.FoodDays} days");
            sb.AppendLine($"  Powers: {Character.Powers.UsesRemaining}/{Character.Powers.MaxUses}");
            sb.AppendLine($"  Omens: {Character.Omens.Count}");
            sb.AppendLine($"  Inventory ({Character.Inventory.GetFreeSlots()}/{Character.Inventory.MaxCapacity} slots free):");
            foreach (var item in Character.Inventory.InventoryItems)
                sb.AppendLine($"    - {item.Description} x{item.Quantity}");
            foreach (var scroll in Character.Scrolls)
                sb.AppendLine($"    - Scroll: {scroll.Description} ({scroll.School})");
            if (Character.IsInfected) sb.AppendLine("  Status: INFECTED");
            if (Character.IsDead) sb.AppendLine("  Status: DEAD");
        }

        if (Campaign is not null)
        {
            sb.AppendLine($"Campaign: {Campaign.Name}");
            sb.AppendLine($"  Day {Campaign.CurrentDay}, Hour {Campaign.CurrentHour}");
            sb.AppendLine($"  Miseries: {Campaign.Miseries.Count}/7");

            if (Campaign.JournalEntries.Count > 0)
            {
                // ponytail: full injection, cap/retrieval when journals outgrow the context budget
                sb.AppendLine("  Journal:");
                foreach (var entry in Campaign.JournalEntries)
                    sb.AppendLine($"    [Day {entry.Day}, {entry.Category}] {entry.Text}");
            }

            if (Campaign.Pois.Count > 0)
            {
                sb.AppendLine("  Map (0-100 grid, y=0 is north):");
                foreach (var poi in Campaign.Pois)
                    sb.AppendLine($"    - {poi.Name} ({poi.Type}) at ({poi.X},{poi.Y})"
                        + (poi.ConnectedTo is null ? "" : $", path to {poi.ConnectedTo}"));
                if (Campaign.CurrentLocationName is not null)
                    sb.AppendLine($"  Party location: {Campaign.CurrentLocationName}");
            }

            if (Campaign.FallenCharacters.Count > 0)
            {
                sb.AppendLine("  Fallen wretches (dead, gone, unrecoverable):");
                foreach (var f in Campaign.FallenCharacters)
                    sb.AppendLine($"    - {f.Name}, died day {f.DayDied}");
            }
        }

        if (ActiveEncounter is not null)
        {
            sb.AppendLine($"Active Encounter: {ActiveEncounter.Name}");
            sb.AppendLine($"  Living Adversaries: {ActiveEncounter.LivingAdversaries.Count}");
            sb.AppendLine($"  Dead Adversaries: {ActiveEncounter.DeadAdversaries.Count}");
        }

        return sb.ToString();
    }
}
