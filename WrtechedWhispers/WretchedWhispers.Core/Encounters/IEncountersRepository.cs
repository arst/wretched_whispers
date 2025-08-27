namespace WretchedWhispers.Core.Encounters;

public interface IEncountersRepository
{
    Task<Encounter?> Get(Guid encounterId);
    Task Save(Encounter encounter);
}