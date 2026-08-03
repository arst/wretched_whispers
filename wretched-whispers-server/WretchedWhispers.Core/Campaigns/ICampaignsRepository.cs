namespace WretchedWhispers.Core.Campaigns;

public interface ICampaignsRepository
{
    /// <summary>
    /// Loads a campaign by its unique identifier.
    /// Returns null if no campaign exists with the specified ID.
    /// </summary>
    Task<Campaign?> Get(Guid campaignId);

    /// <summary>
    /// Saves the campaign, assigning tenant ownership from the ambient request/operation scope.
    /// (The domain does not know the current user; the infrastructure implementation supplies it.)
    /// For explicit userId control (tests, seeding), use <see cref="SaveCampaign(Campaign, string)"/>.
    /// </summary>
    Task SaveCampaign(Campaign newCampaign);

    /// <summary>
    /// Loads a campaign only if it belongs to the ambient tenant, as a single-row query.
    /// Null covers both "does not exist" and "owned by someone else" — callers surface 404
    /// for both to avoid leaking which sessions exist.
    /// </summary>
    Task<Campaign?> GetOwned(Guid campaignId, CancellationToken ct);

    /// <summary>
    /// Returns all campaigns belonging to the ambient tenant.
    /// </summary>
    Task<List<Campaign>> GetForUser(CancellationToken ct);

    /// <summary>
    /// Returns all campaigns belonging to the specified user. Use for tests and data seeding
    /// where no ambient tenant scope is available.
    /// </summary>
    Task<List<Campaign>> GetForUser(string userId);

    /// <summary>
    /// Saves the campaign with an explicitly provided userId. Use for tests and data seeding
    /// where no ambient tenant scope is available.
    /// </summary>
    Task SaveCampaign(Campaign campaign, string userId);
}