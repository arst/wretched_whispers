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
/*
1. Session zero / setup

GM pitches tone: bleak doom, heavy metal, “the world will end.”

Players make characters:

Roll or assign ability modifiers (Agility, Presence, Strength, Toughness).

Roll starting hit points (Toughness + d8, min 1).

Roll/choose starting equipment (weapons, armor, shield).

(Optional) Choose a class like Fanged Deserter (not yet coded in your Core).

Roll starting Omens (optional).

Learn any starting scrolls.

2. Opening scene

GM describes where the PCs are when the day begins.

Roll Calendar of Nechrubel: at dawn, on a 1, a Misery occurs; the 7th ends the world.

Powers (scroll uses) reset each dawn.

3. Exploration & encounters

Players narrate what they do.

GM describes the environment and throws hazards, NPCs, creatures, etc.

When actions are risky:

GM sets a DR (usually 12).

Player rolls d20 + ability mod.

If total ≥ DR → success; if not → failure.

Natural 20 = critical success; natural 1 = fumble.

4. Combat (when swords come out)

Attacking: Player rolls to hit (Strength for melee, Presence for ranged).

Defending: Player rolls Agility vs DR (modified by armor penalties) to avoid enemy hits.

If defence fails, damage is rolled and armor’s damage reduction die is subtracted.

Crits/fumbles trigger their effects (double damage, free attack, armor breaks, etc.).

Shields can block one full hit before breaking.

5. Magic

Casting a scroll = Presence DR12 test.

Failure = lose HP (d2), dizzy for an hour, and power fizzles.

Certain gear (zweihänder, medium/heavy armor) prevents scroll use.

6. Morale & reaction

First-time encounters: roll Reaction to see if they fight, negotiate, help, etc.

If a fight turns against enemies: roll Morale to see if they flee or surrender.

7. Rest, healing, infection

Quick rest heals d4 HP; full night heals d6.

Infection stops healing and causes d6 damage per day.

8. The grind toward the end

Days pass → more dawn rolls.

Miseries accumulate until 7:7 triggers apocalypse.
 */

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

    public Encounter StartEncounter(string name, string description)
    {
        var encounter = new Encounter(Guid.NewGuid(), name, description);
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