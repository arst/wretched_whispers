using WretchedWhispers.Core.Characters;

namespace WretchedWhispers.Core.CharacterCreation;

public interface ICharacterCreationService
{
    public Task<Character> Create(string name);
}