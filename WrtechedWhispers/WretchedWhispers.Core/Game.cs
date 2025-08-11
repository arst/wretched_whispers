using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.World;

namespace WretchedWhispers.Core;

public class Game
{
    public Guid Id { get; private set; }
    
    private int _currentDay;
    private int _currentHour;
    private readonly List<Character.Character> _characters;
    private readonly CalendarOfNechrubel _calender;
    private readonly DiceExpr _dawnDice;

    public Game(Guid id, int currentDay, int currentHour, List<Character.Character> characters, CalendarOfNechrubel calender,
        DiceExpr dawnDice)
    {
        Id = id;
        _currentDay = currentDay;
        _currentHour = currentHour;
        _characters = characters;
        _calender = calender;
        _dawnDice = dawnDice;
    }
    
    public void AdvanceTime(int hours, IRandomService rng)
    {
        _currentHour += hours;
        if (_currentHour >= 24)
        {
            _currentDay += _currentHour / 24;
            _currentHour %= 24;
            var dawnResult = _calender.DawnRoll(new Dice.Dice(rng), _dawnDice);
            foreach (var character in _characters)
            {
                character.NewDawn(new Dice.Dice(rng));
            }
        }
    }
    
    public void ChallengeCharacter(Guid characterId, Dr challengeDr, AbilityKind ability, IRandomService rng)
    {
        var character = _characters.Single(c => c.Id == characterId);
        character.Challenge(new Dice.Dice(rng), challengeDr, ability);
    }
    
    
    public void JoinGame(Character.Character character)
    {
        _characters.Add(character);
    }

    public Game Create(DiceExpr dawnDice)
    {
        return new Game(Guid.NewGuid(),1, 0, [], new CalendarOfNechrubel(), dawnDice);
    }
}