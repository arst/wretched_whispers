using System.ComponentModel;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description("Allow interactions with encounters in the game: start them, finish them, perform actions and so on.")]
public class EncounterPlugin(EncounterService encounterService, IEncountersRepository repository, Dice dice)
{
    [Description("Create a new encounter with the specified name, description, and initial type. ")]
    public async Task<EncounterDto> CreateEncounter(
        [Description("The name of the encounter")]
        string name,
        [Description("A description of the encounter setting, circumstances, or narrative context")]
        string description,
        [Description(
            "Initial type for the encounter. One of: Friendly, Hostile, Unknown. For Unknown(encounters that are not clearly hostile or friendly from the start) initial reaction will be rolled to set encounter type to Hostile or Friendly.")]
        string initialEncounterType)
    {
        _ = Enum.TryParse(initialEncounterType, out EncounterType type)
            ? type
            : throw new ArgumentException(
                $"Encounter type {initialEncounterType} is not valid. Expected one of: Friendly, Hostile, Unknown.");

        var encounter = Encounter.Create(name, description, type, dice);
        await repository.Save(encounter);

        return CreateEncounterDto(encounter);
    }

    [Description(
        "Add adversary to an encounter with the specified name, description, and initial type. Each encounter usually have multiple adversaries.")]
    public async Task<EncounterDto> AddAdversaryToEncounter(Guid encounterId, NewAdversaryDto adversary)
    {
        await encounterService.AddAdversaries(
            encounterId,
            [
                new Adversary(
                    adversary.Name,
                    new HitPoints(adversary.HitPoints, adversary.HitPoints),
                    GenerateArmor(adversary.ArmorTier),
                    adversary.Morale,
                    new AttackProfile(
                        adversary.WeaponDescription,
                        DiceExpr.Parse(adversary.WeaponDamageDie))
                )
            ]);
        var encounter = await repository.Get(encounterId) ?? throw new InvalidOperationException("Encounter not found");

        return CreateEncounterDto(encounter);
    }

    [Description("Start a new encounter in the specified campaign")]
    public async Task<EncounterDto> StartEncounter(Guid encounterId)
    {
        var encounter = await encounterService.StartEncounter(encounterId);
        return CreateEncounterDto(encounter);
    }

    [Description("Execute an attack from an adversary against a player character in an encounter.")]
    public async Task<AdversaryAttackOutcomeDto> AttackPlayer(
        [Description("The unique identifier of the encounter where the attack takes place")]
        Guid encounterId,
        [Description("The unique identifier of the adversary performing the attack")]
        Guid attackingAdversaryId,
        [Description("The unique identifier of the player character being attacked")]
        Guid playerBeingAttackedId)
    {
        var defenceOutcome =
            await encounterService.AttackPlayer(encounterId, attackingAdversaryId, playerBeingAttackedId);

        return new AdversaryAttackOutcomeDto(defenceOutcome.DamageDealt);
    }

    [Description("Execute an attack from a player character against an adversary in an encounter")]
    public async Task<CharacterAttackOutcomeDto> AttackAdversary(
        [Description("The unique identifier of the encounter where the attack takes place")]
        Guid encounterId,
        [Description("The unique identifier of the player character performing the attack")]
        Guid attackingPlayerId,
        [Description("The unique identifier of the adversary being attacked")]
        Guid adversaryBeingAttackedId)
    {
        var outcome = await encounterService.AttackAdversary(encounterId, adversaryBeingAttackedId, attackingPlayerId);

        return new CharacterAttackOutcomeDto(outcome.Hit, outcome.Damage.Amount, outcome.Critical, outcome.Fumble,
            outcome.WeaponBroken, outcome.TargetArmorDegraded);
    }

    [Description(
        "Ends an encounter. The encounter must already be started and have no active(not dead, not fled) adversaries.")]
    public async Task<EncounterDto> EndEncounter(Guid encounterId)
    {
        var encounter = await encounterService.EndEncounter(encounterId);
        return CreateEncounterDto(encounter);
    }

    [Description(
        "Get an encounter by its unique identifier. Returns the encounter details including adversaries and their states.")]
    public async Task<EncounterDto> GetEncounter(Guid encounterId)
    {
        var encounter = await repository.Get(encounterId) ?? throw new InvalidOperationException("Encounter not found");
        return CreateEncounterDto(encounter);
    }

    private static EncounterDto CreateEncounterDto(Encounter encounter)
    {
        return new EncounterDto
        {
            Id = encounter.Id,
            Name = encounter.Name,
            Description = encounter.Description,
            Adversaries = encounter.Adversaries.Select(e => new AdversaryDto
            {
                Id = e.Id,
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
            ArmorTier = GetArmorTier(adversary.Armor.Tier),
            Morale = adversary.Morale,
            Attack = new AttackProfileDto
            {
                Description = adversary.Attack.Description,
                DamageDice = adversary.Attack.DamageDie.ToString()
            },
            IsDead = adversary.Hp.Current <= 0,
            IsFled = adversary.IsFled
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

    private static Armor GenerateArmor(ArmorTierDto armorType)
    {
        return armorType switch
        {
            ArmorTierDto.Light => new Armor(LightArmorTier.Instance),
            ArmorTierDto.Medium => new Armor(MediumArmorTier.Instance),
            ArmorTierDto.Heavy => new Armor(HeavyArmorTier.Instance),
            ArmorTierDto.None => new Armor(NoArmorTier.Instance),
            _ => throw new ArgumentException(
                $"Unknown armor type: {armorType}. Expected one of: light, medium, heavy, none.")
        };
    }
}