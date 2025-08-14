using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Encounters;

public sealed class Encounter
{
    private readonly List<Adversary> _adversaries = [];

    private Encounter(Guid id, EncounterType initialType, string name, string description)
    {
        Id = id;
        InitialType = initialType;
        Name = name;
        Description = description;
    }
    
    public Guid Id { get; }
    
    public EncounterType InitialType { get; }
    
    public EncounterType CurrentType { get; private set; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<Adversary> Adversaries => _adversaries;

    public IReadOnlyList<Adversary> LivingAdversaries => _adversaries.Where(a => !a.IsDead).ToList().AsReadOnly();

    public IReadOnlyList<Adversary> DeadAdversaries => _adversaries.Where(a => a.IsDead).ToList().AsReadOnly();

    public void AddAdversary(Adversary e)
    {
        _adversaries.Add(e);
    }

    private void RemoveAdversary(Adversary e)
    {
        var adversary = _adversaries.First(a => a.Id == e.Id);
        _adversaries.Remove(adversary);
    }

    public DefenceOutcome AdversaryAttack(Character player, Adversary foe, IRandomService rng)
    {
        return player.Defend(rng, foe.Attack.DamageDie);
    }

    public AttackOutcome PlayerAttack(IRandomService rng, Character player, Adversary foe)
    {
        var outcome = player.Attack(rng, foe.Armor);

        if (outcome.Hit) foe.ReceiveDamage(outcome.Damage.Amount);

        if (!ShouldCheckMorale())
            return outcome;
        
        var moraleDiceExpr = DiceExpr.Parse("2d6");
        var moraleRoll = rng.Roll(moraleDiceExpr);

        if (moraleRoll >= foe.Morale)
            return outcome;
        
        RemoveAdversary(foe);
        return outcome;
    }

    private bool ShouldCheckMorale()
    {
        var groupSize = _adversaries.Count;

        if (groupSize == 1)
        {
            var adversary = _adversaries.First();
            var hasLessThanThirdHp = adversary.Hp.Current < adversary.Hp.Max *  0.3;

            return hasLessThanThirdHp;
        }
        
        var livingAdversaries = LivingAdversaries.Count;

        return livingAdversaries <= groupSize / 2;
    }

    public static Encounter Create(string name, string description, EncounterType initialType, IRandomService rng)
    {
        var encounter = new Encounter(Guid.NewGuid(), initialType, name, description);
        encounter.Initiate(rng, initialType);

        return encounter;
    }

    private void Initiate(IRandomService rng, EncounterType initialType)
    {
        if (initialType is not EncounterType.Unknown)
            return;
        
        var reaction = RollInitialReaction(rng);
        if (reaction is InitialReaction.Kill or InitialReaction.Angered)
        {
            ElevateToHostile();
        }
        else
        {
            ElevateToFriendly();
        }
    }

    private void ElevateToFriendly()
    {
        CurrentType = EncounterType.Friendly;
    }

    private void ElevateToHostile()
    {
        CurrentType = EncounterType.Hostile;
    }
    
    private static InitialReaction RollInitialReaction(IRandomService rng)
    {
        var initialReactionDiceExpr = DiceExpr.Parse("2d6");
        var rollResult = rng.Roll(initialReactionDiceExpr);

        return rollResult switch
        {
            2 or 3 => InitialReaction.Kill,
            >= 4 and <= 6 => InitialReaction.Angered,
            7 or 8 => InitialReaction.Indifferent,
            9 or 10 => InitialReaction.AlmostFriendly,
            _ => InitialReaction.Helpful
        };
    }
}