using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Wraps CampaignPlugin to auto-fill campaignId/characterId from SessionContext and add guardrails.
/// The model never sees GUID parameters -- IDs are resolved from session state.
/// Does NOT expose EndCampaign or GetCampaignById per D-13 -- ended state is derived from domain.
/// </summary>
[Description("Manage the campaign: create it, add the character, start it, and advance time.")]
public sealed class CampaignWrapperPlugin(ICampaignOperations inner, SessionContext sessionContext)
{
    private Guid RequireCampaignId() =>
        sessionContext.CampaignId
        ?? throw new InvalidOperationException("No campaign created yet -- call CreateCampaign first.");

    [KernelFunction]
    [Description("Create a new campaign with a dawn roll dice expression that determines campaign length")]
    public async Task<CampaignDto> CreateCampaign(
        [Description("Dice expression for dawn rolls (e.g., 'd100' for very slow, 'd6' for fast)")]
        string diceExpression,
        [Description("The name of the new campaign")] string name,
        [Description("A description of the campaign's setting, goals, or theme")] string description)
    {
        var result = await inner.CreateCampaign(diceExpression, name, description);
        sessionContext.SetCampaignId(result.Id);
        return result;
    }

    [KernelFunction]
    [Description("Add the character to the campaign")]
    public async Task AddCharacterToCampaign()
    {
        var campaignId = RequireCampaignId();
        var characterId = sessionContext.CharacterId
            ?? throw new InvalidOperationException("No character created yet -- call CreateCharacter first.");

        await inner.AddCharacterToCampaign(campaignId, characterId);
    }

    [KernelFunction]
    [Description("Start the campaign. Characters must have joined it first.")]
    public async Task<CampaignDto> StartCampaign()
    {
        return await inner.StartCampaign(RequireCampaignId());
    }

    [KernelFunction]
    [Description("Advance time in the campaign by the specified number of hours")]
    public async Task<AdvanceTimeOutcomeDto> AdvanceTime(
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        return await inner.AdvanceTime(RequireCampaignId(), hours);
    }

    [KernelFunction]
    [Description("Rest for recovery -- characters heal HP and restore magical abilities during the rest period")]
    public async Task<AdvanceTimeOutcomeDto> Rest(
        [Description("The number of hours characters will rest and recover")] int hours)
    {
        return await inner.Rest(RequireCampaignId(), hours);
    }
}
