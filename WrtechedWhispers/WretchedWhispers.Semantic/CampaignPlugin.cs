using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description("Plugin to create new campaigns and load existing campaigns.")]
public class CampaignPlugin(
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository,
    CampaignService campaignService)
{
    [KernelFunction]
    [Description(
        "Creates a new campaign with the specified dice expression for dawn rolls. Dawn roll dice is selected by player and rolled each dawn. It determines the length of the campaign." +
        "Examples: d100 - “years of pain”(very slow campaign), d20 - “a bleak half-year”, d10 - “a fall in anguish”, d6 - “a cruel month”, d2 - “the end is nigh!” (very fast) ")]
    public CampaignDto CreateCampaign(
        [Description(
            "Dice expression for dawn rolls that determines campaign length (e.g., 'd100' for very slow, 'd6' for fast)")]
        string diceExpression,
        [Description("The name of the new campaign")]
        string name,
        [Description("A description of the campaign's setting, goals, or theme")]
        string description)
    {
        var dawnDiceExpr = DiceExpr.Parse(diceExpression);
        var newCampaign = Campaign.Create(dawnDiceExpr, name, description);
        campaignsRepository.SaveCampaign(newCampaign);
        return CreateCampaignDto(newCampaign);
    }

    [KernelFunction]
    [Description("Adds a character to an existing campaign. The character must already exist in the repository.")]
    public async Task AddCharacterToCampaign(
        [Description("The unique identifier of the campaign to join")]
        Guid campaignId,
        [Description("The name of the character to join the campaign with")]
        Guid characterId)
    {
        var existingCampaign = await campaignsRepository.Get(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        var existingCharacter = await charactersRepository.Get(characterId);

        if (existingCharacter is null) throw new ArgumentException($"Character with ID {characterId} does not exist.");

        existingCampaign.JoinGame(existingCharacter.Id);

        await campaignsRepository.SaveCampaign(existingCampaign);
    }

    [KernelFunction]
    [Description(
        "Starts a campaign with the specified ID. The campaign must already exist and characters must have joined it.")]
    public async Task<CampaignDto> StartCampaign(Guid campaignId)
    {
        var existingCampaign = await campaignsRepository.Get(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        existingCampaign.Start();
        await campaignsRepository.SaveCampaign(existingCampaign);

        return CreateCampaignDto(existingCampaign);
    }

    [KernelFunction]
    [Description("Advances time in a campaign with id provided by the number of hours provided.")]
    public async Task<AdvanceTimeOutcomeDto> AdvanceTime(
        [Description("The unique identifier of the campaign to advance time in")]
        Guid campaignId,
        [Description("The number of hours to advance the campaign time by")]
        int hours)
    {
        var outcome = await campaignService.AdvanceTime(campaignId, hours);

        var existingCampaign = await campaignsRepository.Get(campaignId);

        return existingCampaign is null
            ? throw new ArgumentException($"Campaign with {campaignId} doesn't exist.")
            : new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn);
    }

    [KernelFunction]
    [Description("Advances time in a campaign while characters deliberately rest to recover health and powers. Unlike AdvanceTime which is used for passive time progression during game actions or events, Rest is a deliberate character action where they choose to rest for recovery. Characters will heal HP and restore magical abilities during the rest period.")]
    public async Task<AdvanceTimeOutcomeDto> Rest(
        [Description("The unique identifier of the campaign to advance time in while resting")]
        Guid campaignId,
        [Description("The number of hours characters will rest and recover")]
        int hours)
    {
        var outcome = await campaignService.AdvanceTimeWithRest(campaignId, hours);

        var existingCampaign = await campaignsRepository.Get(campaignId);

        return existingCampaign is null
            ? throw new ArgumentException($"Campaign with {campaignId} doesn't exist.")
            : new AdvanceTimeOutcomeDto(outcome.Miseries, outcome.IsWorldEnded, outcome.IsNewDawn);
    }

    [KernelFunction]
    [Description("Ends a campaign with the specified ID. The campaign must already exist and started.")]
    public async Task<CampaignDto> EndCampaign(Guid campaignId)
    {
        var existingCampaign = await campaignsRepository.Get(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        existingCampaign.End();
        await campaignsRepository.SaveCampaign(existingCampaign);

        return CreateCampaignDto(existingCampaign);
    }

    [KernelFunction]
    [Description("Loads an existing campaign by its ID.")]
    public async Task<CampaignDto?> GetCampaignById(
        [Description("The unique identifier of the campaign to load")]
        Guid campaignId)
    {
        var existingCampaign = await campaignsRepository.Get(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return CreateCampaignDto(existingCampaign);
    }

    private static CampaignDto CreateCampaignDto(Campaign campaign)
    {
        return new CampaignDto(
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.CurrentDay,
            campaign.CurrentHour,
            campaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }
}