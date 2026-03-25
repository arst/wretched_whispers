namespace WretchedWhispers.Api.Services;

/// <summary>
/// The 6 stages of a game session lifecycle.
/// Plan 01 owns the full implementation; this is the contract both plans agreed on.
/// </summary>
public enum SessionStage
{
    CharacterCreation,
    CampaignSetup,
    Exploration,
    Combat,
    Resolution,
    Ended
}
