# GameSessionService Refactoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the 488-line GameSessionService god-class into focused services, replace manual SSE with .NET 10 native support, add structured logging + OTel traces, and fix the stage runaway bug by only registering stage-appropriate functions on the kernel.

**Architecture:** Coordinator pattern — a thin TurnCoordinator calls focused services (SessionContextLoader, KernelFactory, AgentExecutor, StateUpdateMapper) in sequence. Each service has one job, gets ILogger, and is independently testable. The kernel physically contains only the functions allowed for the current stage.

**Tech Stack:** .NET 10, Semantic Kernel, ASP.NET Core native SSE (`Results.ServerSentEvents`), OpenTelemetry, Polly resilience

**Spec:** `docs/superpowers/specs/2026-03-25-game-session-refactoring-design.md`

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `WretchedWhispers.Api/Models/GameTurnEvent.cs` | Typed SSE event hierarchy |
| `WretchedWhispers.Api/Models/AzureOpenAiSettings.cs` | Strongly-typed config |
| `WretchedWhispers.Api/Services/SessionContextLoader.cs` | Load domain state into SessionContext |
| `WretchedWhispers.Api/Services/KernelFactory.cs` | Build stage-scoped kernel |
| `WretchedWhispers.Api/Services/AgentExecutor.cs` | Create agent, stream response, extract tool results |
| `WretchedWhispers.Api/Services/StateUpdateMapper.cs` | Map domain state to StateUpdate event |
| `WretchedWhispers.Api/Services/TurnCoordinator.cs` | Orchestrate turn, own transaction |
| `WretchedWhispers.Tests/Services/SessionContextLoaderTests.cs` | Unit tests |
| `WretchedWhispers.Tests/Services/KernelFactoryTests.cs` | Stage-to-function regression tests |
| `WretchedWhispers.Tests/Services/StateUpdateMapperTests.cs` | Mapping tests |
| `WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs` | Orchestration tests |

### Modified Files
| File | Changes |
|------|---------|
| `WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs` | Register new services, bind AzureOpenAiSettings |
| `WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs` | Add custom ActivitySource |
| `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` | Native SSE endpoint, use StateUpdateMapper for GetSessionDetail |
| `WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs` | Reuse AgentExecutor, accept GameTurnEvent |

### Deleted Files
| File | Replaced By |
|------|------------|
| `WretchedWhispers.Api/Services/GameSessionService.cs` | TurnCoordinator + services |
| `WretchedWhispers.Api/Services/StageTransitionFilter.cs` | KernelFactory stage scoping |
| `WretchedWhispers.Api/Services/StagePluginRegistry.cs` | KernelFactory |
| `WretchedWhispers.Api/Models/SseEvent.cs` | GameTurnEvent |
| `WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs` | KernelFactoryTests |
| `WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs` | KernelFactoryTests |

---

### Task 1: GameTurnEvent model + AzureOpenAiSettings

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Models/GameTurnEvent.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Api/Models/AzureOpenAiSettings.cs`

- [ ] **Step 1: Create GameTurnEvent hierarchy**

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Models/GameTurnEvent.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.Models;

[JsonDerivedType(typeof(NarrativeChunk))]
[JsonDerivedType(typeof(ToolResult))]
[JsonDerivedType(typeof(StateUpdate))]
[JsonDerivedType(typeof(TurnError))]
[JsonDerivedType(typeof(TurnDone))]
public abstract record GameTurnEvent(
    [property: JsonIgnore] string EventType);

public record NarrativeChunk(string Text) : GameTurnEvent("narrative");

public record ToolResult(string Function, object Result) : GameTurnEvent("tool_result");

public record StateUpdate(
    Guid? CampaignId,
    int CurrentDay,
    int CurrentHour,
    Guid? CharacterId,
    string? CharacterName,
    int? CharacterHp,
    int? CharacterMaxHp,
    int? CharacterStrength,
    int? CharacterAgility,
    int? CharacterPresence,
    int? CharacterToughness,
    string? CharacterWeapon,
    string? CharacterArmor,
    string[]? CharacterInventory,
    int MiseryCount,
    string Stage,
    string Status,
    bool HasLostEye,
    bool HasStabbedLung,
    bool HasBrokenHand,
    bool HasCrushedFoot,
    bool HasSeveredArm,
    bool HasSmashedFace,
    bool IsInfected,
    bool IsDizzyFromMagic,
    bool IsEncumbered,
    bool IsDead,
    string ArmorTier,
    bool HasShield,
    bool IsShieldBroken,
    bool WorldEnded) : GameTurnEvent("state_update");

public record TurnError(string Message) : GameTurnEvent("error");

public record TurnDone() : GameTurnEvent("done");
```

- [ ] **Step 2: Create AzureOpenAiSettings**

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Models/AzureOpenAiSettings.cs
namespace WretchedWhispers.Api.Models;

public sealed class AzureOpenAiSettings
{
    public string ChatModelDeployment { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string ApiKey { get; init; } = "";
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors (new files have no consumers yet)

- [ ] **Step 4: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Models/GameTurnEvent.cs WrtechedWhispers/WretchedWhispers.Api/Models/AzureOpenAiSettings.cs
git commit -m "feat: add GameTurnEvent hierarchy and AzureOpenAiSettings"
```

---

### Task 2: StateUpdateMapper

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/StateUpdateMapper.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Tests/Services/StateUpdateMapperTests.cs`

- [ ] **Step 1: Write the test**

```csharp
// WrtechedWhispers/WretchedWhispers.Tests/Services/StateUpdateMapperTests.cs
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using Moq;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class StateUpdateMapperTests
{
    [Fact]
    public void Map_WithNoCharacter_ReturnsNullCharacterFields()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Test", "desc");
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = campaign;

        var result = StateUpdateMapper.Map(context);

        Assert.NotNull(result);
        Assert.Null(result.CharacterId);
        Assert.Null(result.CharacterName);
        Assert.Equal("charactercreation", result.Stage);
        Assert.Equal("character-creation", result.Status);
    }

    [Fact]
    public void Map_WithNoCampaign_ReturnsNullCampaignFields()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        var result = StateUpdateMapper.Map(context);

        Assert.Null(result.CampaignId);
        Assert.Equal(0, result.CurrentDay);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StateUpdateMapperTests" --nologo -v q`
Expected: Build error — `StateUpdateMapper` does not exist

- [ ] **Step 3: Implement StateUpdateMapper**

Extract the 110-line mapping block from `GameSessionService.cs` lines 187-300 into a static method. The mapper reads from `SessionContext` (which has Campaign, Character, ActiveEncounter already loaded).

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Services/StateUpdateMapper.cs
using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

namespace WretchedWhispers.Api.Services;

public static class StateUpdateMapper
{
    public static StateUpdate Map(SessionContext context)
    {
        var campaign = context.Campaign;
        var character = context.Character;

        Guid? characterId = null;
        string? characterName = null;
        int? characterHp = null;
        int? characterMaxHp = null;
        int? characterStrength = null;
        int? characterAgility = null;
        int? characterPresence = null;
        int? characterToughness = null;
        string? characterWeapon = null;
        string? characterArmor = null;
        string[]? characterInventory = null;
        bool hasLostEye = false, hasStabbedLung = false, hasBrokenHand = false;
        bool hasCrushedFoot = false, hasSeveredArm = false, hasSmashedFace = false;
        bool isInfected = false, isDizzyFromMagic = false, isEncumbered = false, isDead = false;
        string armorTier = "none";
        bool hasShield = false, isShieldBroken = false;

        if (character is not null)
        {
            characterId = character.Id;
            characterHp = character.Hp.Current;
            characterMaxHp = character.Hp.Max;
            characterName = character.Name;
            characterStrength = character.Abilities.Strength.Modifier;
            characterAgility = character.Abilities.Agility.Modifier;
            characterPresence = character.Abilities.Presence.Modifier;
            characterToughness = character.Abilities.Toughness.Modifier;
            characterWeapon = character.Weapon.Kind.ToString();
            characterArmor = character.Armor.Tier switch
            {
                NoArmorTier => "None",
                LightArmorTier => "Light Armor",
                MediumArmorTier => "Medium Armor",
                HeavyArmorTier => "Heavy Armor",
                _ => "Unknown"
            };
            characterInventory = character.Inventory.InventoryItems
                .Select(i => i.Description).ToArray();
            hasLostEye = character.HasLostEye;
            hasStabbedLung = character.HasStabbedLung;
            hasBrokenHand = character.HasBrokenHand;
            hasCrushedFoot = character.HasCrushedFoot;
            hasSeveredArm = character.HasSeveredArm;
            hasSmashedFace = character.HasSmashedFace;
            isInfected = character.IsInfected;
            isDizzyFromMagic = character.IsDizzyFromMagic;
            isEncumbered = character.IsEncumbered;
            isDead = character.IsDead;
            armorTier = character.Armor.Tier switch
            {
                NoArmorTier => "none",
                LightArmorTier => "light",
                MediumArmorTier => "medium",
                HeavyArmorTier => "heavy",
                _ => "none"
            };
            hasShield = character.Shield is not null;
            isShieldBroken = character.Shield?.IsBroken ?? false;
        }

        return new StateUpdate(
            CampaignId: campaign?.Id,
            CurrentDay: campaign?.CurrentDay ?? 0,
            CurrentHour: campaign?.CurrentHour ?? 0,
            CharacterId: characterId,
            CharacterName: characterName,
            CharacterHp: characterHp,
            CharacterMaxHp: characterMaxHp,
            CharacterStrength: characterStrength,
            CharacterAgility: characterAgility,
            CharacterPresence: characterPresence,
            CharacterToughness: characterToughness,
            CharacterWeapon: characterWeapon,
            CharacterArmor: characterArmor,
            CharacterInventory: characterInventory,
            MiseryCount: campaign?.Miseries.Count ?? 0,
            Stage: context.DeriveStage().ToString().ToLowerInvariant(),
            Status: DeriveStatus(campaign),
            HasLostEye: hasLostEye,
            HasStabbedLung: hasStabbedLung,
            HasBrokenHand: hasBrokenHand,
            HasCrushedFoot: hasCrushedFoot,
            HasSeveredArm: hasSeveredArm,
            HasSmashedFace: hasSmashedFace,
            IsInfected: isInfected,
            IsDizzyFromMagic: isDizzyFromMagic,
            IsEncumbered: isEncumbered,
            IsDead: isDead,
            ArmorTier: armorTier,
            HasShield: hasShield,
            IsShieldBroken: isShieldBroken,
            WorldEnded: campaign?.WorldEnded ?? false);
    }

    private static string DeriveStatus(Campaign? campaign)
    {
        if (campaign is null) return "character-creation";
        if (campaign.Players.Count == 0) return "character-creation";
        if (campaign.IsActive()) return "in-progress";
        return "ended";
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StateUpdateMapperTests" --nologo -v q`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/StateUpdateMapper.cs WrtechedWhispers/WretchedWhispers.Tests/Services/StateUpdateMapperTests.cs
git commit -m "feat: extract StateUpdateMapper from GameSessionService"
```

---

### Task 3: SessionContextLoader

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/SessionContextLoader.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Tests/Services/SessionContextLoaderTests.cs`

- [ ] **Step 1: Write the test**

Test that the loader correctly builds SessionContext from repository data:

```csharp
// WrtechedWhispers/WretchedWhispers.Tests/Services/SessionContextLoaderTests.cs
using Moq;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class SessionContextLoaderTests
{
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();

    private SessionContextLoader CreateLoader() =>
        new(_campaignsRepo.Object, _charactersRepo.Object, _encountersRepo.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionContextLoader>.Instance);

    [Fact]
    public async Task Load_NoCampaign_ReturnsEmptyContext()
    {
        var sessionId = Guid.NewGuid();
        _campaignsRepo.Setup(r => r.Get(sessionId)).ReturnsAsync((Campaign?)null);

        var loader = CreateLoader();
        var ctx = await loader.LoadAsync(sessionId);

        Assert.Equal(sessionId, ctx.SessionId);
        Assert.Null(ctx.Campaign);
        Assert.Null(ctx.Character);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }

    [Fact]
    public async Task Load_CampaignWithNoPlayers_ReturnsCampaignSetupNotReached()
    {
        var sessionId = Guid.NewGuid();
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Test", "desc");
        typeof(Campaign).GetProperty("Id")!.SetValue(campaign, sessionId);
        _campaignsRepo.Setup(r => r.Get(sessionId)).ReturnsAsync(campaign);

        var loader = CreateLoader();
        var ctx = await loader.LoadAsync(sessionId);

        Assert.NotNull(ctx.Campaign);
        Assert.Null(ctx.CharacterId);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SessionContextLoaderTests" --nologo -v q`
Expected: Build error — `SessionContextLoader` does not exist

- [ ] **Step 3: Implement SessionContextLoader**

Extract `BuildSessionContextAsync` from `GameSessionService.cs` lines 338-375. Add `ILogger<SessionContextLoader>` for state mutation logging.

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Services/SessionContextLoader.cs
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.Services;

public sealed class SessionContextLoader(
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository,
    IEncountersRepository encountersRepository,
    ILogger<SessionContextLoader> logger)
{
    public async Task<SessionContext> LoadAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = new SessionContext { SessionId = sessionId };

        var campaign = await campaignsRepository.Get(sessionId);
        if (campaign is null)
        {
            logger.LogInformation("Session {SessionId}: no campaign found", sessionId);
            return context;
        }

        context.Campaign = campaign;
        context.SetCampaignId(campaign.Id);

        var firstPlayerId = campaign.Players.FirstOrDefault();
        if (firstPlayerId != Guid.Empty)
        {
            var character = await charactersRepository.Get(firstPlayerId, ct);
            if (character is not null)
            {
                context.Character = character;
                context.SetCharacterId(character.Id);
            }
        }

        foreach (var encId in campaign.EncounterIds.Reverse())
        {
            var enc = await encountersRepository.Get(encId);
            if (enc is not null && enc.IsStarted && !enc.IsResolved)
            {
                context.ActiveEncounter = enc;
                context.SetActiveEncounterId(enc.Id);
                break;
            }
        }

        var stage = context.DeriveStage();
        logger.LogInformation(
            "Session {SessionId}: loaded context — Stage={Stage}, CharacterId={CharacterId}, CampaignId={CampaignId}, EncounterId={EncounterId}",
            sessionId, stage, context.CharacterId, context.CampaignId, context.ActiveEncounterId);

        return context;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SessionContextLoaderTests" --nologo -v q`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/SessionContextLoader.cs WrtechedWhispers/WretchedWhispers.Tests/Services/SessionContextLoaderTests.cs
git commit -m "feat: extract SessionContextLoader from GameSessionService"
```

---

### Task 4: KernelFactory with stage-scoped functions

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/KernelFactory.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Tests/Services/KernelFactoryTests.cs`

- [ ] **Step 1: Write the regression tests**

One test per stage verifying the exact function list. This is the hard gate that prevents the runaway bug.

```csharp
// WrtechedWhispers/WretchedWhispers.Tests/Services/KernelFactoryTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Semantic;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class KernelFactoryTests
{
    private readonly KernelFactory _factory;

    public KernelFactoryTests()
    {
        // Build a minimal IServiceProvider with mock plugins
        var services = new ServiceCollection();
        services.AddScoped(_ => new Mock<CharacterPlugin>(
            new Mock<ICharactersRepository>().Object,
            new Mock<CharacterCreationService>(
                new Mock<ICharactersRepository>().Object,
                TestDice()).Object,
            new Mock<CharacterService>().Object,
            TestDice()).Object);
        services.AddScoped(_ => new Mock<CampaignPlugin>(
            new Mock<ICampaignsRepository>().Object,
            new Mock<ICharactersRepository>().Object,
            new Mock<CampaignService>(
                new Mock<ICampaignsRepository>().Object,
                TestDice()).Object).Object);
        services.AddScoped(_ => new Mock<EncounterPlugin>(
            new Mock<IEncountersRepository>().Object,
            TestDice()).Object);
        services.AddScoped(_ => new Mock<DicePlugin>(TestDice()).Object);
        services.AddScoped(_ => new Mock<ICampaignsRepository>().Object);
        services.AddScoped(_ => new Mock<IEncountersRepository>().Object);
        var sp = services.BuildServiceProvider();

        var settings = Options.Create(new AzureOpenAiSettings
        {
            ChatModelDeployment = "test", Endpoint = "https://test.openai.azure.com/", ApiKey = "test"
        });

        _factory = new KernelFactory(sp, settings, NullLogger<KernelFactory>.Instance);
    }

    private static Dice TestDice() =>
        new(new Mock<IRandomService>().Object);

    [Fact]
    public void CharacterCreation_HasOnlyCreateCharacter()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, _) = _factory.Build(ctx, SessionStage.CharacterCreation);

        var functions = kernel.Plugins.GetFunctionsMetadata()
            .Select(f => $"{f.PluginName}.{f.Name}").ToList();

        Assert.Single(functions);
        Assert.Contains("Character.CreateCharacter", functions);
    }

    [Fact]
    public void CampaignSetup_HasOnlyConfigureAndStart()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(Guid.NewGuid());
        var (kernel, _) = _factory.Build(ctx, SessionStage.CampaignSetup);

        var functions = kernel.Plugins.GetFunctionsMetadata()
            .Select(f => $"{f.PluginName}.{f.Name}").ToList();

        Assert.Equal(2, functions.Count);
        Assert.Contains("Campaign.ConfigureCampaign", functions);
        Assert.Contains("Campaign.StartCampaign", functions);
    }

    [Fact]
    public void Exploration_HasCorrectFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, _) = _factory.Build(ctx, SessionStage.Exploration);

        var functions = kernel.Plugins.GetFunctionsMetadata()
            .Select(f => $"{f.PluginName}.{f.Name}").OrderBy(f => f).ToList();

        Assert.Equal(10, functions.Count);
        Assert.Contains("Character.ChallengeCharacter", functions);
        Assert.Contains("Character.AddItemToCharacterInventory", functions);
        Assert.Contains("Character.BuyItem", functions);
        Assert.Contains("Character.CastScroll", functions);
        Assert.Contains("Campaign.AdvanceTime", functions);
        Assert.Contains("Campaign.Rest", functions);
        Assert.Contains("Encounter.CreateEncounter", functions);
        Assert.Contains("Encounter.AddAdversaryToEncounter", functions);
        Assert.Contains("Encounter.StartEncounter", functions);
        Assert.Contains("Dice.Roll", functions);
        Assert.DoesNotContain("Character.CreateCharacter", functions);
        Assert.DoesNotContain("Campaign.ConfigureCampaign", functions);
    }

    [Fact]
    public void Combat_HasOnlyCombatFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, _) = _factory.Build(ctx, SessionStage.Combat);

        var functions = kernel.Plugins.GetFunctionsMetadata()
            .Select(f => $"{f.PluginName}.{f.Name}").OrderBy(f => f).ToList();

        Assert.Equal(4, functions.Count);
        Assert.Contains("Encounter.AttackPlayer", functions);
        Assert.Contains("Encounter.AttackAdversary", functions);
        Assert.Contains("Encounter.EndEncounter", functions);
        Assert.Contains("Dice.Roll", functions);
    }

    [Fact]
    public void Resolution_HasCorrectFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, _) = _factory.Build(ctx, SessionStage.Resolution);

        var functions = kernel.Plugins.GetFunctionsMetadata()
            .Select(f => $"{f.PluginName}.{f.Name}").OrderBy(f => f).ToList();

        Assert.Equal(9, functions.Count);
        Assert.Contains("Resolution.CompleteResolution", functions);
        Assert.Contains("Campaign.AdvanceTime", functions);
        Assert.Contains("Character.AddItemToCharacterInventory", functions);
        Assert.DoesNotContain("Character.CreateCharacter", functions);
        Assert.DoesNotContain("Campaign.StartCampaign", functions);
    }

    [Fact]
    public void Ended_HasNoFunctions()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (kernel, _) = _factory.Build(ctx, SessionStage.Ended);

        var functions = kernel.Plugins.GetFunctionsMetadata().ToList();
        Assert.Empty(functions);
    }
}
```

Note: The constructor setup for mock plugins above is pseudocode — the exact constructor signatures depend on the plugin classes. The implementer must read each plugin's constructor and create appropriate mocks. The critical assertion is the function list per stage.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~KernelFactoryTests" --nologo -v q`
Expected: Build error — `KernelFactory` does not exist

- [ ] **Step 3: Implement KernelFactory**

The factory builds wrapper plugins (same as current `BuildKernelForSession`) but only imports the functions allowed for the given stage. Uses `KernelPluginFactory.CreateFromFunctions()` to selectively register functions.

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Services/KernelFactory.cs
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Semantic;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

namespace WretchedWhispers.Api.Services;

public sealed class KernelFactory(
    IServiceProvider serviceProvider,
    IOptions<AzureOpenAiSettings> azureSettings,
    ILogger<KernelFactory> logger)
{
    internal static readonly ActivitySource ActivitySource = new("WretchedWhispers.GameTurn");

    // Stage → (PluginName, FunctionNames[])
    private static readonly Dictionary<SessionStage, (string Plugin, string[] Functions)[]> StageFunctions = new()
    {
        [SessionStage.CharacterCreation] = [("Character", ["CreateCharacter"])],
        [SessionStage.CampaignSetup] = [("Campaign", ["ConfigureCampaign", "StartCampaign"])],
        [SessionStage.Exploration] =
        [
            ("Character", ["ChallengeCharacter", "AddItemToCharacterInventory", "BuyItem", "CastScroll"]),
            ("Campaign", ["AdvanceTime", "Rest"]),
            ("Encounter", ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"]),
            ("Dice", ["Roll"])
        ],
        [SessionStage.Combat] =
        [
            ("Encounter", ["AttackPlayer", "AttackAdversary", "EndEncounter"]),
            ("Dice", ["Roll"])
        ],
        [SessionStage.Resolution] =
        [
            ("Character", ["AddItemToCharacterInventory", "RemoveItemFromCharacterInventory",
                "InfectCharacter", "CureInfection", "ImproveCharacterAbility", "DegradeCharacterAbility"]),
            ("Campaign", ["AdvanceTime"]),
            ("Resolution", ["CompleteResolution"])
        ],
        [SessionStage.Ended] = []
    };

    /// <summary>
    /// Builds a stage-scoped Kernel. Returns the kernel and the list of registered function names (for logging).
    /// </summary>
    public (Kernel Kernel, string[] RegisteredFunctions) Build(SessionContext sessionContext, SessionStage stage)
    {
        using var activity = ActivitySource.StartActivity("BuildKernel");
        activity?.SetTag("session.stage", stage.ToString());

        var settings = azureSettings.Value;
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddAzureOpenAIChatCompletion(settings.ChatModelDeployment, settings.Endpoint, settings.ApiKey);
        var kernel = kernelBuilder.Build();

        if (!StageFunctions.TryGetValue(stage, out var specs) || specs.Length == 0)
        {
            logger.LogInformation("Stage {Stage}: no plugins registered (narration only)", stage);
            return (kernel, []);
        }

        // Build all wrapper plugins (only instantiate what's needed)
        var wrappers = BuildWrapperPlugins(sessionContext);

        var registeredFunctions = new List<string>();
        foreach (var (pluginName, functionNames) in specs)
        {
            if (!wrappers.TryGetValue(pluginName, out var wrapperObj)) continue;

            // Import full plugin temporarily to get KernelFunctions, then re-import selectively
            var tempPlugin = kernel.ImportPluginFromObject(wrapperObj, pluginName);
            var selectedFunctions = tempPlugin
                .Where(f => functionNames.Contains(f.Name))
                .ToList();
            kernel.Plugins.Remove(tempPlugin);

            var scopedPlugin = KernelPluginFactory.CreateFromFunctions(pluginName, selectedFunctions);
            kernel.Plugins.Add(scopedPlugin);

            registeredFunctions.AddRange(selectedFunctions.Select(f => $"{pluginName}.{f.Name}"));
        }

        logger.LogInformation(
            "Stage {Stage}: registered {Count} functions — [{Functions}]",
            stage, registeredFunctions.Count, string.Join(", ", registeredFunctions));

        activity?.SetTag("kernel.functions", string.Join(",", registeredFunctions));

        return (kernel, registeredFunctions.ToArray());
    }

    private Dictionary<string, object> BuildWrapperPlugins(SessionContext sessionContext)
    {
        var campaignsRepo = serviceProvider.GetRequiredService<ICampaignsRepository>();
        var encountersRepo = serviceProvider.GetRequiredService<IEncountersRepository>();

        return new Dictionary<string, object>
        {
            ["Character"] = new CharacterWrapperPlugin(
                new CharacterPluginAdapter(serviceProvider.GetRequiredService<CharacterPlugin>()),
                sessionContext, campaignsRepo),
            ["Campaign"] = new CampaignWrapperPlugin(
                new CampaignPluginAdapter(serviceProvider.GetRequiredService<CampaignPlugin>()),
                campaignsRepo, sessionContext),
            ["Encounter"] = new EncounterWrapperPlugin(
                new EncounterPluginAdapter(serviceProvider.GetRequiredService<EncounterPlugin>()),
                sessionContext),
            ["Dice"] = new DiceWrapperPlugin(
                new DicePluginAdapter(serviceProvider.GetRequiredService<DicePlugin>())),
            ["Resolution"] = new ResolutionWrapperPlugin(sessionContext, encountersRepo)
        };
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~KernelFactoryTests" --nologo -v q`
Expected: PASS — every stage has exactly the right functions

- [ ] **Step 5: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/KernelFactory.cs WrtechedWhispers/WretchedWhispers.Tests/Services/KernelFactoryTests.cs
git commit -m "feat: add KernelFactory with stage-scoped function registration"
```

---

### Task 5: AgentExecutor

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/AgentExecutor.cs`

- [ ] **Step 1: Implement AgentExecutor**

Extracts agent creation (with ChatHistorySummarizationReducer, PromptComposer), streaming, and tool result extraction from `GameSessionService.cs` lines 86, 118-173, 417-477. Wraps in resilience pipeline.

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Services/AgentExecutor.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

namespace WretchedWhispers.Api.Services;

public sealed class AgentExecutor(
    IChatHistoryRepository chatHistoryRepository,
    PromptComposer promptComposer,
    ResiliencePipelineProvider<string> resilienceProvider,
    ILogger<AgentExecutor> logger)
{
    /// <summary>
    /// Creates a game master agent, streams its response token-by-token, and yields events.
    /// Caller collects NarrativeChunk text for chat history persistence.
    /// Resilience retry wraps the entire invocation — on retry, the stream restarts.
    /// </summary>
    public async IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        Kernel kernel,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = KernelFactory.ActivitySource.StartActivity("ExecuteAgent");

        var chatHistory = await chatHistoryRepository.LoadSession(chatSessionId, ct)
            ?? new ChatHistory();

        var agent = CreateAgent(kernel, sessionContext);

        // Resilience wraps the full invocation. On retry, we re-stream from scratch.
        // Events are collected during the pipeline execution, then yielded after success.
        // This preserves streaming granularity (per-token NarrativeChunks) while
        // supporting retry — on success, events replay to the caller immediately.
        var collectedEvents = new List<GameTurnEvent>();

        var pipeline = resilienceProvider.GetPipeline("llm-retry");
        await pipeline.ExecuteAsync(async token =>
        {
            collectedEvents.Clear();

            var thread = new ChatHistoryAgentThread(chatHistory);
            var userMessage = new ChatMessageContent(AuthorRole.User, playerMessage);

            await foreach (var response in agent.InvokeStreamingAsync(userMessage, thread, cancellationToken: token))
            {
                if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                    continue;

                var content = response.Message.Content;
                if (!string.IsNullOrEmpty(content))
                    collectedEvents.Add(new NarrativeChunk(content));
            }

            await foreach (var completed in thread.GetMessagesAsync(token))
            {
                foreach (var item in completed.Items)
                {
                    if (item is FunctionResultContent funcResult)
                    {
                        logger.LogInformation("Function invoked: {Function}", funcResult.FunctionName);
                        collectedEvents.Add(new ToolResult(funcResult.FunctionName ?? "unknown", funcResult.Result ?? ""));
                    }
                }
            }
        }, ct);

        // Yield all collected events (per-token narrative chunks + tool results)
        var functionCount = 0;
        var narrativeLength = 0;
        foreach (var evt in collectedEvents)
        {
            if (evt is NarrativeChunk nc) narrativeLength += nc.Text.Length;
            if (evt is ToolResult) functionCount++;
            yield return evt;
        }

        logger.LogInformation(
            "Agent completed: {FunctionCount} functions called, {NarrativeLength} chars",
            functionCount, narrativeLength);

        activity?.SetTag("agent.functions_called", functionCount);
        activity?.SetTag("agent.narrative_length", narrativeLength);
    }

    private ChatCompletionAgent CreateAgent(Kernel kernel, SessionContext sessionContext)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var summarizer = new ChatHistorySummarizationReducer(
            chatService, targetCount: 100, thresholdCount: 150)
        {
            SummarizationInstructions = """
                When summarizing this MORK BORG game session, preserve these critical elements:

                ESSENTIAL GAME STATE:
                - Character names, current hit points, abilities, scars, and omens
                - Current campaign location and time of day/season
                - Active encounters (adversaries, their status, ongoing combat)
                - Important NPCs the characters have met and their relationships
                - Key items, weapons, or artifacts in possession
                - Current goals, quests, or destinations
                - Recent significant events that affect the narrative

                PRESERVE THE ATMOSPHERE:
                - Maintain the doom-laden, apocalyptic tone of MORK BORG
                - Keep descriptions of the decaying world and mounting dread
                - Retain any omens, prophecies, or signs of the coming end
                - Preserve the dark humor and grim moments

                CONDENSE BUT KEEP:
                - Dialogue that reveals character or advances plot
                - Combat outcomes and their consequences (wounds, deaths, victories)
                - Environmental hazards or threats still present
                - Any clues, mysteries, or plot hooks still unresolved

                DISCARD:
                - Repetitive descriptions unless they build atmosphere
                - Resolved minor encounters with no lasting impact
                - Excessive back-and-forth without narrative progress
                - Redundant explanations of rules or mechanics

                Format the summary as a narrative that maintains the MORK BORG tone while clearly stating the current game state.
                """
        };

        return new ChatCompletionAgent
        {
            Name = "Game_Master",
            HistoryReducer = summarizer,
            Instructions = promptComposer.Compose(sessionContext),
            Kernel = kernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/AgentExecutor.cs
git commit -m "feat: extract AgentExecutor with chat history, summarizer, and resilience"
```

---

### Task 6: TurnCoordinator

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs`

- [ ] **Step 1: Implement TurnCoordinator**

The thin orchestrator that calls services in sequence. Owns the transaction. Returns `IAsyncEnumerable<GameTurnEvent>`.

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel.ChatCompletion;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.CombatAgent;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Services;

public sealed class TurnCoordinator(
    SessionContextLoader contextLoader,
    KernelFactory kernelFactory,
    AgentExecutor agentExecutor,
    CombatAgentService combatAgentService,
    IChatHistoryRepository chatHistoryRepository,
    WretchedWhispersDbContext dbContext,
    ILogger<TurnCoordinator> logger)
{
    public async IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = KernelFactory.ActivitySource.StartActivity("GameTurn");
        activity?.SetTag("session.id", sessionId.ToString());
        var sw = Stopwatch.StartNew();

        // Validate session
        var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
        var chatSessionId = chatSessions.FirstOrDefault();
        if (chatSessionId == Guid.Empty)
        {
            yield return new TurnError("No chat session found for this campaign");
            yield break;
        }

        // Load context and derive stage
        var context = await contextLoader.LoadAsync(sessionId, ct);
        if (context.Campaign is null)
        {
            yield return new TurnError("Session not found");
            yield break;
        }

        var stage = context.DeriveStage();
        logger.LogInformation("Turn started: Session={SessionId}, Stage={Stage}", sessionId, stage);
        activity?.SetTag("session.stage", stage.ToString());

        // Build stage-scoped kernel
        var (kernel, registeredFunctions) = kernelFactory.Build(context, stage);

        // Transaction scope
        await dbContext.Database.BeginTransactionAsync(ct);
        var functionsCalled = new List<string>();

        try
        {
            // Persist user message
            await chatHistoryRepository.SaveMessage(
                chatSessionId,
                new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User, playerMessage),
                ct);

            var fullResponseText = new System.Text.StringBuilder();

            if (stage == SessionStage.Combat)
            {
                logger.LogInformation("Combat delegated: EncounterId={EncounterId}",
                    context.ActiveEncounterId);

                // Combat sub-agent handles the turn
                await foreach (var evt in combatAgentService.ResolveCombatAsync(context, kernel, ct))
                {
                    if (evt is ToolResult tr) functionsCalled.Add(tr.Function);
                    if (evt is NarrativeChunk nc) fullResponseText.Append(nc.Text);
                    yield return evt;
                }
            }
            else
            {
                // Regular game master agent
                await foreach (var evt in agentExecutor.ExecuteAsync(kernel, context, chatSessionId, playerMessage, ct))
                {
                    if (evt is ToolResult tr) functionsCalled.Add(tr.Function);
                    if (evt is NarrativeChunk nc) fullResponseText.Append(nc.Text);
                    yield return evt;
                }
            }

            // Persist assistant response
            await chatHistoryRepository.SaveMessage(
                chatSessionId,
                new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant, fullResponseText.ToString())
                {
                    AuthorName = "Game_Master"
                },
                ct);

            // Commit transaction
            await dbContext.Database.CommitTransactionAsync(ct);

            // Reload context for state update (captures mutations from this turn)
            var postTurnContext = await contextLoader.LoadAsync(sessionId, ct);
            yield return StateUpdateMapper.Map(postTurnContext);

            // Done signal
            yield return new TurnDone();

            sw.Stop();
            logger.LogInformation(
                "Turn completed: Stage={Stage}, Duration={Duration}ms, Functions=[{Functions}], NarrativeLength={Length}",
                stage, sw.ElapsedMilliseconds, string.Join(", ", functionsCalled), fullResponseText.Length);

            activity?.SetTag("turn.duration_ms", sw.ElapsedMilliseconds);
            activity?.SetTag("turn.functions", string.Join(",", functionsCalled));
        }
        catch (OperationCanceledException)
        {
            try { await dbContext.Database.RollbackTransactionAsync(); } catch { }
            yield return new TurnError("Request was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turn failed: Stage={Stage}, Functions=[{Functions}]",
                stage, string.Join(", ", functionsCalled));

            try { await dbContext.Database.RollbackTransactionAsync(); } catch { }
            yield return new TurnError("An error occurred while processing your action");
        }
    }
}
```

- [ ] **Step 2: Rewrite CombatAgentService (same task — must land together to avoid broken commit)**

Replace `Channel<SseEvent>` with `IAsyncEnumerable<GameTurnEvent>`. Remove `StagePluginRegistry` dependency (kernel is already stage-scoped). Add `ILogger`.

```csharp
// WrtechedWhispers/WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Prompts;
using WretchedWhispers.Api.Services;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

namespace WretchedWhispers.Api.Plugins.CombatAgent;

public sealed class CombatAgentService(ILogger<CombatAgentService> logger)
{
    private const int MaxIterations = 30;

    public async IAsyncEnumerable<GameTurnEvent> ResolveCombatAsync(
        SessionContext sessionContext,
        Kernel kernel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var combatAgent = new ChatCompletionAgent
        {
            Name = "Combat_Resolver",
            Instructions = CombatPrompts.ComposeWithContext(sessionContext),
            Kernel = kernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };

        var thread = new ChatHistoryAgentThread();
        var combatMessage = new ChatMessageContent(AuthorRole.User,
            "Resolve this combat encounter. Attack with adversaries, let the player fight back, and end the encounter when all adversaries are dead or fled.");

        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            logger.LogDebug("Combat iteration {Iteration}: Adversaries={Adversaries}, HP={Hp}",
                iteration,
                sessionContext.ActiveEncounter?.LivingAdversaries.Count,
                sessionContext.Character?.Hp.Current);

            await foreach (var response in combatAgent.InvokeStreamingAsync(combatMessage, thread, cancellationToken: ct))
            {
                if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                    continue;

                var content = response.Message.Content;
                if (!string.IsNullOrEmpty(content))
                    yield return new NarrativeChunk(content);
            }

            await foreach (var completed in thread.GetMessagesAsync(ct))
            {
                foreach (var item in completed.Items)
                {
                    if (item is FunctionResultContent funcResult)
                        yield return new ToolResult(funcResult.FunctionName ?? "unknown", funcResult.Result ?? "");
                }
            }

            if (sessionContext.ActiveEncounter is { IsEnded: true }) break;
            if (sessionContext.Character is { IsDead: true }) break;

            combatMessage = new ChatMessageContent(AuthorRole.User,
                "Continue the combat. Attack with remaining adversaries and let the player respond.");
        }
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors (TurnCoordinator + CombatAgentService both reference each other's new APIs)

- [ ] **Step 4: Commit both together**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs WrtechedWhispers/WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs
git commit -m "feat: add TurnCoordinator and update CombatAgentService to yield GameTurnEvent"
```

---

### Task 7: Update DI registration and OpenTelemetry

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs`

- [ ] **Step 1: Update SemanticKernelConfiguration**

Replace `GameSessionService` registration with new services. Bind `AzureOpenAiSettings`. Add `CombatAgentService` to DI.

```csharp
// Replace the body of AddSemanticKernel method:
public static IServiceCollection AddSemanticKernel(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Singleton concurrency guard for per-session 409 Conflict
    services.AddSingleton<SessionConcurrencyGuard>();

    // Strongly-typed Azure OpenAI settings
    services.Configure<AzureOpenAiSettings>(configuration.GetSection("AzureOpenAi"));

    // Turn orchestration services (all scoped for request-scoped DbContext)
    services.AddScoped<TurnCoordinator>();
    services.AddScoped<SessionContextLoader>();
    services.AddScoped<KernelFactory>();
    services.AddScoped<AgentExecutor>();
    services.AddScoped<CombatAgentService>();
    services.AddScoped<PromptComposer>();

    // SK plugins (inner services for wrapper plugins)
    services.AddScoped<CharacterPlugin>();
    services.AddScoped<CampaignPlugin>();
    services.AddScoped<EncounterPlugin>();
    services.AddScoped<DicePlugin>();

    // Resilience pipeline for LLM retry
    var timeoutSeconds = configuration.GetValue("GameSession:ResponseTimeoutSeconds", 180);
    services.AddResiliencePipeline("llm-retry", pipelineBuilder =>
    {
        pipelineBuilder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = configuration.GetValue("GameSession:MaxRetryAttempts", 2),
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(timeoutSeconds));
    });

    return services;
}
```

Add the using for `AzureOpenAiSettings`:
```csharp
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.CombatAgent;
```

- [ ] **Step 2: Update OpenTelemetryConfiguration**

Add custom `ActivitySource`:

```csharp
// In the WithTracing section, add:
.AddSource("WretchedWhispers.GameTurn")
```

- [ ] **Step 3: Verify build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs WrtechedWhispers/WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs
git commit -m "feat: register new services, bind AzureOpenAiSettings, add OTel source"
```

---

### Task 8: Update SessionEndpoints to native SSE

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs`

- [ ] **Step 1: Replace the actions endpoint**

Replace the manual SSE handler (lines 36-103) with native `Results.ServerSentEvents`. Replace `GameSessionService` with `TurnCoordinator`. Use `StateUpdateMapper` in `GetSessionDetail`.

The actions endpoint becomes:

```csharp
group.MapPost("/{sessionId:guid}/actions", async (
    Guid sessionId,
    PlayerActionRequest request,
    TurnCoordinator turnCoordinator,
    SessionConcurrencyGuard guard,
    ICampaignsRepository campaignsRepo,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var userCampaigns = await campaignsRepo.GetForUser(userId);
    if (!userCampaigns.Any(c => c.Id == sessionId))
        return Results.NotFound();

    if (!await guard.TryAcquire(sessionId))
        return Results.Conflict(new { error = "GM response already in progress" });

    // Guard must release AFTER stream completes, not when Results.ServerSentEvents returns.
    // Wrap the async enumerable so guard releases in the enumerable's finally block.
    return Results.ServerSentEvents(
        MapToSseItems(
            WithGuardRelease(turnCoordinator.ExecuteTurnAsync(sessionId, request.Message, ct), guard, sessionId)),
        eventType: null);
});
```

Add two local helper methods in the class:

```csharp
private static async IAsyncEnumerable<SseItem<string>> MapToSseItems(
    IAsyncEnumerable<GameTurnEvent> events,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    await foreach (var evt in events.WithCancellation(ct))
    {
        var json = JsonSerializer.Serialize<object>(evt, jsonOptions);
        yield return new SseItem<string>(json, evt.EventType);
    }
}

/// <summary>
/// Wraps an async enumerable to release the concurrency guard after stream completion.
/// Results.ServerSentEvents returns immediately (before stream is consumed), so we
/// cannot release the guard in a try/finally around the Results call.
/// </summary>
private static async IAsyncEnumerable<GameTurnEvent> WithGuardRelease(
    IAsyncEnumerable<GameTurnEvent> events,
    SessionConcurrencyGuard guard,
    Guid sessionId,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    try
    {
        await foreach (var evt in events.WithCancellation(ct))
            yield return evt;
    }
    finally
    {
        guard.Release(sessionId);
    }
}
```

Add required usings:
```csharp
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WretchedWhispers.Api.Models;
```

- [ ] **Step 2: Update GetSessionDetail to use StateUpdateMapper**

Replace the 110-line mapping block in `GetSessionDetail` with:

```csharp
// Instead of manually mapping 24+ fields, build a SessionContext and use the mapper
var sessionContext = new SessionContext { SessionId = sessionId };
sessionContext.Campaign = campaign;
sessionContext.SetCampaignId(campaign.Id);
// ... load character into context same as before ...
var stateUpdate = StateUpdateMapper.Map(sessionContext);
```

Then use `stateUpdate` fields in the `SessionDetailDto` constructor.

- [ ] **Step 3: Verify build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors

- [ ] **Step 4: Run all tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: All existing tests pass (wrapper plugin tests, stage derivation tests unchanged)

- [ ] **Step 5: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs
git commit -m "feat: native .NET 10 SSE endpoint with TurnCoordinator"
```

---

### Task 9: Delete old files and clean up

**Files:**
- Delete: `WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs`
- Delete: `WrtechedWhispers/WretchedWhispers.Api/Services/StageTransitionFilter.cs`
- Delete: `WrtechedWhispers/WretchedWhispers.Api/Services/StagePluginRegistry.cs`
- Delete: `WrtechedWhispers/WretchedWhispers.Api/Models/SseEvent.cs`
- Delete: `WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs`
- Delete: `WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs`

- [ ] **Step 1: Delete old files**

```bash
rm WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs
rm WrtechedWhispers/WretchedWhispers.Api/Services/StageTransitionFilter.cs
rm WrtechedWhispers/WretchedWhispers.Api/Services/StagePluginRegistry.cs
rm WrtechedWhispers/WretchedWhispers.Api/Models/SseEvent.cs
rm WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs
rm WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs
```

- [ ] **Step 2: Fix any remaining references**

Search for `GameSessionService`, `StageTransitionFilter`, `StagePluginRegistry`, `SseEvent` across the codebase and remove/update any remaining references.

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors

- [ ] **Step 3: Run all tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: delete GameSessionService, StageTransitionFilter, StagePluginRegistry, SseEvent"
```

---

### Task 10: Update wrapper plugin tests for new DI shapes

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs`

- [ ] **Step 1: Verify existing wrapper tests still pass**

The wrapper plugins themselves didn't change, but their constructors were updated in earlier phases. Verify.

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~WrapperPluginTests" --nologo -v q`
Expected: PASS

- [ ] **Step 2: Fix any failures and commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs
git commit -m "test: update wrapper plugin tests for new service shapes"
```

---

### Task 11: TurnCoordinator unit tests

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs`

- [ ] **Step 1: Write tests**

Test the orchestration sequence: verify services are called in order, transaction is committed on success and rolled back on failure, chat messages are persisted.

The tests should mock all 5 services (SessionContextLoader, KernelFactory, AgentExecutor, CombatAgentService, IChatHistoryRepository) and the DbContext. Assert:
- `LoadAsync` called before `Build`
- `Build` receives the correct stage from `DeriveStage()`
- `ExecuteAsync` called with the kernel from `Build`
- User message saved before agent runs
- Assistant message saved after agent completes
- Transaction committed after persistence
- On exception: transaction rolled back, TurnError yielded
- StateUpdate yielded after commit
- TurnDone is the last event

- [ ] **Step 2: Run tests**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~TurnCoordinatorTests" --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs
git commit -m "test: add TurnCoordinator unit tests for orchestration sequence"
```

---

### Task 12: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: 0 errors, 0 warnings (except known NU1904 SK vulnerability warning)

- [ ] **Step 2: Full test suite**

Run: `dotnet test WrtechedWhispers/WrtechedWhispers.sln --nologo -v q`
Expected: All tests pass

- [ ] **Step 3: Verify no references to deleted classes remain**

```bash
grep -r "GameSessionService\|StageTransitionFilter\|StagePluginRegistry\|SseEvent" WrtechedWhispers/ --include="*.cs" -l
```
Expected: No matches (except possibly test files referencing old class names in comments)

- [ ] **Step 4: Commit any final fixes**

```bash
git add -A
git commit -m "chore: final cleanup after GameSessionService refactoring"
```
