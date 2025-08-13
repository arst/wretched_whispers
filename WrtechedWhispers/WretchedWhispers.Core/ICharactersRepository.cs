using WretchedWhispers.Core.Characters;

namespace WretchedWhispers.Core;

public interface ICharactersRepository
{
    Task<Character?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Character character, CancellationToken ct = default);
}