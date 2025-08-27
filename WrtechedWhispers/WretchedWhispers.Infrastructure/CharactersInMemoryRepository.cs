using System.Collections.Concurrent;
using WretchedWhispers.Core.Characters;

namespace WretchedWhispers.Infrastructure;

public class CharactersInMemoryRepository : ICharactersRepository
{
    private static readonly ConcurrentDictionary<Guid, Character> Characters = new();

    public Task<Character?> Get(Guid id, CancellationToken ct = default)
    {
        Characters.TryGetValue(id, out var character);
        return Task.FromResult(character);
    }

    public Task Save(Character character, CancellationToken ct = default)
    {
        Characters.AddOrUpdate(character.Id, character, (k, v) => v);

        return Task.CompletedTask;
    }
}