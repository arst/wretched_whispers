namespace WretchedWhispers.Api.Services;

public interface ISessionContextLoader
{
    Task<SessionContext> LoadAsync(Guid sessionId, CancellationToken ct = default);
}
