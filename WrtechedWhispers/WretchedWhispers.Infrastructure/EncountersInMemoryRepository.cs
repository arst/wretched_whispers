using System.Collections.Concurrent;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Infrastructure;

public class EncountersInMemoryRepository : IEncountersRepository
{
    private static readonly ConcurrentDictionary<Guid, Encounter> Encounters = new();

    public Task<Encounter?> Get(Guid encounterId)
    {
        Encounters.TryGetValue(encounterId, out var encounter);
        return Task.FromResult(encounter);
    }

    public Task Save(Encounter encounter)
    {
        Encounters.AddOrUpdate(encounter.Id, encounter, (k, v) => v);

        return Task.CompletedTask;
    }
}