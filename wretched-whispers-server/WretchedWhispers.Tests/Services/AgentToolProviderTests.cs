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
/// Verifies the Agent Framework tool provider offers every catalog tool in every stage of play, and that
/// its guards refuse a call outside the tool's stage (as the stage stands at the call), past the turn's
/// end, or past the call limit. Which tools belong to which stage is pinned (with explicit lists) in
/// GameToolCatalogTests.
/// </summary>
public class AgentToolProviderTests
{
    private readonly AgentToolProvider _provider;
    private readonly Mock<IEncountersRepository> _encountersRepo = new();
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();

    public AgentToolProviderTests()
    {
        var charsRepo = _charactersRepo.Object;
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

    /// <summary>A session past setup, in exploration unless an encounter is set on it: a character, and a
    /// campaign that has begun.</summary>
    private SessionContext PlayContext()
    {
        var characterId = Guid.NewGuid();
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        campaign.JoinGame(characterId);
        campaign.Start();
        _campaignsRepo.Setup(r => r.Get(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);

        var ctx = new SessionContext { SessionId = Guid.NewGuid(), Campaign = campaign };
        ctx.SetCharacterId(characterId);
        ctx.SetCampaignId(campaign.Id);
        return ctx;
    }

    [Fact]
    public void EveryStageOfPlay_OffersEveryTool()
    {
        var expected = GameToolCatalog.All.Select(d => $"{d.Group}.{d.Name}").ToArray();
        var exploration = PlayContext();
        var combat = PlayContext();
        combat.ActiveEncounter = StartedEncounter();
        var aftermath = ResolutionContextWithEncounter();

        foreach (var ctx in new[] { exploration, combat, aftermath })
            Assert.Equal(expected, _provider.GetTools(ctx).RegisteredFunctions);
    }

    [Fact]
    public void ABrokenOrEndedSession_IsOfferedNoTools()
    {
        var characterless = new SessionContext { SessionId = Guid.NewGuid() };
        var ended = PlayContext();
        ended.Campaign?.End();

        Assert.Empty(_provider.GetTools(characterless).Tools);
        Assert.Empty(_provider.GetTools(ended).Tools);
    }

    // CompleteResolution is limited to one call per turn; it needs only an active encounter id and the
    // encounters repository, so it is the lightest limited tool to drive end to end.
    private AIFunction CompleteResolutionTool(SessionContext ctx) =>
        _provider.GetTools(ctx).Tools.Single(t => t.Name == "CompleteResolution");

    private SessionContext ResolutionContextWithEncounter()
    {
        // Each load returns a fresh ended (unresolved) fight, so every call that reaches the domain
        // can resolve it — a second Resolve() on the same instance would throw for its own reasons.
        var ctx = PlayContext();
        var encounterId = Guid.NewGuid();
        _encountersRepo.Setup(r => r.Get(encounterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => EndedEncounter());
        ReopenAftermath(ctx, encounterId);
        return ctx;
    }

    /// <summary>Puts the turn back in the aftermath CompleteResolution just closed, so a second call WOULD
    /// succeed if it reached the domain.</summary>
    private static void ReopenAftermath(SessionContext ctx, Guid encounterId)
    {
        ctx.SetActiveEncounterId(encounterId);
        ctx.ActiveEncounter = EndedEncounter();
    }

    private static Encounter StartedEncounter()
    {
        var encounter = Encounter.Create("Chapel Duel", "A fight", EncounterType.Hostile,
            new Dice(new Mock<IRandomService>().Object));
        encounter.AddAdversary(new Adversary("Priest", new HitPoints(4, 4), new Armor(ArmorTier.Light), 7,
            new AttackProfile("Brass crucible", DiceExpr.D4)));
        encounter.StartEncounter();
        return encounter;
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
        ReopenAftermath(ctx, encounterId);

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
        ReopenAftermath(ctx, encounterId);
        await CompleteResolutionTool(ctx).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        _encountersRepo.Verify(r => r.Save(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LimitedTool_FailedCallDoesNotCount_SoTheModelCanRetry()
    {
        var ctx = ResolutionContextWithEncounter();
        var encounterId = ctx.ActiveEncounterId!.Value;
        ctx.ClearActiveEncounter();
        ctx.ActiveEncounter = EndedEncounter(); // still the aftermath, but with no encounter id to resolve
        var tool = CompleteResolutionTool(ctx);

        // Rejected by the tool's own validation: it never completed, so it must not use up the budget.
        var first = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal("No encounter to resolve.", first.Message);

        ReopenAftermath(ctx, encounterId);
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

        var ctx = PlayContext();
        ctx.SetActiveEncounterId(encounter.Id);
        return (ctx, _provider.GetTools(ctx).Tools);
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
                     // In stage now that the fight has begun, but the first round is the player's next message.
                     ("ResolveCombatRound", new { action = "Attack" }),
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

        var nextTurn = _provider.GetTools(ctx).Tools;

        Assert.NotNull(await Call(nextTurn, "Roll", new { diceExpression = "d6" }));
    }

    [Fact]
    public void ToolNames_AreBareFunctionNames_MatchingStagePrompts()
    {
        var (tools, _) = _provider.GetTools(PlayContext());

        // The stage prompts instruct the model to call e.g. "ChallengeCharacter" (no plugin prefix),
        // so the actual tool names must be the bare method names.
        Assert.Contains(tools, t => t.Name == "ChallengeCharacter");
        Assert.Contains(tools, t => t.Name == "CreateEncounter");
        Assert.Contains(tools, t => t.Name == "Roll");
    }

    // ---- the stage at the moment of the call ----

    [Fact]
    public async Task InAFight_AnExplorationTool_IsRefusedWithWhatToCallInstead()
    {
        var ctx = PlayContext();
        ctx.ActiveEncounter = StartedEncounter();
        var tools = _provider.GetTools(ctx).Tools;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Call(tools, "ChallengeCharacter", new { challengeDr = 12, abilityKind = "Strength", hoursTaken = 0 }).AsTask());

        Assert.StartsWith("ChallengeCharacter cannot be called during a fight.", ex.Message);
        Assert.Contains("ResolveCombatRound", ex.Message);
    }

    /// <summary>The sixth run's stuck game: an aftermath turn in which the player stabbed themself had no
    /// dice to roll, because a turn's tools were fixed when it began. Closing the aftermath now opens the
    /// tools for outside a fight in that same turn.</summary>
    [Fact]
    public async Task InTheAftermath_ADiceTool_IsRefused_UntilCompleteResolutionClosesIt()
    {
        var ctx = ResolutionContextWithEncounter();
        var tools = _provider.GetTools(ctx).Tools;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Call(tools, "Roll", new { diceExpression = "d4" }).AsTask());
        Assert.StartsWith("Roll cannot be called in the aftermath of a fight.", ex.Message);
        Assert.Contains("CompleteResolution", ex.Message);

        await Call(tools, "CompleteResolution");

        Assert.NotNull(await Call(tools, "Roll", new { diceExpression = "d4" }));
    }

    [Fact]
    public async Task ARoundThatEndsTheFight_OpensTheAftermathInTheSameTurn()
    {
        var dice = new Dice(new Mock<IRandomService>().Object);
        var character = TestCharacters.Create(dice);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        // A fight whose only enemy is already down: the round finds nobody to hit and ends it as won.
        var encounter = StartedEncounter();
        encounter.Adversaries[0].ReceiveDamage(4);
        _encountersRepo.Setup(r => r.Get(encounter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(encounter);

        var ctx = PlayContext();
        ctx.SetCharacterId(character.Id);
        ctx.SetActiveEncounterId(encounter.Id);
        // The copy loaded when the turn began, as the real repository's are: the round never touches it.
        ctx.ActiveEncounter = StartedEncounter();
        var tools = _provider.GetTools(ctx).Tools;

        await Call(tools, "ResolveCombatRound", new { action = "Attack" });

        Assert.Equal(SessionStage.Resolution, ctx.DeriveStage());
        await Call(tools, "CompleteResolution");
    }

    /// <summary>The opening turn: once ConfigureCampaign has begun the campaign, the map tools work in that
    /// same turn. Without this the turn stayed in setup, refused them, and three refusals failed the turn.</summary>
    [Fact]
    public async Task ConfiguringTheCampaign_BeginsPlayInTheSameTurn()
    {
        var characterId = Guid.NewGuid();
        var campaign = Campaign.Create(Difficulty.Grim, "Unnamed", "Not yet");
        campaign.JoinGame(characterId);
        _campaignsRepo.Setup(r => r.Get(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        // The copy loaded when the turn began, as the real repository's are: configuring never touches it.
        var loaded = Campaign.Create(Difficulty.Grim, "Unnamed", "Not yet");
        loaded.JoinGame(characterId);
        var ctx = new SessionContext { SessionId = Guid.NewGuid(), Campaign = loaded };
        ctx.SetCharacterId(characterId);
        ctx.SetCampaignId(campaign.Id);
        var tools = _provider.GetTools(ctx).Tools;

        await Call(tools, "ConfigureCampaign", new
        {
            name = "Ash",
            description = "The end",
            startingPlaceName = "Galgenbeck",
            startingPlaceType = "Town"
        });

        Assert.Equal(SessionStage.Exploration, ctx.DeriveStage());
        await Call(tools, "RecordJournalEntry", new { category = "Event", text = "Woke in Galgenbeck" });
    }
}
