using WretchedWhispers.Api.Deployment;
using Xunit;

namespace WretchedWhispers.Tests.Deployment;

public class DeploymentProfileTests
{
    [Theory]
    [InlineData("Server", true, false, false, false)]
    [InlineData("StandaloneContainer", false, true, true, false)]
    [InlineData("Desktop", false, true, true, true)]
    public void ProfileMapsToExpectedCapabilities(
        string name, bool identity, bool localAuth, bool settings, bool desktopShell)
    {
        var profile = DeploymentProfile.For(name);

        Assert.Equal(identity, profile.UsesIdentity);
        Assert.Equal(localAuth, profile.UsesLocalAuth);
        Assert.Equal(settings, profile.UsesSettings);
        Assert.Equal(desktopShell, profile.OpensDesktopShell);
    }

    [Fact]
    public void UnknownProfileIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DeploymentProfile.For("Unknown"));
}
