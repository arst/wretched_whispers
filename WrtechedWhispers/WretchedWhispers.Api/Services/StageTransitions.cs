namespace WretchedWhispers.Api.Services;

public static class StageTransitions
{
    private static readonly Dictionary<(SessionStage, string, string), SessionStage> Transitions = new()
    {
        { (SessionStage.CharacterCreation, "Character", "CreateCharacter"), SessionStage.CampaignSetup },
        { (SessionStage.CampaignSetup, "Campaign", "StartCampaign"), SessionStage.Exploration },
        { (SessionStage.Exploration, "Encounter", "StartEncounter"), SessionStage.Combat },
        { (SessionStage.Combat, "Encounter", "EndEncounter"), SessionStage.Resolution },
        { (SessionStage.Resolution, "Resolution", "CompleteResolution"), SessionStage.Exploration },
    };

    public static SessionStage? GetNextStage(SessionStage current, string pluginName, string functionName)
    {
        return Transitions.TryGetValue((current, pluginName, functionName), out var next) ? next : null;
    }
}
