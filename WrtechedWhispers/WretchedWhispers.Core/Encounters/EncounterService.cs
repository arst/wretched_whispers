using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Encounters;

public class EncounterService(
    IRandomService rng,
    ICharactersRepository charactersRepository,
    IEncountersRepository encountersRepository)
{
    public async Task<Encounter> CreateEncounter(string name, string description, EncounterType encounterType)
    {
        var encounter = Encounter.Create(name, description, encounterType, rng);
        await encountersRepository.Save(encounter);
        return encounter;
    }

    public async Task AddAdversaries(Guid encounterId, IEnumerable<Adversary> adversaries)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");

        foreach (var adversary in adversaries) encounter.AddAdversary(adversary);

        await encountersRepository.Save(encounter);
    }

    public async Task<Encounter> StartEncounter(Guid encounterId)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");
        encounter.StartEncounter();
        await encountersRepository.Save(encounter);

        return encounter;
    }

    public async Task<Encounter> EndEncounter(Guid encounterId)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");
        encounter.EndEncounter();
        await encountersRepository.Save(encounter);

        return encounter;
    }

    public async Task<bool> IsEncounterActive(Guid encounterId)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");
        return encounter is { IsStarted: true, IsEnded: false };
    }

    public async Task<AttackOutcome> AttackAdversary(Guid encounterId, Guid adversaryId, Guid characterId)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");
        var attacker = await charactersRepository.Get(characterId) ?? throw new InvalidOperationException();
        var adversary = encounter.Adversaries.Single(a => a.Id == adversaryId);

        var outcome = attacker.Attack(adversary.Armor);
        await charactersRepository.Save(attacker);

        encounter.ProcessPlayerAttackOutcome(outcome, adversaryId);
        await encountersRepository.Save(encounter);
        return outcome;
    }

    public async Task<DefenceOutcome> AttackPlayer(Guid encounterId, Guid adversaryId, Guid characterId)
    {
        var encounter = await encountersRepository.Get(encounterId) ??
                        throw new InvalidOperationException("Encounter not found");
        var defender = await charactersRepository.Get(characterId) ?? throw new InvalidOperationException();
        var adversary = encounter.Adversaries.Single(a => a.Id == adversaryId);

        var defenceOutcome = defender.Defend(adversary.Attack.DamageDie);
        await charactersRepository.Save(defender);

        encounter.ProcessPlayerDefenceOutcome(defenceOutcome, adversaryId);
        await encountersRepository.Save(encounter);

        return defenceOutcome;
    }
}