using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public class CharacterService(ICharactersRepository charactersRepository, Dice dice)
{
    public async Task<ChallengeResult> ChallengePlayer(
        Guid characterId, Dr dr, AbilityKind ability, DifficultySettings settings,
        ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
    {
        var character = await charactersRepository.Get(characterId);

        if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

        var outcome = character.Challenge(dr, ability, dice);

        var damageTaken = 0;
        if (!outcome.IsSuccess && consequenceOnFailure is not ChallengeConsequence.None)
        {
            damageTaken = character.SufferConsequence(consequenceOnFailure, settings, dice);
            await charactersRepository.Save(character);
        }

        return new ChallengeResult(outcome, damageTaken, character.IsDead);
    }
}
