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
        bool spendOmenToLowerDr = false,
        CancellationToken ct = default)
    {
        var character = await charactersRepository.Get(characterId, ct);

        if (character is null) throw new InvalidOperationException($"Character with id {characterId} does not exist.");

        // A pending blessing is spent by this test whether it passes or fails.
        var blessingSpent = character.NextTestBonus > 0;
        var outcome = character.Challenge(dr, ability, dice, spendOmenToLowerDr: spendOmenToLowerDr);

        var consequence = new ConsequenceOutcome(0, null);
        var consequenceApplied = !outcome.IsSuccess && consequenceOnFailure is not ChallengeConsequence.None;
        if (consequenceApplied)
            consequence = character.SufferConsequence(consequenceOnFailure, settings, dice);

        // A spent omen or blessing must persist even when the test succeeds.
        if (spendOmenToLowerDr || blessingSpent || consequenceApplied)
            await charactersRepository.Save(character, ct);

        return new ChallengeResult(
            outcome, consequence.Damage, character.IsDead, character.Hp.Current, consequence.Injury);
    }

    /// <summary>Uses one unit of an item the character carries and saves the result. Takes the loaded
    /// character rather than its id: the caller has already resolved the item on that instance.</summary>
    public async Task<ItemUseOutcome> UseItem(Character character, Guid itemId, CancellationToken ct = default)
    {
        var outcome = character.UseItem(itemId, dice);
        await charactersRepository.Save(character, ct);
        return outcome;
    }

    public async Task<GettingBetterOutcome> GetBetter(Guid characterId, bool allowAbilityLoss, CancellationToken ct = default)
    {
        var character = await charactersRepository.Get(characterId, ct);
        if (character is null) throw new InvalidOperationException($"Character with id {characterId} does not exist.");

        var outcome = character.GetBetter(dice, allowAbilityLoss);
        await charactersRepository.Save(character, ct);
        return outcome;
    }
}
