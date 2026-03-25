using System.Text;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.Services;

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
            if (Character.IsInfected) sb.AppendLine("  Status: INFECTED");
            if (Character.IsDead) sb.AppendLine("  Status: DEAD");
        }

        if (Campaign is not null)
        {
            sb.AppendLine($"Campaign: {Campaign.Name}");
            sb.AppendLine($"  Day {Campaign.CurrentDay}, Hour {Campaign.CurrentHour}");
            sb.AppendLine($"  Miseries: {Campaign.Miseries.Count}/7");
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
