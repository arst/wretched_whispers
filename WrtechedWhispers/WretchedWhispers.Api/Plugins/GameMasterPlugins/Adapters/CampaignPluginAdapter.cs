using WretchedWhispers.Semantic;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;

/// <summary>
/// Adapts CampaignPlugin to ICampaignOperations.
/// </summary>
public sealed class CampaignPluginAdapter(CampaignPlugin inner) : ICampaignOperations
{
    public Task<CampaignDto> CreateCampaign(string diceExpression, string name, string description) =>
        inner.CreateCampaign(diceExpression, name, description);

    public Task AddCharacterToCampaign(Guid campaignId, Guid characterId) =>
        inner.AddCharacterToCampaign(campaignId, characterId);

    public Task<CampaignDto> StartCampaign(Guid campaignId) => inner.StartCampaign(campaignId);

    public Task<AdvanceTimeOutcomeDto> AdvanceTime(Guid campaignId, int hours) => inner.AdvanceTime(campaignId, hours);

    public Task<AdvanceTimeOutcomeDto> Rest(Guid campaignId, int hours) => inner.Rest(campaignId, hours);
}
