using System.ComponentModel;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Engine.GameTools;

/// <summary>
/// Encounter and combat game-master tools. Auto-fills encounter/character ids from
/// <see cref="SessionContext"/> (the model only ever names an adversary), validates arguments, and
/// calls the domain directly. Also owns CompleteResolution, the post-combat lifecycle step.
/// </summary>
[Description("Manage encounters and combat: create encounters, add adversaries, start combat, resolve combat rounds, and complete the resolution aftermath.")]
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
        [Description("Initial type. 'Unknown' = the domain rolls the Mörk Borg reaction table and returns the result — the DEFAULT for any first meeting whose attitude the fiction leaves open. Pre-declare 'Hostile' or 'Friendly' ONLY when the fiction predetermines the attitude (an ambush, a sworn enemy, a hired guide).")] string initialEncounterType)
    {
        if (!Enum.TryParse(initialEncounterType, ignoreCase: true, out EncounterType type)
            || !Enum.IsDefined(type))
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

    [Description("Add an adversary to the current encounter. Stat the creature as the fiction demands — the domain then scales hit points and caps armor to the campaign's difficulty, so forgiving campaigns stay winnable. The returned encounter carries the ADJUSTED stats: treat those as the truth and never restate the numbers you sent.")]
    [GameTool(SessionStage.Exploration)]
    public async Task<EncounterDto> AddAdversaryToEncounter(
        [Description("The adversary to add")] NewAdversaryDto adversary)
    {
        ToolGuard.Positive(adversary.HitPoints, "adversary.hitPoints", "at least 1");
        ToolGuard.InRange(adversary.Morale, 2, 12, "adversary.morale", "a d6+d6-style score");
        ToolGuard.DiceExpression(adversary.WeaponDamageDie, "adversary.weaponDamageDie");

        var encounterId = RequireEncounterId();
        // The domain scales the GM's stats to the campaign difficulty — the model states the creature
        // it imagines and never has to reason about whether the fight is winnable.
        await encounterService.AddAdversaries(
            encounterId,
            [
                Adversary.Create(
                    adversary.Name,
                    adversary.HitPoints,
                    MapArmorTier(adversary.ArmorTier),
                    adversary.Morale,
                    new AttackProfile(adversary.WeaponDescription, DiceExpr.Parse(adversary.WeaponDamageDie)),
                    Difficulty())
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

    [Description("Escalate the current encounter to Hostile. Use ONLY when the fiction legitimately escalates — the player attacks first, negotiation collapses, treachery is revealed. Never use it to override a rolled reaction without in-fiction cause. Required before StartEncounter when the encounter is Friendly.")]
    [GameTool(SessionStage.Exploration)]
    public async Task<EncounterDto> TurnEncounterHostile()
    {
        var encounter = await encounterService.TurnHostile(RequireEncounterId());
        return CreateEncounterDto(encounter);
    }

    [Description("Resolve EXACTLY ONE combat round from the player's action: resolves the player's attack or flee attempt, then every living adversary's retaliation, morale, and ends the encounter automatically when the fight is over. Call it once per player combat action — never more.")]
    [GameTool(SessionStage.Combat)]
    public async Task<CombatRoundOutcomeDto> ResolveCombatRound(
        [Description("The player's action this round: 'Attack' (strike an adversary), 'Flee' (attempt to escape), or 'Other' (the player's action was already resolved with another tool this turn - enemies still respond)")]
        string action,
        [Description("Name of the adversary to attack (Attack only; defaults to the nearest living adversary)")]
        string? targetAdversaryName = null,
        [Description("Optional omen spend - COSTS one of the player's omens (see Omens in Game State). 'MaxDamage': the player's attack this round deals its weapon's maximum damage. 'ReduceDamageTaken': the first hit the player suffers this round is reduced by d6. Fails if no omens remain. Only use when the player asks to spend an omen, or at a truly dramatic moment.")]
        CombatOmenUse omenUse = CombatOmenUse.None)
    {
        if (!Enum.TryParse<PlayerRoundAction>(action, ignoreCase: true, out var roundAction))
            throw new ArgumentException(
                $"Action '{action}' is not valid. Expected one of: Attack, Flee, Other.");

        var outcome = await encounterService.ResolveRound(
            RequireEncounterId(), RequireCharacterId(), roundAction, targetAdversaryName, omenUse,
            Difficulty());
        return CombatRoundOutcomeDto.From(outcome);
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

    private static EncounterDto CreateEncounterDto(Encounter encounter) => new()
    {
        Name = encounter.Name,
        Description = encounter.Description,
        Disposition = encounter.CurrentType.ToString(),
        Reaction = encounter.Reaction?.ToString(),
        ReactionRoll = encounter.ReactionRoll,
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
        LivingAdversaries = encounter.LivingAdversaries.Select(CreateAdversaryDto).ToList(),
        DeadAdversaries = encounter.DeadAdversaries.Select(CreateAdversaryDto).ToList()
    };

    private static AdversaryDto CreateAdversaryDto(Adversary adversary) => new()
    {
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

    // Same fallback as CharacterTools: an unconfigured campaign is treated as Grim.
    private DifficultySettings Difficulty() =>
        DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Core.Campaigns.Difficulty.Grim);

    private static ArmorTier MapArmorTier(ArmorTierDto armorType) => armorType switch
    {
        ArmorTierDto.Light => ArmorTier.Light,
        ArmorTierDto.Medium => ArmorTier.Medium,
        ArmorTierDto.Heavy => ArmorTier.Heavy,
        ArmorTierDto.None => ArmorTier.None,
        _ => throw new ArgumentException(
            $"Unknown armor type: {armorType}. Expected one of: light, medium, heavy, none.")
    };
}
