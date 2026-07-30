using System.Text.Json.Serialization;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Encounters;

public sealed class Encounter
{
    private readonly List<Adversary> _adversaries;

    [JsonConstructor]
    private Encounter(Guid id, EncounterType initialType, EncounterType currentType, string name, string description,
        // Param type must equal the property type or STJ refuses to bind the constructor.
        IReadOnlyList<Adversary> adversaries, bool isStarted = false, bool isEnded = false, bool isResolved = false,
        InitialReaction? reaction = null, int? reactionRoll = null)
    {
        Id = id;
        InitialType = initialType;
        CurrentType = currentType;
        Name = name;
        Description = description;
        _adversaries = adversaries?.ToList() ?? [];
        IsStarted = isStarted;
        IsEnded = isEnded;
        IsResolved = isResolved;
        Reaction = reaction;
        ReactionRoll = reactionRoll;
    }

    private Encounter(Guid id, EncounterType initialType, string name, string description)
        : this(id, initialType, default, name, description, [])
    {
    }

    public Guid Id { get; }
    public EncounterType InitialType { get; }
    [JsonInclude] public EncounterType CurrentType { get; private set; }
    [JsonInclude] public InitialReaction? Reaction { get; private set; }
    [JsonInclude] public int? ReactionRoll { get; private set; }
    public string Name { get; }
    public string Description { get; }
    // Read-only projection over the list the constructor binds — mutation goes through AddAdversary.
    [JsonInclude] public IReadOnlyList<Adversary> Adversaries => _adversaries;
    [JsonIgnore] public IReadOnlyList<Adversary> LivingAdversaries => Adversaries.Where(a => !a.IsDead).ToList().AsReadOnly();
    [JsonIgnore] public IReadOnlyList<Adversary> DeadAdversaries => Adversaries.Where(a => a.IsDead).ToList().AsReadOnly();
    [JsonInclude] public bool IsStarted { get; private set; }
    [JsonInclude] public bool IsEnded { get; private set; }
    [JsonInclude] public bool IsResolved { get; private set; }

    public static Encounter Create(string name, string description, EncounterType initialType, Dice dice)
    {
        var encounter = new Encounter(Guid.NewGuid(), initialType, name, description);
        encounter.Initiate(initialType, dice);

        return encounter;
    }

    public void StartEncounter()
    {
        if (CurrentType == EncounterType.Friendly)
            throw new InvalidOperationException(
                "The encounter is friendly — call TurnEncounterHostile first; the fiction must escalate before combat can start.");
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

    /// <summary>Escalates a friendly meeting to hostile (player aggression, collapsed talks).
    /// Idempotent when already hostile; only a finished encounter refuses.</summary>
    public void TurnHostile()
    {
        if (IsEnded) throw new InvalidOperationException("Can't turn a finished encounter hostile.");
        ElevateToHostile();
    }

    /// <summary>Ends the encounter because the player escaped — adversaries may still be active.</summary>
    public void EndByPlayerEscape()
    {
        if (!IsStarted) throw new InvalidOperationException("Can't end an encounter that hasn't started.");
        IsEnded = true;
    }

    public void Resolve()
    {
        if (!IsEnded) throw new InvalidOperationException("Can't resolve an encounter that hasn't ended.");
        IsResolved = true;
    }

    public void AddAdversary(Adversary e)
    {
        _adversaries.Add(e);
    }

    public void ProcessPlayerAttackOutcome(AttackOutcome outcome, Guid adversaryId, Dice dice)
    {
        var adversary = Adversaries.Single(a => a.Id == adversaryId);

        if (outcome.Hit) adversary.ReceiveDamage(outcome.Damage.Amount);

        if (!ShouldCheckMorale())
            return;

        var moraleDiceExpr = DiceExpr.D(2, 6);
        var moraleRoll = dice.Roll(moraleDiceExpr);

        if (moraleRoll >= adversary.Morale)
            return;

        adversary.Retreat();
    }

    private void Initiate(EncounterType initialType, Dice dice)
    {
        if (initialType is not EncounterType.Unknown)
        {
            CurrentType = initialType;
            return;
        }

        var rollResult = dice.Roll(DiceExpr.D(2, 6));
        ReactionRoll = rollResult;
        Reaction = MapReaction(rollResult);
        if (Reaction is InitialReaction.Kill or InitialReaction.Angered)
            ElevateToHostile();
        else
            ElevateToFriendly();
    }

    private static InitialReaction MapReaction(int rollResult) => rollResult switch
    {
        2 or 3 => InitialReaction.Kill,
        >= 4 and <= 6 => InitialReaction.Angered,
        7 or 8 => InitialReaction.Indifferent,
        9 or 10 => InitialReaction.AlmostFriendly,
        _ => InitialReaction.Helpful
    };

    private void ElevateToFriendly()
    {
        CurrentType = EncounterType.Friendly;
    }

    private void ElevateToHostile()
    {
        CurrentType = EncounterType.Hostile;
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
