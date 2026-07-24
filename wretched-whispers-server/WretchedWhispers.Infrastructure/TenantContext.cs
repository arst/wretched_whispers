namespace WretchedWhispers.Infrastructure;

public sealed class TenantContext : ITenantContext
{
    private string? _userId;

    public string UserId => _userId
        ?? throw new InvalidOperationException(
            "UserId has not been set. Ensure ITenantContext is configured before accessing UserId.");

    public void SetUserId(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        _userId = userId;
    }
}
