using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Verifies the Agent Framework tool provider exposes exactly the stage-scoped tool set
/// (the registered-function names mirror <see cref="GameToolCatalog"/>). Replaces the former
/// KernelFactoryTests after the SK→Agent Framework migration. Which tools belong to which stage is
/// pinned (with explicit lists) in GameToolCatalogTests — here we only prove the provider builds
/// what the catalog dictates.
/// </summary>
public class AgentToolProviderTests
{
    private readonly AgentToolProvider _provider;
    private readonly Mock<IEncountersRepository> _encountersRepo = new();
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();

    public AgentToolProviderTests()
    {
        var charsRepo = new Mock<ICharactersRepository>().Object;
        var campsRepo = _campaignsRepo.Object;
        var encsRepo = _encountersRepo.Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

        _provider = new AgentToolProvider(
            charsRepo,
            encsRepo,
            new CharacterService(charsRepo, dice),
            new CampaignService(campsRepo, charsRepo, dice),
            new EncounterService(dice, charsRepo, encsRepo),
            dice,
            NullLogger<AgentToolProvider>.Instance);
    }

    [Theory]
    [InlineData(SessionStage.CharacterCreation)]
    [InlineData(SessionStage.CampaignSetup)]
    [InlineData(SessionStage.Exploration)]
    [InlineData(SessionStage.Combat)]
    [InlineData(SessionStage.Resolution)]
    [InlineData(SessionStage.Ended)]
    public void EveryStage_BuildsExactlyTheCatalogToolSet(SessionStage stage)
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };

        var (tools, registered) = _provider.GetToolsForStage(ctx, stage);

        var expected = GameToolCatalog.ForStage(stage);
        Assert.Equal(expected.Count, tools.Count);
        Assert.Equal(expected.Select(d => $"{d.Group}.{d.Name}").ToArray(), registered);
    }

    // CompleteResolution is limited to one call per turn; it needs only an active encounter id and the
    // encounters repository, so it is the lightest limited tool to drive end to end.
    private AIFunction CompleteResolutionTool(SessionContext ctx) =>
        _provider.GetToolsForStage(ctx, SessionStage.Resolution).Tools.Single(t => t.Name == "CompleteResolution");

    private SessionContext ResolutionContextWithEncounter()
    {
        // Each load returns a fresh ended (unresolved) fight, so every call that reaches the domain
        // can resolve it — a second Resolve() on the same instance would throw for its own reasons.
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var encounterId = Guid.NewGuid();
        _encountersRepo.Setup(r => r.Get(encounterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => EndedEncounter());
        ctx.SetActiveEncounterId(encounterId);
        return ctx;
    }

    private static Encounter EndedEncounter()
    {
        var encounter = Encounter.Create("Chapel Duel", "Aftermath", EncounterType.Hostile,
            new Dice(new Mock<IRandomService>().Object));
        var priest = new Adversary("Priest", new HitPoints(4, 4), new Armor(ArmorTier.Light), 7,
            new AttackProfile("Brass crucible", DiceExpr.D4));
        encounter.AddAdversary(priest);
        encounter.StartEncounter();
        priest.ReceiveDamage(4);
        encounter.EndEncounter();
        return encounter;
    }

    [Fact]
    public async Task LimitedTool_SecondCallInATurn_FailsWithoutReachingTheDomain()
    {
        var ctx = ResolutionContextWithEncounter();
        var tool = CompleteResolutionTool(ctx);
        var encounterId = ctx.ActiveEncounterId!.Value;

        await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        ctx.SetActiveEncounterId(encounterId); // so a second call WOULD succeed if it reached the domain

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("CompleteResolution was already called this turn; narrate its result and stop.", ex.Message);
        _encountersRepo.Verify(r => r.Save(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LimitedTool_CounterResetsOnTheNextTurnsProviderCall()
    {
        var ctx = ResolutionContextWithEncounter();
        var encounterId = ctx.ActiveEncounterId!.Value;

        await CompleteResolutionTool(ctx).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        ctx.SetActiveEncounterId(encounterId);
        await CompleteResolutionTool(ctx).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        _encountersRepo.Verify(r => r.Save(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LimitedTool_FailedCallDoesNotCount_SoTheModelCanRetry()
    {
        var ctx = ResolutionContextWithEncounter();
        var encounterId = ctx.ActiveEncounterId!.Value;
        ctx.ClearActiveEncounter();
        var tool = CompleteResolutionTool(ctx);

        // Rejected by the tool's own validation: it never completed, so it must not use up the budget.
        var first = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("No encounter to resolve.", first.Message);

        ctx.SetActiveEncounterId(encounterId);
        await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        _encountersRepo.Verify(r => r.Save(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- StartEncounter ends the turn's game actions ----

    private const string TurnEndedFragment = "StartEncounter ended this turn's actions";

    /// <summary>An exploration turn with a hostile, unstarted encounter ready to begin, and a campaign
    /// the journal can be written to.</summary>
    private (SessionContext Ctx, IReadOnlyList<AIFunction> Tools) ExplorationTurnWithReadyEncounter()
    {
        var dice = new Dice(new Mock<IRandomService>().Object);
        var encounter = Encounter.Create("Road Ambush", "Bandits", EncounterType.Hostile, dice);
        encounter.AddAdversary(new Adversary("Bandit", new HitPoints(4, 4), new Armor(ArmorTier.None), 7,
            new AttackProfile("Rusty knife", DiceExpr.D4)));
        _encountersRepo.Setup(r => r.Get(encounter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(encounter);

        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        _campaignsRepo.Setup(r => r.Get(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.SetActiveEncounterId(encounter.Id);
        return (ctx, _provider.GetToolsForStage(ctx, SessionStage.Exploration).Tools);
    }

    private static ValueTask<object?> Call(IReadOnlyList<AIFunction> tools, string name, object? args = null) =>
        tools.Single(t => t.Name == name).InvokeAsync(
            new AIFunctionArguments(args is null
                ? new Dictionary<string, object?>()
                : System.Text.Json.JsonSerializer.SerializeToElement(args).EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object?)p.Value.Clone())),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task AfterStartEncounter_OtherGameTools_AreRejectedBeforeReachingTheDomain()
    {
        var (_, tools) = ExplorationTurnWithReadyEncounter();
        await Call(tools, "Roll", new { diceExpression = "d6" }); // fine before combat starts

        await Call(tools, "StartEncounter");

        // The model reaching for a bare roll or an ability test to "resolve" the declared attack.
        foreach (var (name, args) in new (string, object)[]
                 {
                     ("Roll", new { diceExpression = "d20" }),
                     ("ChallengeCharacter", new { challengeDr = 12, abilityKind = "Strength", hoursTaken = 0 }),
                     ("AdvanceTime", new { hours = 1 }),
                 })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Call(tools, name, args).AsTask());
            Assert.Contains(TurnEndedFragment, ex.Message);
            Assert.Contains($"{name} cannot be called now", ex.Message);
        }
    }

    [Fact]
    public async Task AfterStartEncounter_RecordKeepingTools_StillWork()
    {
        var (_, tools) = ExplorationTurnWithReadyEncounter();
        await Call(tools, "StartEncounter");

        var result = await Call(tools, "RecordJournalEntry", new { category = "Event", text = "Bandits on the road" });

        Assert.Contains("Recorded", result?.ToString());
        _campaignsRepo.Verify(r => r.SaveCampaign(It.IsAny<Campaign>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartEncounterThatFails_EndsNothing()
    {
        var (ctx, tools) = ExplorationTurnWithReadyEncounter();
        ctx.ClearActiveEncounter(); // no encounter: StartEncounter is rejected by its own validation

        await Assert.ThrowsAsync<InvalidOperationException>(() => Call(tools, "StartEncounter").AsTask());

        Assert.NotNull(await Call(tools, "Roll", new { diceExpression = "d6" }));
    }

    [Fact]
    public async Task TurnEnd_DoesNotCarryIntoTheNextTurnsTools()
    {
        var (ctx, tools) = ExplorationTurnWithReadyEncounter();
        await Call(tools, "StartEncounter");

        var nextTurn = _provider.GetToolsForStage(ctx, SessionStage.Exploration).Tools;

        Assert.NotNull(await Call(nextTurn, "Roll", new { diceExpression = "d6" }));
    }

    [Fact]
    public void ToolNames_AreBareFunctionNames_MatchingStagePrompts()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = _provider.GetToolsForStage(ctx, SessionStage.Exploration);

        // The stage prompts instruct the model to call e.g. "ChallengeCharacter" (no plugin prefix),
        // so the actual tool names must be the bare method names.
        Assert.Contains(tools, t => t.Name == "ChallengeCharacter");
        Assert.Contains(tools, t => t.Name == "CreateEncounter");
        Assert.Contains(tools, t => t.Name == "Roll");
    }
}
