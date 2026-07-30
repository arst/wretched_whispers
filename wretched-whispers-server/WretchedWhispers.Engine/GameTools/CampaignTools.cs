using System.ComponentModel;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Campaign game-master tools. Auto-fills the campaign id from <see cref="SessionContext"/>, validates
/// arguments, and calls the domain directly. Does not expose EndCampaign or GetCampaignById -- ended
/// state is derived from the domain, not driven by the model.
/// </summary>
[Description("Manage the campaign: configure its setting and pace, and advance time. The campaign starts automatically once it is configured and a player has joined.")]
public sealed class CampaignTools(
    CampaignService campaignService,
    SessionContext sessionContext)
{
    private Guid RequireCampaignId() =>
        sessionContext.CampaignId
        ?? throw new InvalidOperationException("No campaign exists for this session.");

    [Description("Configure the campaign's name and description. The campaign already exists; it begins automatically once it is configured and the character has been created.")]
    // Not offered in CharacterCreation: that stage is now an unreachable fallback for a session with no
    // character, and it is deliberately toolless.
    [GameTool(SessionStage.CampaignSetup)]
    public async Task<CampaignDto> ConfigureCampaign(
        [Description("The name of the campaign")] string name,
        [Description("A description of the campaign's setting, goals, or theme")] string description)
    {
        var campaign = await campaignService.ConfigureCampaign(RequireCampaignId(), name, description);
        return CreateCampaignDto(campaign);
    }

    [Description("Advance time in the campaign by the specified number of hours")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<AdvanceTimeOutcomeDto> AdvanceTime(
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        var outcome = await campaignService.AdvanceTime(RequireCampaignId(), hours);
        return new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn);
    }

    [Description("Rest for recovery -- characters heal HP and restore magical abilities during the rest period")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<AdvanceTimeOutcomeDto> Rest(
        [Description("The number of hours characters will rest and recover")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        var outcome = await campaignService.AdvanceTimeWithRest(RequireCampaignId(), hours);
        return new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn,
            outcome.OmensRefreshed);
    }

    [Description("Record a lasting fact in the campaign journal — the GM's memory of the fiction. Use it the moment something durable is established: an NPC met, a location discovered, a promise made, a quest taken, or a notable event (a death, a betrayal, a discovery).")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution)]
    public async Task<string> RecordJournalEntry(
        [Description("Kind of fact: 'Npc', 'Location', 'Promise', 'Quest', or 'Event'")]
        JournalCategory category,
        [Description("One concise line stating the fact, e.g. 'Grimlod the flagellant owes the character a lantern'")]
        string text)
    {
        var campaign = await campaignService.RecordJournalEntry(RequireCampaignId(), category, text);
        return $"Recorded. The journal holds {campaign.JournalEntries.Count} entries.";
    }

    [Description("Chart a place on the regional map the first time the fiction establishes it: a town entered, a dungeon found, a landmark sighted. Assign approximate map coordinates consistent with the geography already charted. Does not move the party.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<string> RecordPointOfInterest(
        [Description("Kind of place: 'Town', 'Dungeon', 'Landmark', 'Ruin', or 'Camp'")]
        PoiType type,
        [Description("Unique name of the place")] string name,
        [Description("Approximate west-east position on the map, 0-100 (0 is west)")] int x,
        [Description("Approximate north-south position on the map, 0-100 (0 is north)")] int y,
        [Description("Optional: name of an already-charted place this one connects to by road or trail")]
        string? connectedTo = null)
    {
        var campaign = await campaignService.RecordPointOfInterest(RequireCampaignId(), type, name, x, y, connectedTo);
        return $"Charted. The map holds {campaign.Pois.Count} places.";
    }

    [Description("Mark where the party currently is. Call when the party arrives at a charted place.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<string> SetPartyLocation(
        [Description("Name of an already-charted place")] string locationName)
    {
        var campaign = await campaignService.SetPartyLocation(RequireCampaignId(), locationName);
        return $"The party is now at {campaign.CurrentLocationName}.";
    }

    private static CampaignDto CreateCampaignDto(Campaign campaign) => new(
        campaign.Id,
        campaign.Name,
        campaign.Description,
        campaign.CurrentDay,
        campaign.CurrentHour,
        campaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
}
