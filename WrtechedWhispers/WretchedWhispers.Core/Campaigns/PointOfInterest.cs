namespace WretchedWhispers.Core.Campaigns;

public enum PoiType { Town, Dungeon, Landmark, Ruin, Camp }

/// <summary>A charted place on the regional map. Positions live on an abstract 0-100 grid
/// (x west-east, y north-south with 0 at north) and never move once recorded.</summary>
public sealed record PointOfInterest(string Name, PoiType Type, int X, int Y, string? ConnectedTo, int Day);
