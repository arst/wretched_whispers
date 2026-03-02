using System.Text.Json.Serialization;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Encounters;

public sealed class Encounter
{
    [JsonConstructor]
    private Encounter(Guid id, EncounterType initialType, EncounterType currentType, string name, string description,
        List<Adversary> adversaries, bool isStarted = false, bool isEnded = false)
    {
        Id = id;
        InitialType = initialType;
        CurrentType = currentType;
        Name = name;
        Description = description;
        Adversaries = adversaries ?? [];
        IsStarted = isStarted;
        IsEnded = isEnded;
    }

    private Encounter(Guid id, EncounterType initialType, string name, string description)
        : this(id, initialType, default, name, description, [])
    {
    }

    public Guid Id { get; }
    public EncounterType InitialType { get; }
    [JsonInclude] public EncounterType CurrentType { get; private set; }
    public string Name { get; }
    public string Description { get; }
    [JsonInclude] public List<Adversary> Adversaries { get; private set; }
    [JsonIgnore] public IReadOnlyList<Adversary> LivingAdversaries => Adversaries.Where(a => !a.IsDead).ToList().AsReadOnly();
    [JsonIgnore] public IReadOnlyList<Adversary> DeadAdversaries => Adversaries.Where(a => a.IsDead).ToList().AsReadOnly();
    public bool IsStarted { get; set; }
    public bool IsEnded { get; set; }

    public static Encounter Create(string name, string description, EncounterType initialType, IRandomService rng)
    {
        var encounter = new Encounter(Guid.NewGuid(), initialType, name, description);
        encounter.Initiate(initialType);

        return encounter;
    }

    public void StartEncounter()
    {
        if (Adversaries.Count == 0)
            throw new InvalidOperationException("Can't start an encounter without adversaries.");
        IsStarted = true;
        IsEnded = false;
    }

    public void EndEncounter()
    {
        if (!IsStarted) throw new InvalidOperationException("Can't end an encounter that hasn't started.");
        var anyActiveAdversaries = Adversaries.Any(a => a is { IsDead: false, IsFled: false });
        if (anyActiveAdversaries)
            throw new InvalidOperationException("Can't end an encounter with active adversaries.");
        IsEnded = true;
    }

    public void AddAdversary(Adversary e)
    {
        Adversaries.Add(e);
    }

    public void ProcessPlayerAttackOutcome(AttackOutcome outcome, Guid adversaryId)
    {
        var adversary = Adversaries.Single(a => a.Id == adversaryId);

        if (outcome.Hit) adversary.ReceiveDamage(outcome.Damage.Amount);

        if (!ShouldCheckMorale())
            return;

        var moraleDiceExpr = DiceExpr.D(2, 6);
        var moraleRoll = Dice.Roll(moraleDiceExpr);

        if (moraleRoll >= adversary.Morale)
            return;

        adversary.Retreat();
    }

    public void ProcessPlayerDefenceOutcome(DefenceOutcome defenceOutcome, Guid adversaryId)
    {
    }

    private void Initiate(EncounterType initialType)
    {
        if (initialType is not EncounterType.Unknown)
            return;

        var reaction = RollInitialReaction();
        if (reaction is InitialReaction.Kill or InitialReaction.Angered)
            ElevateToHostile();
        else
            ElevateToFriendly();
    }

    private void ElevateToFriendly()
    {
        CurrentType = EncounterType.Friendly;
    }

    private void ElevateToHostile()
    {
        CurrentType = EncounterType.Hostile;
    }

    private static InitialReaction RollInitialReaction()
    {
        var initialReactionDiceExpr = DiceExpr.D(2, 6);
        var rollResult = Dice.Roll(initialReactionDiceExpr);

        return rollResult switch
        {
            2 or 3 => InitialReaction.Kill,
            >= 4 and <= 6 => InitialReaction.Angered,
            7 or 8 => InitialReaction.Indifferent,
            9 or 10 => InitialReaction.AlmostFriendly,
            _ => InitialReaction.Helpful
        };
    }

    private bool ShouldCheckMorale()
    {
        var groupSize = Adversaries.Count;

        if (groupSize == 1)
        {
            var adversary = Adversaries.First();
            var hasLessThanThirdHp = adversary.Hp.Current < adversary.Hp.Max * 0.3;

            return hasLessThanThirdHp;
        }

        var livingAdversaries = LivingAdversaries.Count;

        return livingAdversaries <= groupSize / 2;
    }
}