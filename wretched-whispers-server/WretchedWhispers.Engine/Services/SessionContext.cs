using System.Text;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
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

    /// <summary>
    /// Assembles the context needed to answer "what stage is this session in?" from aggregates the
    /// caller already holds. The list view needs that answer for many campaigns at once and cannot
    /// afford <see cref="ISessionContextLoader"/>'s per-session round trips; without this it
    /// hand-rolled the same wiring out at the API boundary, where a forgotten SetCharacterId would
    /// silently change the derived stage. <paramref name="characterId"/> is separate from
    /// <paramref name="character"/> on purpose: a campaign whose player row failed to load is still
    /// past character creation.
    /// </summary>
    public static SessionContext For(Campaign campaign, Guid? characterId, Character? character)
    {
        var context = new SessionContext { Campaign = campaign, Character = character };
        context.SetCampaignId(campaign.Id);
        if (characterId is { } id && id != Guid.Empty)
            context.SetCharacterId(id);
        return context;
    }

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
            // Omitted for classless wretches, so snapshots for characters created before classes existed
            // stay byte-identical.
            if (Character.Class != CharacterClass.Classless)
                sb.AppendLine($"  Class: {ClassPresets.For(Character.Class).DisplayName}");
            sb.AppendLine($"  HP: {Character.Hp.Current}/{Character.Hp.Max}");
            sb.AppendLine($"  Strength: {Character.Abilities.Strength.Modifier}, Agility: {Character.Abilities.Agility.Modifier}");
            sb.AppendLine($"  Presence: {Character.Abilities.Presence.Modifier}, Toughness: {Character.Abilities.Toughness.Modifier}");
            sb.AppendLine($"  Weapon: {Character.Weapon.Kind} ({Character.Weapon.DamageDie})");
            sb.AppendLine($"  Armor: {Character.Armor.Tier.DisplayName()}");
            sb.AppendLine($"  Shield: {(Character.Shield is null ? "none" : Character.Shield.IsBroken ? "broken" : "intact")}");
            sb.AppendLine($"  Silver: {Character.Silver}");
            sb.AppendLine($"  Food: {Character.FoodDays} days");
            // "Powers" is only the daily budget for casting scrolls (Character.Cast spends one). There is
            // no other way to spend it, so showing the counter to a character with no scrolls just invites
            // the model to narrate a power that no tool can apply.
            if (Character.Scrolls.Count > 0)
                sb.AppendLine(
                    $"  Scroll castings left today: {Character.Powers.UsesRemaining}/{Character.Powers.MaxUses}");
            sb.AppendLine($"  Omens: {Character.Omens.Count}");
            sb.AppendLine($"  Inventory ({Character.Inventory.GetFreeSlots()}/{Character.Inventory.MaxCapacity} slots free):");
            foreach (var item in Character.Inventory.InventoryItems)
                sb.AppendLine($"    - {item.Description} x{item.Quantity}");
            foreach (var scroll in Character.Scrolls)
                sb.AppendLine($"    - Scroll: {scroll.Description} ({scroll.School})");
            // Named with its cost: the +2 DR is applied silently inside Challenge, so without this the
            // model cannot explain a miss and invents a reason instead.
            if (Character.IsEncumbered)
                sb.AppendLine("  Status: ENCUMBERED (over carry limit: +2 DR on all Strength and Agility tests)");
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
            sb.AppendLine($"  Disposition: {ActiveEncounter.CurrentType}"
                + (ActiveEncounter.Reaction is null
                    ? ""
                    : $" (reaction roll {ActiveEncounter.ReactionRoll} — {ActiveEncounter.Reaction})"));
            sb.AppendLine($"  Living Adversaries: {ActiveEncounter.LivingAdversaries.Count}");
            sb.AppendLine($"  Dead Adversaries: {ActiveEncounter.DeadAdversaries.Count}");
        }

        return sb.ToString();
    }
}
