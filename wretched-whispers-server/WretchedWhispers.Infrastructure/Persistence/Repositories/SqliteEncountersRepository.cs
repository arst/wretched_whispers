using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteEncountersRepository : IEncountersRepository
{
    private readonly WretchedWhispersDbContext _db;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteEncountersRepository(WretchedWhispersDbContext db, JsonSerializerOptions jsonOptions)
    {
        _db = db;
        _jsonOptions = jsonOptions;
    }

    public async Task<Encounter?> Get(Guid encounterId, CancellationToken ct = default)
    {
        var entity = await _db.Encounters.FindAsync([encounterId], ct);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Encounter>(entity.Data, _jsonOptions);
    }

    public async Task Save(Encounter encounter, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(encounter, _jsonOptions);
        var entity = await _db.Encounters.FindAsync([encounter.Id], ct);

        if (entity is not null)
        {
            entity.Data = json;
        }
        else
        {
            entity = new EncounterEntity { Id = encounter.Id, Data = json };
            _db.Encounters.Add(entity);
        }

        await _db.SaveChangesAsync(ct);
    }
}
