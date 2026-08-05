namespace WretchedWhispers.Engine.Services;

public interface ISessionContextLoader
{
    /// <summary>Loads without an ownership check — for callers that have already established it (the
    /// turn worker, which runs against an already-claimed turn row).</summary>
    Task<SessionContext> LoadAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Loads only if the session belongs to the ambient user; null otherwise, covering both "does
    /// not exist" and "owned by someone else". Request handlers use this so establishing ownership
    /// and loading the context cost one read of the campaign instead of two.
    /// </summary>
    Task<SessionContext?> LoadOwnedAsync(Guid sessionId, CancellationToken ct = default);
}
