namespace WretchedWhispers.Infrastructure;

public sealed class UserContext : IUserContext
{
    private string? _userId;

    public string UserId => _userId
        ?? throw new InvalidOperationException(
            "UserId has not been set. Ensure IUserContext is configured before accessing UserId.");

    public void SetUserId(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        _userId = userId;
    }
}
