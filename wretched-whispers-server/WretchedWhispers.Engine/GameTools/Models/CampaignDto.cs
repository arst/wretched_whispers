using System.ComponentModel;
using WretchedWhispers.Core.Campaigns.World;

namespace WretchedWhispers.Engine.GameTools.Models;

public record CampaignDto(
    [property: Description("The campaign's name")]
    string Name,
    [property: Description("Description of the campaign setting and context")]
    string Description,
    [property: Description("Current day number in the campaign")]
    int CurrentDay,
    [property: Description("Current hour of the day (0-23)")]
    int CurrentHour,
    [property: Description("List of miseries that have befallen the world")]
    List<Misery> Miseries);