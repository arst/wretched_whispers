using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteCharactersRepository : ICharactersRepository
{
    private readonly WretchedWhispersDbContext _db;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteCharactersRepository(WretchedWhispersDbContext db, JsonSerializerOptions jsonOptions)
    {
        _db = db;
        _jsonOptions = jsonOptions;
    }

    public async Task<Character?> Get(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Characters.FindAsync([id], ct);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Character>(entity.Data, _jsonOptions);
    }

    public async Task<IReadOnlyDictionary<Guid, Character>> GetMany(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<Guid, Character>();

        var entities = await _db.Characters
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        var characters = new Dictionary<Guid, Character>(entities.Count);
        foreach (var entity in entities)
        {
            if (JsonSerializer.Deserialize<Character>(entity.Data, _jsonOptions) is { } character)
                characters[entity.Id] = character;
        }

        return characters;
    }

    public async Task Save(Character character, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(character, _jsonOptions);
        var entity = await _db.Characters.FindAsync([character.Id], ct);

        if (entity is not null)
        {
            entity.Data = json;
        }
        else
        {
            entity = new CharacterEntity { Id = character.Id, Data = json };
            _db.Characters.Add(entity);
        }

        await _db.SaveChangesAsync(ct);
    }
}
