using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Armor;
using WretchedWhispers.Core.Characters.Weapon;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Core.World;

namespace WretchedWhispers.Core;

public class Campaign
{
    private readonly CalendarOfNechrubel _calender;

    private readonly List<Character> _characters;

    private readonly DiceExpr _dawnDice;

    private readonly List<Encounter> _encounters;

    private Campaign(Guid id, string name, string description, int currentDay, int currentHour,
        List<Character> characters, CalendarOfNechrubel calender,
        DiceExpr dawnDice, List<Encounter> encounters)
    {
        Id = id;
        Name = name;
        Description = description;
        CurrentDay = currentDay;
        CurrentHour = currentHour;
        _characters = characters;
        _calender = calender;
        _dawnDice = dawnDice;
        _encounters = encounters;
    }

    public Guid Id { get; private set; }

    public string Name { get; }

    public string Description { get; }

    public int CurrentDay { get; private set; }

    public int CurrentHour { get; private set; }

    public IReadOnlyCollection<Misery> Miseries => _calender.Miseries;

    public IReadOnlyCollection<Encounter> Encounters => _encounters.AsReadOnly();

    public void AdvanceTime(int hours, IRandomService rng)
    {
        CurrentHour += hours;
        if (CurrentHour >= 24)
        {
            CurrentDay += CurrentHour / 24;
            CurrentHour %= 24;
            _calender.DawnRoll(new Dice.Dice(rng), _dawnDice);

            if (_calender.WorldEnded)
                throw new InvalidOperationException(
                    "World has ended, cannot advance time."); // TODO: Need to return the result with current Miseries and also if world ended or not.

            foreach (var character in _characters) character.NewDawn(new Dice.Dice(rng));
        }
    }

    public ChallengeOutcome ChallengeCharacter(Guid characterId, Dr dr, AbilityKind ability, IRandomService rng)
    {
        var character = _characters.Single(c => c.Id == characterId);
        var challengeOutcome = character.Challenge(new Dice.Dice(rng), dr, ability);

        return challengeOutcome;
    }

    public Encounter StartEncounter(Encounter character)
    {
        var encounter = new Encounter();
        _encounters.Add(encounter);

        return encounter;
    }

    public Encounter AddAdversary(Guid encounterId, Adversary adversary)
    {
        var encounter = _encounters.Single(e => e.Id == encounterId);
        encounter.AddAdversary(adversary);

        return encounter;
    }

    public AttackOutcome AttackAdversary(Guid encounterId, Guid adversaryId, Guid characterId, Armor targetArmor,
        IRandomService rng)
    {
        var encounter = _encounters.Single(e => e.Id == encounterId);
        var attacker = _characters.Single(c => c.Id == characterId);
        var adversary = encounter.Adversaries.Single(a => a.Id == adversaryId);
        return encounter.PlayerAttack(rng, attacker, adversary);
    }

    public DefenceOutcome AttackPlayer(Guid encounterId, Guid adversaryId, Guid characterId, Weapon attackingWeapon,
        IRandomService rng)
    {
        var defender = _characters.Single(c => c.Id == characterId);
        var encounter = _encounters.Single(e => e.Id == encounterId);
        var attackingAdversary = encounter.Adversaries.Single(a => a.Id == adversaryId);
        return encounter.AdversaryAttack(defender, attackingAdversary, rng);
    }

    public void JoinGame(Character character)
    {
        _characters.Add(character);
    }

    public static Campaign Create(DiceExpr dawnDice, string name, string description)
    {
        return new Campaign(Guid.NewGuid(), name, description, 1, 0, [], new CalendarOfNechrubel(), dawnDice, []);
    }
}