using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description("Plugin to create new campaigns and load existing campaigns.")]
public class GamePlugin(
    IRandomService randomService,
    ICampaignsRepository campaignsRepository,
    ICharacterCreationService characterCreationService)
{
    [KernelFunction]
    [Description("Loads an existing campaign by its ID.")]
    public async Task<CampaignDto?> GetGameById(Guid campaignId)
    {
        var existingCampaign = await campaignsRepository.GetCampaignById(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return new CampaignDto(existingCampaign.Id, existingCampaign.Name, existingCampaign.Description,
            existingCampaign.CurrentDay, existingCampaign.CurrentHour,
            existingCampaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }

    [KernelFunction]
    [Description(
        "Starts a new campaign with the specified dice expression for dawn rolls. Dawn roll dice is selected by player and rolled each dawn. It determines the length of the campaign." +
        "Examples: d100 - “years of pain”(very slow campaign), d20 - “a bleak half-year”, d10 - “a fall in anguish”, d6 - “a cruel month”, d2 - “the end is nigh!” (very fast) ")]
    public CampaignDto Start(string diceExpression, string name, string description)
    {
        var dawnDiceExpr = DiceExpr.Parse(diceExpression);
        var newCampaign = Campaign.Create(dawnDiceExpr, name, description);
        campaignsRepository.SaveCampaign(newCampaign);
        return new CampaignDto(newCampaign.Id, newCampaign.Name, newCampaign.Description, newCampaign.CurrentDay,
            newCampaign.CurrentHour,
            newCampaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }

    [KernelFunction]
    [Description("Loads an exsiting campaign by id, that is usually provided by player.")]
    public async Task<CampaignDto> Load(Guid campaignId)
    {
        var existingGame = await campaignsRepository.GetCampaignById(campaignId);

        if (existingGame is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return new CampaignDto(existingGame.Id, existingGame.Name, existingGame.Description, existingGame.CurrentDay,
            existingGame.CurrentHour,
            existingGame.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }

    [KernelFunction]
    [Description("Advances time in a campaign with id provided by the number of hours provided.")]
    public async Task<CampaignDto> AdvanceTime(Guid campaignId, int hours)
    {
        var existingCampaign = await campaignsRepository.GetCampaignById(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        existingCampaign.AdvanceTime(hours, randomService);

        return new CampaignDto(existingCampaign.Id, existingCampaign.Name, existingCampaign.Description,
            existingCampaign.CurrentDay, existingCampaign.CurrentHour,
            existingCampaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList());
    }
}