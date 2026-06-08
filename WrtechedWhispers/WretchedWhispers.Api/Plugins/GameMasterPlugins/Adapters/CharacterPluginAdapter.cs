using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Api.GameTools;
using WretchedWhispers.Api.GameTools.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;

/// <summary>
/// Adapts CharacterPlugin to ICharacterOperations.
/// CharacterPlugin methods match the interface but the class is in the Semantic project
/// which cannot reference the Api project where the interface lives.
/// </summary>
public sealed class CharacterPluginAdapter(CharacterPlugin inner) : ICharacterOperations
{
    public Task<CharacterDto> CreateCharacter(string name) => inner.CreateCharacter(name);

    public Task<ChallengeOutcomeDto> ChallengeCharacter(Guid characterId, int challengeDr, AbilityKind abilityKind) =>
        inner.ChallengeCharacter(characterId, challengeDr, abilityKind);

    public Task<CharacterDto> AddItemToCharacterInventory(Guid characterId, string itemDescription, bool isBulky, bool isOneTimeUse, int quantity) =>
        inner.AddItemToCharacterInventory(characterId, itemDescription, isBulky, isOneTimeUse, quantity);

    public Task<CharacterDto> RemoveItemFromCharacterInventory(Guid characterId, Guid itemId) =>
        inner.RemoveItemFromCharacterInventory(characterId, itemId);

    public Task<CharacterDto> ImproveCharacterAbility(Guid characterId, AbilityKind abilityKind, int delta) =>
        inner.ImproveCharacterAbility(characterId, abilityKind, delta);

    public Task<CharacterDto> DegradeCharacterAbility(Guid characterId, AbilityKind abilityKind, int delta) =>
        inner.DegradeCharacterAbility(characterId, abilityKind, delta);

    public Task<CharacterDto> InfectCharacter(Guid characterId) => inner.InfectCharacter(characterId);

    public Task<CharacterDto> CureInfection(Guid characterId) => inner.CureInfection(characterId);

    public Task<CharacterDto> BuyItem(Guid characterId, string itemDescription, int silverCost, bool isBulky, bool isOneTimeUse, int quantity) =>
        inner.BuyItem(characterId, itemDescription, silverCost, isBulky, isOneTimeUse, quantity);

    public Task<CastOutcomeDto> CastScroll(Guid characterId, Guid scrollId) => inner.CastScroll(characterId, scrollId);
}
