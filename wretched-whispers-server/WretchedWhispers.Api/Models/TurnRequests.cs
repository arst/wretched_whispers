namespace WretchedWhispers.Api.Models;

public sealed record SubmitTurnRequest(Guid RequestId, string Message);
public sealed record TurnResponse(Guid TurnId, string Status, string StatusUrl, string EventsUrl, string? Error);
