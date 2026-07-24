namespace WretchedWhispers.Core.Campaigns;

/// <summary>A dead wretch remembered in the campaign's graveyard. The dead stay dead.</summary>
public sealed record FallenCharacter(Guid Id, string Name, int DayDied);
