namespace WretchedWhispers.Core.Encounters;

public interface IEncountersRepository
{
    Task<Encounter?> Get(Guid encounterId, CancellationToken ct = default);
    Task Save(Encounter encounter, CancellationToken ct = default);
}
