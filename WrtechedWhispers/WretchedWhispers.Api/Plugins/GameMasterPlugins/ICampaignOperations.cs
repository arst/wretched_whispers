using WretchedWhispers.Api.GameTools.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Contract for campaign operations that wrapper plugins delegate to.
/// Implemented by CampaignPlugin via an adapter.
/// </summary>
public interface ICampaignOperations
{
    Task<CampaignDto> CreateCampaign(string diceExpression, string name, string description);
    Task AddCharacterToCampaign(Guid campaignId, Guid characterId);
    Task<CampaignDto> StartCampaign(Guid campaignId);
    Task<AdvanceTimeOutcomeDto> AdvanceTime(Guid campaignId, int hours);
    Task<AdvanceTimeOutcomeDto> Rest(Guid campaignId, int hours);
}
