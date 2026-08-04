namespace WretchedWhispers.Api.Deployment;

public readonly record struct DeploymentCapabilities(
    bool UsesIdentity,
    bool UsesLocalAuth,
    bool UsesSettings,
    bool OpensDesktopShell);

public static class DeploymentProfile
{
#if DEPLOYMENT_SERVER
    public const string Name = "Server";
#elif DEPLOYMENT_STANDALONE_CONTAINER
    public const string Name = "StandaloneContainer";
#elif DEPLOYMENT_DESKTOP
    public const string Name = "Desktop";
#else
#error A deployment profile compile constant is required.
#endif

    public static DeploymentCapabilities Current => For(Name);
    public static bool UsesIdentity => Current.UsesIdentity;
    public static bool UsesLocalAuth => Current.UsesLocalAuth;
    public static bool UsesSettings => Current.UsesSettings;
    public static bool OpensDesktopShell => Current.OpensDesktopShell;

    public static DeploymentCapabilities For(string name) => name switch
    {
        "Server" => new(true, false, false, false),
        "StandaloneContainer" => new(false, true, true, false),
        "Desktop" => new(false, true, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown deployment profile")
    };
}
