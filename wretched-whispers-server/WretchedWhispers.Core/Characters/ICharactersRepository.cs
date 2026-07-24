namespace WretchedWhispers.Core.Characters;

public interface ICharactersRepository
{
    Task<Character?> Get(Guid id, CancellationToken ct = default);
    Task Save(Character character, CancellationToken ct = default);
}