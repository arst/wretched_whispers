using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Core;

public interface IEncountersRepository
{
    Task<Encounter?> Get(Guid encounterId);
    Task Save(Encounter encounter);
}