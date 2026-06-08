using System.ComponentModel;
using WretchedWhispers.Api.GameTools.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Api.GameTools;

/// <summary>
/// Campaign game-master tools. Auto-fills the campaign id from <see cref="SessionContext"/>, validates
/// arguments, and calls the domain directly. Does not expose EndCampaign or GetCampaignById -- ended
/// state is derived from the domain, not driven by the model. Replaces the former
/// CampaignWrapperPlugin → ICampaignOperations → CampaignPluginAdapter → CampaignPlugin stack.
/// </summary>
[Description("Manage the campaign: configure its setting and pace, start it, and advance time.")]
public sealed class CampaignTools(
    ICampaignsRepository campaignsRepository,
    CampaignService campaignService,
    SessionContext sessionContext)
{
    private Guid RequireCampaignId() =>
        sessionContext.CampaignId
        ?? throw new InvalidOperationException("No campaign exists for this session.");

    [Description("Configure the campaign's name, description, and dawn roll pace. The campaign already exists -- this customizes it before starting.")]
    public async Task<CampaignDto> ConfigureCampaign(
        [Description("Dice expression for dawn rolls (e.g., 'd100' for very slow, 'd6' for fast)")]
        string diceExpression,
        [Description("The name of the campaign")] string name,
        [Description("A description of the campaign's setting, goals, or theme")] string description)
    {
        ToolGuard.DiceExpression(diceExpression, nameof(diceExpression));
        var campaign = await RequireCampaign();
        campaign.Configure(DiceExpr.Parse(diceExpression), name, description);
        await campaignsRepository.SaveCampaign(campaign);
        return CreateCampaignDto(campaign);
    }

    [Description("Start the campaign. The character must already be created.")]
    public async Task<CampaignDto> StartCampaign()
    {
        var campaign = await RequireCampaign();
        campaign.Start();
        await campaignsRepository.SaveCampaign(campaign);
        return CreateCampaignDto(campaign);
    }

    [Description("Advance time in the campaign by the specified number of hours")]
    public async Task<AdvanceTimeOutcomeDto> AdvanceTime(
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        var outcome = await campaignService.AdvanceTime(RequireCampaignId(), hours);
        return new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn);
    }

    [Description("Rest for recovery -- characters heal HP and restore magical abilities during the rest period")]
    public async Task<AdvanceTimeOutcomeDto> Rest(
        [Description("The number of hours characters will rest and recover")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        var outcome = await campaignService.AdvanceTimeWithRest(RequireCampaignId(), hours);
        return new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn);
    }

    private async Task<Campaign> RequireCampaign()
    {
        var campaignId = RequireCampaignId();
        return await campaignsRepository.Get(campaignId)
            ?? throw new InvalidOperationException("Campaign not found.");
    }

    private static CampaignDto CreateCampaignDto(Campaign campaign) => new(
        campaign.Id,
        campaign.Name,
        campaign.Description,
        campaign.CurrentDay,
        campaign.CurrentHour,
        campaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
}
