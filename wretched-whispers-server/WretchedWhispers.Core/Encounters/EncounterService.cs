using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Encounters;

public class EncounterService(
    Dice dice,
    ICharactersRepository charactersRepository,
    IEncountersRepository encountersRepository)
{
    public async Task<Encounter> CreateEncounter(string name, string description, EncounterType encounterType, CancellationToken ct = default)
    {
        var encounter = Encounter.Create(name, description, encounterType, dice);
        await encountersRepository.Save(encounter, ct);
        return encounter;
    }

    public async Task AddAdversaries(Guid encounterId, IEnumerable<Adversary> adversaries, CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct) ??
                        throw new InvalidOperationException("Encounter not found");

        foreach (var adversary in adversaries) encounter.AddAdversary(adversary);

        await encountersRepository.Save(encounter, ct);
    }

    public async Task<Encounter> StartEncounter(Guid encounterId, CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct) ??
                        throw new InvalidOperationException("Encounter not found");
        encounter.StartEncounter();
        await encountersRepository.Save(encounter, ct);

        return encounter;
    }

    public async Task<Encounter> EndEncounter(Guid encounterId, CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct) ??
                        throw new InvalidOperationException("Encounter not found");
        encounter.EndEncounter();
        await encountersRepository.Save(encounter, ct);

        return encounter;
    }

    public async Task<Encounter> TurnHostile(Guid encounterId, CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct) ??
                        throw new InvalidOperationException("Encounter not found");
        encounter.TurnHostile();
        await encountersRepository.Save(encounter, ct);

        return encounter;
    }

    public async Task<bool> IsEncounterActive(Guid encounterId, CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct) ??
                        throw new InvalidOperationException("Encounter not found");
        return encounter is { IsStarted: true, IsEnded: false };
    }

    /// <summary>Resolves one whole combat round: the player's declared action, then retaliation from
    /// every adversary still standing (unless the player fled), then end-of-round bookkeeping.</summary>
    public async Task<CombatRoundOutcome> ResolveRound(
        Guid encounterId, Guid characterId, PlayerRoundAction action, string? targetName = null,
        CombatOmenUse omenUse = CombatOmenUse.None, DifficultySettings? settings = null,
        CancellationToken ct = default)
    {
        var encounter = await encountersRepository.Get(encounterId, ct)
            ?? throw new InvalidOperationException("Encounter not found");
        var character = await charactersRepository.Get(characterId, ct)
            ?? throw new InvalidOperationException("Character not found");
        if (!encounter.IsStarted || encounter.IsEnded)
            throw new InvalidOperationException("The encounter is not in active combat.");
        if (omenUse is not CombatOmenUse.None && character.Omens.Count == 0)
            throw new InvalidOperationException("No omens remaining.");

        AttackOutcome? playerAttack = null;
        string? attackedName = null;
        ChallengeOutcome? fleeAttempt = null;
        var playerFled = false;
        // Track by id, not name — two adversaries can share a name, and only one of them fled.
        var fledBefore = encounter.Adversaries.Where(a => a.IsFled).Select(a => a.Id).ToHashSet();

        switch (action)
        {
            case PlayerRoundAction.Attack:
                {
                    var living = ActiveAdversaries(encounter);
                    if (living.Count == 0)
                        throw new InvalidOperationException("No living adversaries remain.");
                    var target = living.FirstOrDefault(a =>
                            a.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                        ?? living[0];
                    // No settings => MORK BORG RAW, the strictest reading. Only the forgiving presets
                    // floor a landed blow, and only for the player's own swing.
                    var outcome = character.Attack(target.Armor, dice,
                        spendOmenForMaxDamage: omenUse is CombatOmenUse.MaxDamage,
                        minimumOneDamage: settings?.PlayerHitsAlwaysDamage ?? false);
                    encounter.ProcessPlayerAttackOutcome(outcome, target.Id, dice);
                    playerAttack = outcome;
                    attackedName = target.Name;
                    break;
                }
            case PlayerRoundAction.Flee:
                fleeAttempt = character.AttemptFlee(dice);
                playerFled = fleeAttempt.IsSuccess;
                break;
            case PlayerRoundAction.Other:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        var retaliations = new List<AdversaryRetaliation>();
        if (!playerFled)
        {
            // The omen shield covers the first hit that actually deals damage this round.
            var omenShieldAvailable = omenUse is CombatOmenUse.ReduceDamageTaken;
            foreach (var adversary in ActiveAdversaries(encounter))
            {
                if (character.IsDead) break;
                var defence = character.Defend(adversary.Attack.DamageDie, dice,
                    spendOmenToReduceDamage: omenShieldAvailable);
                if (defence.OmenDamageReduction > 0) omenShieldAvailable = false;
                retaliations.Add(new AdversaryRetaliation(adversary.Name, defence));
            }
        }

        var fledThisRound = encounter.Adversaries
            .Where(a => a.IsFled && !fledBefore.Contains(a.Id))
            .Select(a => a.Name)
            .ToList();

        var endReason =
            playerFled ? EncounterEndReason.PlayerFled
            : character.IsDead ? EncounterEndReason.PlayerDead
            : !encounter.Adversaries.Any(a => a is { IsDead: false, IsFled: false }) ? EncounterEndReason.AllDefeated
            : EncounterEndReason.None;

        if (endReason is EncounterEndReason.AllDefeated) encounter.EndEncounter();
        else if (endReason is EncounterEndReason.PlayerFled) encounter.EndByPlayerEscape();
        // PlayerDead: DeriveStage's IsDead check takes over; the encounter is left as-is.

        await charactersRepository.Save(character, ct);
        await encountersRepository.Save(encounter, ct);

        return new CombatRoundOutcome(
            playerAttack, attackedName, fleeAttempt, retaliations, fledThisRound,
            endReason is not EncounterEndReason.None, endReason,
            character.Hp.Current, character.Hp.IsZero && !character.IsDead);
    }

    private static IReadOnlyList<Adversary> ActiveAdversaries(Encounter encounter) =>
        encounter.Adversaries.Where(a => a is { IsDead: false, IsFled: false }).ToList();
}
