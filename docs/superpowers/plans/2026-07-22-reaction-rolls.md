# Domain-Enforced Reaction Rolls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing Mörk Borg reaction table into real, model-visible, domain-enforced encounter state: `Unknown` encounters roll and store their reaction, friendly encounters cannot start combat, and a new `TurnHostile` transition handles legitimate escalation.

**Architecture:** All mechanics live in the `Encounter` aggregate (Core); `EncounterService` gains one pass-through method; the Engine surfaces the result via `EncounterDto` + `FormatSnapshot` and adds one tool; `StagePrompts` teaches the model when to use `Unknown`. Spec: `docs/superpowers/specs/2026-07-22-reaction-rolls-design.md`.

**Tech Stack:** C# / .NET 10, xUnit + Moq, System.Text.Json.

## Global Constraints

- NEVER use the null-forgiving operator (`!`) — use proper validation instead.
- Prefix all shell commands with `rtk` (e.g. `rtk dotnet test ...`), including inside `&&` chains.
- Build: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`. Test: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln` (full suite includes live LLM evals — only run the full suite where a step says so; otherwise use `--filter`).
- Aggregate JSON uses camelCase (`AggregateJsonOptions`): serialized property names are `reaction`, `reactionRoll`.
- The dice mock is 0-based per die: `SetupDiceRolls(a, b)` for 2d6 yields total `a + b + 2`.
- Domain entities are sealed with `[JsonConstructor]`; backward compat via optional constructor params defaulting `null`.
- Guard exception messages verbatim as given in each task.
- New public members carry no XML doc comments unless the surrounding file already documents siblings.

---

### Task 1: Domain — reaction storage, latent CurrentType fix, StartEncounter guard, TurnHostile

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Encounters/Encounter.cs`
- Test (create): `WrtechedWhispers/WretchedWhispers.Tests/Encounters/EncounterReactionTests.cs`

**Interfaces:**
- Consumes: existing `Encounter` (`Create`, `StartEncounter`, `AddAdversary`, `EndEncounter`), `InitialReaction`, `EncounterType`, `Dice`, `DiceExpr`.
- Produces (later tasks rely on these exact members): `public InitialReaction? Reaction { get; }`, `public int? ReactionRoll { get; }`, `public void TurnHostile()`, and the fixed semantics: `CurrentType` equals the declared type for non-Unknown creation; `StartEncounter()` throws `InvalidOperationException` while `CurrentType == EncounterType.Friendly`.

**Background for the implementer:** `Encounter.Initiate` currently early-returns for non-`Unknown` types, leaving `CurrentType` at `default(EncounterType)` = `Friendly` even for declared-Hostile encounters (latent bug — nothing consumed `CurrentType` until now). Existing tests that pass `EncounterType.Hostile` with a `SetupDiceRolls(...)// reaction roll` comment are vestigial: Hostile creation never rolls; those setups are harmless and the tests keep passing once the fix makes declared-Hostile encounters actually Hostile.

- [ ] **Step 1: Write the failing tests**

Create `WrtechedWhispers/WretchedWhispers.Tests/Encounters/EncounterReactionTests.cs`:

```csharp
using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Encounters;

public sealed class EncounterReactionTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    private static Adversary MinimalAdversary() => new(
        "Goblin", new HitPoints(5, 5), new Armor(ArmorTier.Light), 7,
        new AttackProfile("Claw", DiceExpr.D4));

    // Mock is 0-based per die: SetupDiceRolls(a, b) -> 2d6 total a + b + 2.
    [Theory]
    [InlineData(0, 0, 2, InitialReaction.Kill, EncounterType.Hostile)]
    [InlineData(1, 1, 4, InitialReaction.Angered, EncounterType.Hostile)]
    [InlineData(3, 2, 7, InitialReaction.Indifferent, EncounterType.Friendly)]
    [InlineData(4, 3, 9, InitialReaction.AlmostFriendly, EncounterType.Friendly)]
    [InlineData(5, 5, 12, InitialReaction.Helpful, EncounterType.Friendly)]
    public void UnknownCreation_RollsAndStoresReaction(
        int die1, int die2, int expectedRoll, InitialReaction expectedReaction, EncounterType expectedType)
    {
        SetupDiceRolls(die1, die2);

        var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);

        Assert.Equal(expectedRoll, encounter.ReactionRoll);
        Assert.Equal(expectedReaction, encounter.Reaction);
        Assert.Equal(expectedType, encounter.CurrentType);
    }

    [Fact]
    public void DeclaredHostile_SetsCurrentTypeHostile_NoReaction()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.Null(encounter.Reaction);
        Assert.Null(encounter.ReactionRoll);
    }

    [Fact]
    public void DeclaredFriendly_SetsCurrentTypeFriendly_NoReaction()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);

        Assert.Equal(EncounterType.Friendly, encounter.CurrentType);
        Assert.Null(encounter.Reaction);
        Assert.Null(encounter.ReactionRoll);
    }

    [Fact]
    public void StartEncounter_WhileFriendly_Throws()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);
        encounter.AddAdversary(MinimalAdversary());

        Assert.Throws<InvalidOperationException>(() => encounter.StartEncounter());
    }

    [Fact]
    public void TurnHostile_ThenStartEncounter_Succeeds()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);
        encounter.AddAdversary(MinimalAdversary());

        encounter.TurnHostile();
        encounter.StartEncounter();

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.True(encounter.IsStarted);
    }

    [Fact]
    public void TurnHostile_WhenAlreadyHostileOrStarted_IsIdempotent()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        encounter.AddAdversary(MinimalAdversary());
        encounter.StartEncounter();

        encounter.TurnHostile();

        Assert.Equal(EncounterType.Hostile, encounter.CurrentType);
        Assert.True(encounter.IsStarted);
    }

    [Fact]
    public void TurnHostile_OnEndedEncounter_Throws()
    {
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        var adversary = MinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        adversary.ReceiveDamage(5);
        encounter.EndEncounter();

        Assert.Throws<InvalidOperationException>(() => encounter.TurnHostile());
    }

    [Fact]
    public void Reaction_RoundTripsThroughJson()
    {
        SetupDiceRolls(3, 2); // 2d6 = 7 -> Indifferent -> Friendly
        var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);

        var json = JsonSerializer.Serialize(encounter, _options);
        var restored = JsonSerializer.Deserialize<Encounter>(json, _options);

        Assert.NotNull(restored);
        Assert.Equal(InitialReaction.Indifferent, restored.Reaction);
        Assert.Equal(7, restored.ReactionRoll);
        Assert.Equal(EncounterType.Friendly, restored.CurrentType);
    }

    [Fact]
    public void Encounter_DeserializesFromBlobWithoutReactionFields()
    {
        // Backward compat: blobs persisted before reaction storage must still load.
        var encounter = Encounter.Create("Ambush", "Bandits leap out", EncounterType.Hostile, Dice);
        var json = JsonSerializer.Serialize(encounter, _options);
        using var doc = JsonDocument.Parse(json);
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (prop.Name != "reaction" && prop.Name != "reactionRoll")
                stripped[prop.Name] = prop.Value;
        var legacyJson = JsonSerializer.Serialize(stripped);

        var restored = JsonSerializer.Deserialize<Encounter>(legacyJson, _options);

        Assert.NotNull(restored);
        Assert.Null(restored.Reaction);
        Assert.Null(restored.ReactionRoll);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~EncounterReactionTests"`
Expected: compile errors (`Reaction`, `ReactionRoll`, `TurnHostile` do not exist). That counts as the failing state.

- [ ] **Step 3: Implement in `Encounter.cs`**

In `WrtechedWhispers/WretchedWhispers.Core/Encounters/Encounter.cs`:

3a. Extend the `[JsonConstructor]` (append two optional params) and assign them:

```csharp
[JsonConstructor]
private Encounter(Guid id, EncounterType initialType, EncounterType currentType, string name, string description,
    List<Adversary> adversaries, bool isStarted = false, bool isEnded = false, bool isResolved = false,
    InitialReaction? reaction = null, int? reactionRoll = null)
{
    Id = id;
    InitialType = initialType;
    CurrentType = currentType;
    Name = name;
    Description = description;
    Adversaries = adversaries ?? [];
    IsStarted = isStarted;
    IsEnded = isEnded;
    IsResolved = isResolved;
    Reaction = reaction;
    ReactionRoll = reactionRoll;
}
```

3b. Add the two properties next to `CurrentType`:

```csharp
[JsonInclude] public InitialReaction? Reaction { get; private set; }
[JsonInclude] public int? ReactionRoll { get; private set; }
```

3c. Replace `Initiate` and `RollInitialReaction` with:

```csharp
private void Initiate(EncounterType initialType, Dice dice)
{
    if (initialType is not EncounterType.Unknown)
    {
        CurrentType = initialType;
        return;
    }

    var rollResult = dice.Roll(DiceExpr.D(2, 6));
    ReactionRoll = rollResult;
    Reaction = MapReaction(rollResult);
    if (Reaction is InitialReaction.Kill or InitialReaction.Angered)
        ElevateToHostile();
    else
        ElevateToFriendly();
}

private static InitialReaction MapReaction(int rollResult) => rollResult switch
{
    2 or 3 => InitialReaction.Kill,
    >= 4 and <= 6 => InitialReaction.Angered,
    7 or 8 => InitialReaction.Indifferent,
    9 or 10 => InitialReaction.AlmostFriendly,
    _ => InitialReaction.Helpful
};
```

3d. Guard `StartEncounter` (friendly check first, adversary check unchanged):

```csharp
public void StartEncounter()
{
    if (CurrentType == EncounterType.Friendly)
        throw new InvalidOperationException(
            "The encounter is friendly — call TurnHostile first; the fiction must escalate before combat can start.");
    if (Adversaries.Count == 0)
        throw new InvalidOperationException("Can't start an encounter without adversaries.");
    IsStarted = true;
    IsEnded = false;
}
```

3e. Add `TurnHostile` next to `EndEncounter`:

```csharp
/// <summary>Escalates a friendly meeting to hostile (player aggression, collapsed talks).
/// Idempotent when already hostile; only a finished encounter refuses.</summary>
public void TurnHostile()
{
    if (IsEnded) throw new InvalidOperationException("Can't turn a finished encounter hostile.");
    ElevateToHostile();
}
```

- [ ] **Step 4: Run the new tests and the neighbors that exercise Encounter**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~EncounterReactionTests|FullyQualifiedName~ResolveRoundTests|FullyQualifiedName~EncounterRoundTripTests|FullyQualifiedName~StageDerivationTests|FullyQualifiedName~JsonSerializationTests|FullyQualifiedName~PromptComposerTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Encounters/Encounter.cs WrtechedWhispers/WretchedWhispers.Tests/Encounters/EncounterReactionTests.cs
rtk git commit -m "feat(encounters): store reaction rolls, guard friendly starts, add TurnHostile"
```

---

### Task 2: Service pass-through, TurnEncounterHostile tool, DTO surfacing

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Encounters/EncounterService.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/GameTools/EncounterTools.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/GameTools/Models/EncounterDto.cs`
- Test (modify): `WrtechedWhispers/WretchedWhispers.Tests/Plugins/GameToolsTests.cs`
- Test (modify): `WrtechedWhispers/WretchedWhispers.Tests/Services/GameToolCatalogTests.cs`
- Test (modify): `WrtechedWhispers/WretchedWhispers.Tests/Services/AgentToolProviderTests.cs`

**Interfaces:**
- Consumes (from Task 1): `Encounter.TurnHostile()`, `Encounter.Reaction` (`InitialReaction?`), `Encounter.ReactionRoll` (`int?`), `Encounter.CurrentType`.
- Produces: `EncounterService.TurnHostile(Guid encounterId)` returning `Task<Encounter>`; tool method `EncounterTools.TurnEncounterHostile()` returning `Task<EncounterDto>`; DTO properties `Disposition` (string), `Reaction` (string?), `ReactionRoll` (int?). Tool registry name: `Encounter.TurnEncounterHostile` (Exploration stage → Exploration now exposes 15 tools).

- [ ] **Step 1: Write the failing tests**

1a. In `GameToolsTests.cs`, add to the EncounterTools section (near the existing `CreateEncounter` test at ~line 209):

```csharp
[Fact]
public async Task CreateEncounter_Unknown_ReturnsRolledReactionInDto()
{
    // 0-based mock: 3,2 -> 2d6 = 7 -> Indifferent -> Friendly.
    var mock = new Mock<IRandomService>();
    var queue = new Queue<int>([3, 2]);
    mock.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(() => queue.Dequeue());
    var dice = new Dice(mock.Object);

    var result = await EncounterTools(dice).CreateEncounter("Strangers", "Figures in the fog", "Unknown");

    Assert.Equal("Friendly", result.Disposition);
    Assert.Equal("Indifferent", result.Reaction);
    Assert.Equal(7, result.ReactionRoll);
}

[Fact]
public async Task CreateEncounter_Hostile_ReportsHostileDispositionWithoutReaction()
{
    var result = await EncounterTools().CreateEncounter("Ambush", "Bandits leap out", "Hostile");

    Assert.Equal("Hostile", result.Disposition);
    Assert.Null(result.Reaction);
    Assert.Null(result.ReactionRoll);
}

[Fact]
public async Task TurnEncounterHostile_FlipsDisposition()
{
    var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, _zeroDice);
    _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
    _context.SetActiveEncounterId(encounter.Id);

    var result = await EncounterTools().TurnEncounterHostile();

    Assert.Equal("Hostile", result.Disposition);
    _encountersRepo.Verify(r => r.Save(It.Is<Encounter>(e => e.CurrentType == EncounterType.Hostile)), Times.Once);
}

[Fact]
public async Task TurnEncounterHostile_WithoutActiveEncounter_Throws()
{
    await Assert.ThrowsAsync<InvalidOperationException>(() => EncounterTools().TurnEncounterHostile());
}
```

Notes for the implementer: `_zeroDice`, `_context`, `_encountersRepo`, and the `EncounterTools(Dice?)` factory already exist in the fixture. Check the file's existing `using` directives; add `WretchedWhispers.Core.Encounters` / `WretchedWhispers.Core.Dices` only if missing. If the repository's `Get`/`Save` signatures carry a `CancellationToken`, match the mock setups to the file's existing `_encountersRepo` idiom.

1b. In `GameToolCatalogTests.cs`, rename `Exploration_ExposesExactlyFourteenTools` → `Exploration_ExposesExactlyFifteenTools` and add `"Encounter.TurnEncounterHostile"` after `"Encounter.StartEncounter"` in the expected array (the list is alphabetical).

1c. In `AgentToolProviderTests.cs`, rename `Exploration_HasExactly14Functions` → `Exploration_HasExactly15Functions`, change both `Assert.Equal(14, ...)` to `15`, and add `Assert.Contains("Encounter.TurnEncounterHostile", registered);`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GameToolsTests|FullyQualifiedName~GameToolCatalogTests|FullyQualifiedName~AgentToolProviderTests"`
Expected: compile errors for the new DTO members/tool; count tests fail 14≠15.

- [ ] **Step 3: Implement**

3a. `EncounterService.cs` — add after `EndEncounter`:

```csharp
public async Task<Encounter> TurnHostile(Guid encounterId)
{
    var encounter = await encountersRepository.Get(encounterId) ??
                    throw new InvalidOperationException("Encounter not found");
    encounter.TurnHostile();
    await encountersRepository.Save(encounter);

    return encounter;
}
```

3b. `EncounterDto.cs` — add after `Description`:

```csharp
[JsonPropertyName("Disposition")]
[Description("Current disposition: Friendly (combat cannot start) or Hostile")]
public string Disposition { get; set; } = string.Empty;

[JsonPropertyName("Reaction")]
[Description("Rolled Mörk Borg reaction when the encounter was created as Unknown: Kill, Angered, Indifferent, AlmostFriendly, or Helpful. Null when the type was pre-declared.")]
public string? Reaction { get; set; }

[JsonPropertyName("ReactionRoll")]
[Description("The raw 2d6 reaction roll behind Reaction. Null when the type was pre-declared.")]
public int? ReactionRoll { get; set; }
```

3c. `EncounterTools.cs` — extend `CreateEncounterDto` mapping (in the object initializer, after `Description`):

```csharp
Disposition = encounter.CurrentType.ToString(),
Reaction = encounter.Reaction?.ToString(),
ReactionRoll = encounter.ReactionRoll,
```

3d. `EncounterTools.cs` — replace the `initialEncounterType` parameter description on `CreateEncounter`:

```csharp
[Description("Initial type. 'Unknown' = the domain rolls the Mörk Borg reaction table and returns the result — the DEFAULT for any first meeting whose attitude the fiction leaves open. Pre-declare 'Hostile' or 'Friendly' ONLY when the fiction predetermines the attitude (an ambush, a sworn enemy, a hired guide).")] string initialEncounterType)
```

3e. `EncounterTools.cs` — add the tool after `StartEncounter`:

```csharp
[Description("Escalate the current encounter to Hostile. Use ONLY when the fiction legitimately escalates — the player attacks first, negotiation collapses, treachery is revealed. Never use it to override a rolled reaction without in-fiction cause. Required before StartEncounter when the encounter is Friendly.")]
[GameTool(SessionStage.Exploration)]
public async Task<EncounterDto> TurnEncounterHostile()
{
    var encounter = await encounterService.TurnHostile(RequireEncounterId());
    return CreateEncounterDto(encounter);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GameToolsTests|FullyQualifiedName~GameToolCatalogTests|FullyQualifiedName~AgentToolProviderTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Encounters/EncounterService.cs WrtechedWhispers/WretchedWhispers.Engine/GameTools/EncounterTools.cs WrtechedWhispers/WretchedWhispers.Engine/GameTools/Models/EncounterDto.cs WrtechedWhispers/WretchedWhispers.Tests/Plugins/GameToolsTests.cs WrtechedWhispers/WretchedWhispers.Tests/Services/GameToolCatalogTests.cs WrtechedWhispers/WretchedWhispers.Tests/Services/AgentToolProviderTests.cs
rtk git commit -m "feat(encounters): surface reaction/disposition to the model, add TurnEncounterHostile tool"
```

---

### Task 3: Snapshot disposition line, Exploration prompt, full verification

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs` (the `ActiveEncounter` block in `FormatSnapshot`, ~line 140)
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Prompts/StagePrompts.cs` (the `Exploration` const)
- Test (modify): `WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs`

**Interfaces:**
- Consumes (from Task 1): `Encounter.Reaction`, `Encounter.ReactionRoll`, `Encounter.CurrentType`; (from Task 2) tool name `TurnEncounterHostile`.
- Produces: snapshot lines `  Disposition: Hostile (reaction roll 4 — Angered)` (rolled) / `  Disposition: Friendly` (pre-declared); no code interface for prompts.

- [ ] **Step 1: Write the failing snapshot tests**

In `StageDerivationTests.cs` (which already holds `FormatSnapshot` tests — follow the file's existing arrangement idiom, e.g. `FormatSnapshot_ListsFallenWretches`, reusing its helpers like `CreateMinimalAdversary`):

```csharp
[Fact]
public void FormatSnapshot_ShowsRolledReactionDisposition()
{
    SetupDiceRolls(1, 1); // 2d6 = 4 -> Angered -> Hostile
    var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);
    encounter.AddAdversary(CreateMinimalAdversary());
    encounter.StartEncounter();

    var ctx = new SessionContext { SessionId = Guid.NewGuid() };
    ctx.ActiveEncounter = encounter;

    var snapshot = ctx.FormatSnapshot();

    Assert.Contains("Disposition: Hostile (reaction roll 4 — Angered)", snapshot);
}

[Fact]
public void FormatSnapshot_ShowsPreDeclaredDispositionWithoutReaction()
{
    var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);

    var ctx = new SessionContext { SessionId = Guid.NewGuid() };
    ctx.ActiveEncounter = encounter;

    var snapshot = ctx.FormatSnapshot();

    Assert.Contains("Disposition: Friendly", snapshot);
    Assert.DoesNotContain("reaction roll", snapshot);
}
```

(If `FormatSnapshot` takes arguments or the snapshot tests live behind a helper in this file, follow the existing call idiom exactly — the assertion strings are the requirement.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StageDerivationTests"`
Expected: the two new tests FAIL (no Disposition line yet); the rest PASS.

- [ ] **Step 3: Implement the snapshot line**

In `SessionContext.cs`, inside the `if (ActiveEncounter is not null)` block, after the `Active Encounter:` line:

```csharp
sb.AppendLine($"  Disposition: {ActiveEncounter.CurrentType}"
    + (ActiveEncounter.Reaction is null
        ? ""
        : $" (reaction roll {ActiveEncounter.ReactionRoll} — {ActiveEncounter.Reaction})"));
```

- [ ] **Step 4: Update the Exploration prompt**

In `StagePrompts.cs`, replace this single bullet of the `Exploration` const:

```
- When violence or combat begins, IMMEDIATELY call CreateEncounter to set up the fight, AddAdversaryToEncounter to add enemies, then StartEncounter to begin combat. Do NOT narrate combat without creating an encounter first.
```

with these three bullets:

```
- FIRST MEETING with any creature whose attitude the fiction leaves open: call CreateEncounter with type
  'Unknown' — the domain rolls the Mörk Borg reaction table and returns the reaction and roll. Narrate that
  rolled reaction honestly; NEVER decide hostility yourself when the attitude is uncertain. Pre-declare
  'Hostile' or 'Friendly' only when the fiction predetermines it (an ambush, a sworn enemy, a hired guide).
- When violence or combat begins, IMMEDIATELY call CreateEncounter (if none exists), AddAdversaryToEncounter to
  add enemies, then StartEncounter to begin combat. Do NOT narrate combat without a started encounter.
- StartEncounter only works when the encounter is Hostile. If a friendly or uncertain meeting collapses into
  violence — the player attacks first, talks fail, treachery — call TurnEncounterHostile first, then StartEncounter.
```

- [ ] **Step 5: Run the snapshot tests, then the full suite**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StageDerivationTests"`
Expected: PASS.

Then the full suite (includes live LLM evals — the Exploration prompt changed, watch `DomainAuthorityEvals`):

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: PASS. Known flake: individual live evals occasionally fail on LLM judgment (fast 3s failures); re-run the specific eval once before treating it as a regression.

- [ ] **Step 6: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs WrtechedWhispers/WretchedWhispers.Engine/Prompts/StagePrompts.cs WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs
rtk git commit -m "feat(encounters): snapshot disposition line and reaction-first Exploration prompt"
```
