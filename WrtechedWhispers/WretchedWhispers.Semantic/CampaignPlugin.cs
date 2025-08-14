using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Characters.Armor.Tiers;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Encounters;
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
    public async Task<CampaignDto?> GetGameById(
        [Description("The unique identifier of the campaign to load")] Guid campaignId)
    {
        var existingCampaign = await campaignsRepository.GetCampaignById(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return CreateCampaignDto(existingCampaign);
    }

    [KernelFunction]
    [Description(
        "Starts a new campaign with the specified dice expression for dawn rolls. Dawn roll dice is selected by player and rolled each dawn. It determines the length of the campaign." +
        "Examples: d100 - “years of pain”(very slow campaign), d20 - “a bleak half-year”, d10 - “a fall in anguish”, d6 - “a cruel month”, d2 - “the end is nigh!” (very fast) ")]
    public CampaignDto Start(
        [Description("Dice expression for dawn rolls that determines campaign length (e.g., 'd100' for very slow, 'd6' for fast)")] string diceExpression, 
        [Description("The name of the new campaign")] string name, 
        [Description("A description of the campaign's setting, goals, or theme")] string description)
    {
        var dawnDiceExpr = DiceExpr.Parse(diceExpression);
        var newCampaign = Campaign.Create(dawnDiceExpr, name, description);
        campaignsRepository.SaveCampaign(newCampaign);
        return CreateCampaignDto(newCampaign);
    }

    [KernelFunction]
    [Description("Loads an exsiting campaign by id, that is usually provided by player.")]
    public async Task<CampaignDto> Load(
        [Description("The unique identifier of the campaign to load")] Guid campaignId)
    {
        var existingGame = await campaignsRepository.GetCampaignById(campaignId);

        if (existingGame is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return CreateCampaignDto(existingGame);
    }

    [KernelFunction]
    [Description("Advances time in a campaign with id provided by the number of hours provided.")]
    public async Task<CampaignDto> AdvanceTime(
        [Description("The unique identifier of the campaign to advance time in")] Guid campaignId, 
        [Description("The number of hours to advance the campaign time by")] int hours)
    {
        var existingCampaign = await campaignsRepository.GetCampaignById(campaignId);

        if (existingCampaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        existingCampaign.AdvanceTime(hours, randomService);

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
            campaign.Miseries.Select(m => new MiseryDto(m.Code, m.Psalm)).ToList(),
            campaign.Encounters.Select(CreateEncounterDto).ToList());
    }

    private static EncounterDto CreateEncounterDto(Encounter encounter)
    {
        var adversaryDtos = encounter.Adversaries.Select(CreateAdversaryDto).ToList();

        return new EncounterDto
        {
            Id = encounter.Id,
            Name = encounter.Name,
            Description = encounter.Description,
            Adversaries = adversaryDtos,
            LivingAdversaries = encounter.LivingAdversaries.Select(CreateAdversaryDto).ToList(),
            DeadAdversaries = encounter.DeadAdversaries.Select(CreateAdversaryDto).ToList()
        };
    }

    private static AdversaryDto CreateAdversaryDto(Adversary adversary)
    {
        return new AdversaryDto
        {
            Id = adversary.Id,
            Name = adversary.Name,
            CurrentHp = adversary.Hp.Current,
            MaxHp = adversary.Hp.Max,
            ArmorTier = MapArmorTier(adversary.Armor.Tier),
            Morale = adversary.Morale,
            Attack = new AttackProfileDto
            {
                DamageDice = adversary.Attack.DamageDie.ToString(),
                Description = adversary.Attack.Description
            },
            IsDead = adversary.IsDead
        };
    }

    private static ArmorTierDto MapArmorTier(ArmorTier armorTier)
    {
        return armorTier switch
        {
            NoArmorTier => ArmorTierDto.None,
            LightArmorTier => ArmorTierDto.Light,
            MediumArmorTier => ArmorTierDto.Medium,
            HeavyArmorTier => ArmorTierDto.Heavy,
            _ => ArmorTierDto.None
        };
    }
}

