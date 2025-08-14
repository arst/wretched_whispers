using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Armor;
using WretchedWhispers.Core.Characters.Armor.Tiers;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description("Allow interactions with encounters in the game: start them, finish them, perform actions and so on.")]
public class EncounterPlugin(ICampaignsRepository campaignsRepository)
{
    [KernelFunction]
    [Description("Start a new encounter in the specified campaign")]
    public async Task<EncounterDto> Start(
        [Description("The unique identifier of the campaign to start the encounter in")] Guid campaignId, 
        [Description("The name of the encounter")] string name, 
        [Description("A description of the encounter setting, circumstances, or narrative context")] string description)
    {
        var campaign = await campaignsRepository.GetCampaignById(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        var encounter = campaign.StartEncounter(name, description);

        return new EncounterDto
        {
            Id = encounter.Id,
            Name = encounter.Name,
            Description = encounter.Description,
            Adversaries = [],
            LivingAdversaries = [],
            DeadAdversaries = []
        };
    }

    [KernelFunction]
    [Description("Add an adversary (enemy) to an existing encounter")]
    public async Task<EncounterDto> AddAdversary(
        [Description("The unique identifier of the campaign containing the encounter")] Guid campaignId, 
        [Description("The unique identifier of the encounter to add the adversary to")] Guid encounterId, 
        [Description("The adversary details including name, hit points, armor, morale, and attack information")] AddAdversaryDto adversary)
    {
        var campaign = await campaignsRepository.GetCampaignById(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        var encounter = campaign.Encounters.Single(e => e.Id == encounterId);

        if (encounter is null)
            throw new ArgumentException($"Encounter with {encounterId} doesn't exist in campaign {campaignId}.");

        var a = new Adversary(
            adversary.Name,
            new HitPoints(adversary.HitPoints, adversary.HitPoints),
            GenerateArmor(adversary.ArmorType),
            adversary.Morale,
            new AttackProfile(adversary.AttackDescription, DiceExpr.Parse(adversary.DamageDice)));

        encounter.AddAdversary(a);

        await campaignsRepository.SaveCampaign(campaign);

        return new EncounterDto
        {
            Id = encounter.Id,
            Name = encounter.Name,
            Description = encounter.Description,
            Adversaries = encounter.Adversaries.Select(e => new AdversaryDto
            {
                Name = e.Name,
                CurrentHp = e.Hp.Current,
                MaxHp = e.Hp.Max,
                ArmorTier = GetArmorTier(e.Armor.Tier),
                Morale = e.Morale,
                Attack = new AttackProfileDto
                {
                    Description = e.Attack.Description,
                    DamageDice = e.Attack.DamageDie.ToString()
                }
            }).ToList(),
            LivingAdversaries = [],
            DeadAdversaries = []
        };
    }
    
    private static ArmorTierDto GetArmorTier(ArmorTier armorTier)
    {
        return armorTier switch
        {
            LightArmorTier => ArmorTierDto.Light,
            MediumArmorTier => ArmorTierDto.Medium,
            HeavyArmorTier => ArmorTierDto.Heavy,
            NoArmorTier => ArmorTierDto.None,
            _ => throw new ArgumentException($"Unknown armor tier: {armorTier}.")
        };
    }

    private static Armor GenerateArmor(string armorType)
    {
        return armorType switch
        {
            "light" => new Armor(LightArmorTier.Instance),
            "medium" => new Armor(MediumArmorTier.Instance),
            "heavy" => new Armor(HeavyArmorTier.Instance),
            "none" => new Armor(NoArmorTier.Instance),
            _ => throw new ArgumentException(
                $"Unknown armor type: {armorType}. Expected one of: light, medium, heavy, none.")
        };
    }
}

