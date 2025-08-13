using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;

namespace WretchedWhispers.Core.Encounters;

public sealed class Encounter
{
    private readonly List<Adversary> _adversaries = [];

    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<Adversary> Adversaries => _adversaries;

    public IReadOnlyList<Adversary> LivingAdversaries => _adversaries.Where(a => !a.IsDead).ToList().AsReadOnly();

    public IReadOnlyList<Adversary> DeadAdversaries => _adversaries.Where(a => a.IsDead).ToList().AsReadOnly();

    public Adversary AddAdversary(Adversary e)
    {
        _adversaries.Add(e);
        return e;
    }

    public DefenceOutcome AdversaryAttack(Character player, Adversary foe, IRandomService rng)
    {
        return player.Defend(rng, foe.Attack.DamageDie);
    }

    public AttackOutcome PlayerAttack(IRandomService rng, Character player, Adversary foe)
    {
        var outcome = player.Attack(rng, foe.Armor);

        if (outcome.Hit) foe.ReceiveDamage(outcome.Damage.Amount);

        return outcome;
    }
}