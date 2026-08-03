namespace WretchedWhispers.Infrastructure;

/// <summary>
/// Provides the ambient authenticated-user context for the current operation scope. An infrastructure concern
/// (persistence tenancy), deliberately kept out of the domain — Core never reads the current user.
/// Set at the request boundary (endpoint filter) or app startup (console).
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// The authenticated user's ID. Throws <see cref="InvalidOperationException"/> if not set.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Sets the user ID for the current scope. Called once per request/operation.
    /// </summary>
    void SetUserId(string userId);
}
