using System.ComponentModel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Api.GameTools.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Wraps CampaignPlugin to auto-fill campaignId/characterId from SessionContext and add guardrails.
/// The model never sees GUID parameters -- IDs are resolved from session state.
/// Does NOT expose EndCampaign or GetCampaignById per D-13 -- ended state is derived from domain.
/// </summary>
[Description("Manage the campaign: configure its setting and pace, start it, and advance time.")]
public sealed class CampaignWrapperPlugin(
    ICampaignOperations inner,
    ICampaignsRepository campaignsRepository,
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
        var campaignId = RequireCampaignId();
        var campaign = await campaignsRepository.Get(campaignId)
            ?? throw new InvalidOperationException("Campaign not found.");

        campaign.Configure(DiceExpr.Parse(diceExpression), name, description);
        await campaignsRepository.SaveCampaign(campaign);

        return new CampaignDto(
            campaign.Id, campaign.Name, campaign.Description,
            campaign.CurrentDay, campaign.CurrentHour,
            campaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }

    [Description("Start the campaign. The character must already be created.")]
    public async Task<CampaignDto> StartCampaign()
    {
        return await inner.StartCampaign(RequireCampaignId());
    }

    [Description("Advance time in the campaign by the specified number of hours")]
    public async Task<AdvanceTimeOutcomeDto> AdvanceTime(
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        return await inner.AdvanceTime(RequireCampaignId(), hours);
    }

    [Description("Rest for recovery -- characters heal HP and restore magical abilities during the rest period")]
    public async Task<AdvanceTimeOutcomeDto> Rest(
        [Description("The number of hours characters will rest and recover")] int hours)
    {
        ToolGuard.Positive(hours, nameof(hours), "at least 1 hour");
        return await inner.Rest(RequireCampaignId(), hours);
    }
}
