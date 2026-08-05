namespace WretchedWhispers.Api.Deployment;

/// <summary>
/// What this build is. The profile is fixed at compile time by the DeploymentProfile MSBuild
/// property (see WretchedWhispers.Api.csproj) — Server keeps ASP.NET Identity, the standalone
/// flavours run single-user local auth and own their settings file, and only Desktop opens a window.
/// Properties rather than consts: a const would let the compiler fold the dead branches in Program.cs
/// and warn CS0162 on every one of them.
/// </summary>
public static class DeploymentProfile
{
#if DEPLOYMENT_SERVER
    public const string Name = "Server";
    public static bool UsesIdentity => true;
    public static bool UsesLocalAuth => false;
    public static bool UsesSettings => false;
    public static bool OpensDesktopShell => false;
#elif DEPLOYMENT_STANDALONE_CONTAINER
    public const string Name = "StandaloneContainer";
    public static bool UsesIdentity => false;
    public static bool UsesLocalAuth => true;
    public static bool UsesSettings => true;
    public static bool OpensDesktopShell => false;
#elif DEPLOYMENT_DESKTOP
    public const string Name = "Desktop";
    public static bool UsesIdentity => false;
    public static bool UsesLocalAuth => true;
    public static bool UsesSettings => true;
    public static bool OpensDesktopShell => true;
#else
#error A deployment profile compile constant is required.
#endif
}
