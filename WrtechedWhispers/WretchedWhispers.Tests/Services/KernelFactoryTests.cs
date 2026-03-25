#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class KernelFactoryTests
{
    private readonly KernelFactory _factory;

    public KernelFactoryTests()
    {
        var services = new ServiceCollection();

        // Register mock operations interfaces (used by wrapper plugin constructors)
        services.AddSingleton(new Mock<ICharacterOperations>().Object);
        services.AddSingleton(new Mock<ICampaignOperations>().Object);
        services.AddSingleton(new Mock<IEncounterOperations>().Object);
        services.AddSingleton(new Mock<IDiceOperations>().Object);
        services.AddSingleton(new Mock<ICampaignsRepository>().Object);
        services.AddSingleton(new Mock<IEncountersRepository>().Object);

        var settings = Options.Create(new AzureOpenAiSettings
        {
            ChatModelDeployment = "test-deployment",
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key"
        });

        var sp = services.BuildServiceProvider();

        _factory = new KernelFactory(
            sp,
            settings,
            NullLogger<KernelFactory>.Instance);
    }

    [Fact]
    public void CharacterCreation_HasExactly1Function()
    {
        var ctx = CreateContext(SessionStage.CharacterCreation);
        var (_, registered) = _factory.CreateForStage(ctx, SessionStage.CharacterCreation);

        Assert.Single(registered);
        Assert.Contains("Character.CreateCharacter", registered);
    }

    [Fact]
    public void CampaignSetup_HasExactly2Functions()
    {
        var ctx = CreateContext(SessionStage.CampaignSetup);
        var (_, registered) = _factory.CreateForStage(ctx, SessionStage.CampaignSetup);

        Assert.Equal(2, registered.Length);
        Assert.Contains("Campaign.ConfigureCampaign", registered);
        Assert.Contains("Campaign.StartCampaign", registered);
    }

    [Fact]
    public void Exploration_HasExactly10Functions()
    {
        var ctx = CreateContext(SessionStage.Exploration);
        var (_, registered) = _factory.CreateForStage(ctx, SessionStage.Exploration);

        Assert.Equal(10, registered.Length);
        Assert.Contains("Character.ChallengeCharacter", registered);
        Assert.Contains("Character.AddItemToCharacterInventory", registered);
        Assert.Contains("Character.BuyItem", registered);
        Assert.Contains("Character.CastScroll", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Campaign.Rest", registered);
        Assert.Contains("Encounter.CreateEncounter", registered);
        Assert.Contains("Encounter.AddAdversaryToEncounter", registered);
        Assert.Contains("Encounter.StartEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
    }

    [Fact]
    public void Combat_HasExactly4Functions()
    {
        var ctx = CreateContext(SessionStage.Combat);
        var (_, registered) = _factory.CreateForStage(ctx, SessionStage.Combat);

        Assert.Equal(4, registered.Length);
        Assert.Contains("Encounter.AttackPlayer", registered);
        Assert.Contains("Encounter.AttackAdversary", registered);
        Assert.Contains("Encounter.EndEncounter", registered);
        Assert.Contains("Dice.Roll", registered);
    }

    [Fact]
    public void Resolution_HasExactly9Functions()
    {
        var ctx = CreateContext(SessionStage.Resolution);
        var (_, registered) = _factory.CreateForStage(ctx, SessionStage.Resolution);

        Assert.Equal(9, registered.Length);
        Assert.Contains("Character.AddItemToCharacterInventory", registered);
        Assert.Contains("Character.RemoveItemFromCharacterInventory", registered);
        Assert.Contains("Character.InfectCharacter", registered);
        Assert.Contains("Character.CureInfection", registered);
        Assert.Contains("Character.ImproveCharacterAbility", registered);
        Assert.Contains("Character.DegradeCharacterAbility", registered);
        Assert.Contains("Campaign.AdvanceTime", registered);
        Assert.Contains("Campaign.Rest", registered);
        Assert.Contains("Resolution.CompleteResolution", registered);
    }

    [Fact]
    public void Ended_HasZeroFunctions()
    {
        var ctx = CreateContext(SessionStage.Ended);
        var (kernel, registered) = _factory.CreateForStage(ctx, SessionStage.Ended);

        Assert.Empty(registered);
        Assert.Empty(kernel.Plugins);
    }

    private static SessionContext CreateContext(SessionStage targetStage)
    {
        // Create a SessionContext and configure it so DeriveStage() returns the target.
        // We don't actually call DeriveStage() in KernelFactory -- stage is passed directly.
        // But we need a valid SessionContext for the wrapper plugins.
        return new SessionContext { SessionId = Guid.NewGuid() };
    }
}
