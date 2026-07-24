using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Api.Models;

public record CreateSessionRequest(Difficulty Difficulty = Difficulty.Grim);
