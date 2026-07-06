using System.ComponentModel;
using WretchedWhispers.Api.GameTools.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.GameTools;

/// <summary>
/// Encounter and combat game-master tools. Auto-fills encounter/character ids from
/// <see cref="SessionContext"/> (the model only ever names an adversary), validates arguments, and
/// calls the domain directly. Also owns CompleteResolution, the post-combat lifecycle step.
/// </summary>
[Description("Manage encounters and combat: create them, add adversaries, start combat, execute attacks, end encounters, and resolve the aftermath.")]
public sealed class EncounterTools(
    EncounterService encounterService,
    IEncountersRepository repository,
    CampaignService campaignService,
    SessionContext sessionContext)
{
    private Guid RequireEncounterId() =>
        sessionContext.ActiveEncounterId
        ?? throw new InvalidOperationException("No active encounter -- call CreateEncounter first.");

    private Guid RequireCharacterId() =>
        sessionContext.CharacterId
        ?? throw new InvalidOperationException("No character exists yet -- call CreateCharacter first.");

    [Description("Create a new encounter with the specified name, description, and initial type")]
    [GameTool(SessionStage.Exploration)]
    public async Task<EncounterDto> CreateEncounter(
        [Description("The name of the encounter")] string name,
        [Description("A description of the encounter setting or narrative context")] string description,
        [Description("Initial type: Friendly, Hostile, or Unknown")] string initialEncounterType)
    {
        if (!Enum.TryParse(initialEncounterType, out EncounterType type))
            throw new ArgumentException(
                $"Encounter type {initialEncounterType} is not valid. Expected one of: Friendly, Hostile, Unknown.");

        var encounter = await encounterService.CreateEncounter(name, description, type);
        sessionContext.SetActiveEncounterId(encounter.Id);

        // Link the encounter to the campaign via the domain service so SessionContextLoader finds it
        // on the next turn.
        if (sessionContext.CampaignId is { } campaignId)
            await campaignService.AttachEncounter(campaignId, encounter.Id);

        return CreateEncounterDto(encounter);
    }

    [Description("Add an adversary to the current encounter")]
    [GameTool(SessionStage.Exploration)]
    public async Task<EncounterDto> AddAdversaryToEncounter(
        [Description("The adversary to add")] NewAdversaryDto adversary)
    {
        ToolGuard.Quantity(adversary.HitPoints, "adversary.hitPoints");
        ToolGuard.InRange(adversary.Morale, 2, 12, "adversary.morale", "a d6+d6-style score");
        ToolGuard.DiceExpression(adversary.WeaponDamageDie, "adversary.weaponDamageDie");

        var encounterId = RequireEncounterId();
        await encounterService.AddAdversaries(
            encounterId,
            [
                new Adversary(
                    adversary.Name,
                    new HitPoints(adversary.HitPoints, adversary.HitPoints),
                    GenerateArmor(adversary.ArmorTier),
                    adversary.Morale,
                    new AttackProfile(adversary.WeaponDescription, DiceExpr.Parse(adversary.WeaponDamageDie)))
            ]);

        var encounter = await repository.Get(encounterId)
            ?? throw new InvalidOperationException("Encounter not found");
        return CreateEncounterDto(encounter);
    }

    [Description("Start the current encounter")]
    [GameTool(SessionStage.Exploration)]
    public async Task<EncounterDto> StartEncounter()
    {
        var encounter = await encounterService.StartEncounter(RequireEncounterId());
        return CreateEncounterDto(encounter);
    }

    [Description("A living adversary attacks the player character. The adversary is auto-selected.")]
    [GameTool(SessionStage.Combat)]
    public async Task<AdversaryAttackOutcomeDto> AttackPlayer()
    {
        var adversary = LivingAdversaries().FirstOrDefault()
            ?? throw new InvalidOperationException("No living adversaries remain.");
        var outcome = await encounterService.AttackPlayer(RequireEncounterId(), adversary.Id, RequireCharacterId());
        return new AdversaryAttackOutcomeDto(outcome.DamageDealt);
    }

    [Description("The player character attacks an adversary by name.")]
    [GameTool(SessionStage.Combat)]
    public async Task<CharacterAttackOutcomeDto> AttackAdversary(
        [Description("Name of the adversary to attack")] string adversaryName)
    {
        var living = LivingAdversaries();
        var adversary = living.FirstOrDefault(a => a.Name.Equals(adversaryName, StringComparison.OrdinalIgnoreCase))
            ?? living.FirstOrDefault()
            ?? throw new InvalidOperationException("No living adversaries remain.");

        var outcome = await encounterService.AttackAdversary(RequireEncounterId(), adversary.Id, RequireCharacterId());
        return new CharacterAttackOutcomeDto(outcome.Hit, outcome.Damage.Amount, outcome.Critical, outcome.Fumble,
            outcome.WeaponBroken, outcome.TargetArmorDegraded, outcome.BaseDamageRoll, outcome.DamageReduction);
    }

    [Description("End the current encounter")]
    [GameTool(SessionStage.Combat)]
    public async Task<EncounterDto> EndEncounter()
    {
        var encounter = await encounterService.EndEncounter(RequireEncounterId());
        return CreateEncounterDto(encounter);
    }

    [Description("Complete the resolution of the current encounter and return to exploration")]
    [GameTool(SessionStage.Resolution)]
    public async Task CompleteResolution()
    {
        if (sessionContext.ActiveEncounterId is null)
            throw new InvalidOperationException("No encounter to resolve.");

        // Persist IsResolved=true so DeriveStage returns Exploration on the next turn.
        var encounter = await repository.Get(sessionContext.ActiveEncounterId.Value);
        if (encounter is not null)
        {
            encounter.Resolve();
            await repository.Save(encounter);
        }

        sessionContext.ClearActiveEncounter();
    }

    private IReadOnlyList<Adversary> LivingAdversaries()
    {
        var encounter = sessionContext.ActiveEncounter
            ?? throw new InvalidOperationException("No active encounter.");
        return encounter.LivingAdversaries;
    }

    private static EncounterDto CreateEncounterDto(Encounter encounter) => new()
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

    private static AdversaryDto CreateAdversaryDto(Adversary adversary) => new()
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

    private static ArmorTierDto GetArmorTier(ArmorTier armorTier) => armorTier switch
    {
        ArmorTier.Light => ArmorTierDto.Light,
        ArmorTier.Medium => ArmorTierDto.Medium,
        ArmorTier.Heavy => ArmorTierDto.Heavy,
        ArmorTier.None => ArmorTierDto.None,
        _ => throw new ArgumentException($"Unknown armor tier: {armorTier}.")
    };

    private static Armor GenerateArmor(ArmorTierDto armorType) => armorType switch
    {
        ArmorTierDto.Light => new Armor(ArmorTier.Light),
        ArmorTierDto.Medium => new Armor(ArmorTier.Medium),
        ArmorTierDto.Heavy => new Armor(ArmorTier.Heavy),
        ArmorTierDto.None => new Armor(ArmorTier.None),
        _ => throw new ArgumentException(
            $"Unknown armor type: {armorType}. Expected one of: light, medium, heavy, none.")
    };
}
