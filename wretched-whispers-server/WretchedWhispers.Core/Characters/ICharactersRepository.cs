namespace WretchedWhispers.Core.Characters;

public interface ICharactersRepository
{
    Task<Character?> Get(Guid id, CancellationToken ct = default);

    /// <summary>
    /// One query for many characters, keyed by id. Exists so the session list can show every
    /// wretch's name and HP without a round trip per campaign. Ids with no stored row are absent
    /// from the result rather than mapped to null.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Character>> GetMany(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task Save(Character character, CancellationToken ct = default);
}