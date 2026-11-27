using System.ComponentModel;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Campaigns.Time;
using WretchedWhispers.Core.Campaigns.World;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;

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

    [Description("Configure the campaign's name and description, and name the place where the character wakes. That place goes on the map with the party in it. The campaign already exists; it begins automatically once it is configured and the character has been created.")]
    // Not offered in CharacterCreation: that stage is now an unreachable fallback for a session with no
    // character, and it is deliberately toolless.
    [GameTool(SessionStage.CampaignSetup)]
    public async Task<CampaignDto> ConfigureCampaign(
        [Description("The name of the campaign")] string name,
        [Description("A description of the campaign's setting, goals, or theme")] string description,
        [Description("Name of the place the opening scene is set in, as you will describe it: the town, ruin or landmark the character wakes in or beside")]
        string startingPlaceName,
        [Description("Kind of place: 'Town', 'Dungeon', 'Landmark', 'Ruin', or 'Camp'")]
        PoiType startingPlaceType)
    {
        var campaign = await campaignService.ConfigureCampaign(
            RequireCampaignId(), name, description, startingPlaceType, startingPlaceName);
        // The campaign has begun: the rest of this turn is play, and its tools are checked against that.
        sessionContext.Campaign = campaign;
        return CreateCampaignDto(campaign);
    }

    [Description("Advance time in the campaign by the specified number of hours. Call it whenever the story spends time where the party is: a search, a long talk, a wait. A journey passes its own hours through SetPartyLocation. The doom of the world runs on this clock. The result gives the day and hour it is now: tell those and no others.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<AdvanceTimeOutcome> AdvanceTime(
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        return await campaignService.AdvanceTime(RequireCampaignId(), hours);
    }

    [Description("Rest for recovery: time passes and the character heals -- d4 hit points, or d6 and a chance to refill spent omens for a full night of 8 hours or more. An infected character heals nothing and takes damage instead, which can break or kill. The result says what happened: hpHealed, damageTaken, currentHp, newInjury, isDead. Narrate those numbers and nothing else about the character's health.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<AdvanceTimeOutcome> Rest(
        [Description("The number of hours characters will rest and recover")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        return await campaignService.AdvanceTimeWithRest(RequireCampaignId(), hours);
    }

    [Description("Record a lasting fact in the campaign journal — the GM's memory of the fiction. Use it the moment something durable is established: an NPC met, a location discovered, a promise made, a quest taken, or a notable event (a death, a betrayal, a discovery).")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution, AllowedAfterTurnEnd = true)]
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
    [GameTool(SessionStage.Exploration, SessionStage.Resolution, AllowedAfterTurnEnd = true)]
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

    [Description("Move the party to a charted place and pass the hours the journey took. Call when the party arrives somewhere. The clock advances by those hours, so do not also call AdvanceTime for the same journey. The result says what the passing time brought (a new dawn, a misery, the end of the world) and the day and hour it is now.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution, AllowedAfterTurnEnd = true)]
    public async Task<AdvanceTimeOutcome> SetPartyLocation(
        [Description("Name of an already-charted place")] string locationName,
        [Description("Hours the journey there took, at least 1")] int hoursTravelled)
    {
        ToolGuard.Positive(hoursTravelled, nameof(hoursTravelled), "a journey takes at least 1 hour");
        return await campaignService.TravelTo(RequireCampaignId(), locationName, hoursTravelled);
    }

    private static CampaignDto CreateCampaignDto(Campaign campaign) => new(
        campaign.Name,
        campaign.Description,
        campaign.CurrentDay,
        campaign.CurrentHour,
        campaign.Miseries.ToList());
}
