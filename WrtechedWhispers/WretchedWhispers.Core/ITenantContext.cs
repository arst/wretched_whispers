namespace WretchedWhispers.Core;

/// <summary>
/// Provides ambient tenant context for the current operation scope.
/// Set at the request boundary (endpoint filter) or app startup (console).
/// </summary>
public interface ITenantContext
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
