namespace WretchedWhispers.Api.Services;

/// <summary>
/// Holds the current session's entity IDs so wrapper plugins can auto-fill them.
/// Scoped per request/turn. Plan 01 owns the full implementation; this is the
/// contract both plans agreed on.
/// </summary>
public sealed class SessionContext
{
    public Guid? CharacterId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? ActiveEncounterId { get; private set; }

    public void SetCharacterId(Guid id) => CharacterId = id;
    public void SetCampaignId(Guid id) => CampaignId = id;
    public void SetActiveEncounterId(Guid id) => ActiveEncounterId = id;
    public void ClearActiveEncounter() => ActiveEncounterId = null;
}
