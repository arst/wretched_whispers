using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public class CharacterService(ICharactersRepository charactersRepository, Dice dice)
{
    public async Task<ChallengeOutcome> ChallengePlayer(Guid characterId, Dr dr, AbilityKind ability)
    {
        var character = await charactersRepository.Get(characterId);

        if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

        var challengeOutcome = character.Challenge(dr, ability, dice);

        return challengeOutcome;
    }
}
