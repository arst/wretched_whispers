namespace WretchedWhispers.Core.Campaigns;

/// <summary>
/// All ownership comes from the ambient <c>IUserContext</c> of the current scope — set by the
/// request's endpoint filter (or explicitly in tests/evals). The domain never passes user ids.
/// </summary>
public interface ICampaignsRepository
{
    /// <summary>
    /// Loads a campaign by its unique identifier, regardless of owner.
    /// Returns null if no campaign exists with the specified ID.
    /// </summary>
    Task<Campaign?> Get(Guid campaignId, CancellationToken ct = default);

    /// <summary>
    /// Saves the campaign, stamping ownership from the ambient user context.
    /// </summary>
    Task SaveCampaign(Campaign campaign, CancellationToken ct = default);

    /// <summary>
    /// Loads a campaign only if it belongs to the ambient user, as a single-row query.
    /// Null covers both "does not exist" and "owned by someone else" — callers surface 404
    /// for both to avoid leaking which sessions exist.
    /// </summary>
    Task<Campaign?> GetOwned(Guid campaignId, CancellationToken ct);

    /// <summary>
    /// Returns all campaigns belonging to the ambient user.
    /// </summary>
    Task<List<Campaign>> GetForUser(CancellationToken ct);
}
