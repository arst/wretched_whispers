namespace WretchedWhispers.Api.Models;

public record PoiDto(string Name, string Type, int X, int Y, string? ConnectedTo);
