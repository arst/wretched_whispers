using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public class CharacterService(ICharactersRepository charactersRepository, Dice dice)
{
    public async Task<ChallengeResult> ChallengePlayer(
        Guid characterId, Dr dr, AbilityKind ability, DifficultySettings settings,
        ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None,
        bool spendOmenToLowerDr = false)
    {
        var character = await charactersRepository.Get(characterId);

        if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

        var outcome = character.Challenge(dr, ability, dice, spendOmenToLowerDr: spendOmenToLowerDr);

        var damageTaken = 0;
        var consequenceApplied = !outcome.IsSuccess && consequenceOnFailure is not ChallengeConsequence.None;
        if (consequenceApplied)
            damageTaken = character.SufferConsequence(consequenceOnFailure, settings, dice);

        // A spent omen must persist even when the test succeeds.
        if (spendOmenToLowerDr || consequenceApplied)
            await charactersRepository.Save(character);

        return new ChallengeResult(outcome, damageTaken, character.IsDead, character.Hp.Current);
    }
}
