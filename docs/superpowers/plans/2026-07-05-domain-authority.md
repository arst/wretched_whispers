# Domain Authority & GM Flexibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Push "model chooses intent, domain owns truth" into the last prompt-law areas (combat rounds, campaign lifecycle), open domain-validated ruling channels (challenge consequences, journal), and fix the re-summarization cost defect.

**Architecture:** Five phases, each a branch + PR. Domain logic goes in `WretchedWhispers.Core` (sealed entities, JsonConstructor, services with primary constructors). Model-facing tools in `WretchedWhispers.Api/GameTools` stay thin: auto-fill IDs from `SessionContext`, validate via `ToolGuard`/readable exceptions, call domain, map to DTO. Campaign aggregate persists as a JSON blob (`CampaignEntity.Data`), so new aggregate members need **no migration**; only the chat-session summary columns (Phase 1) do.

**Tech Stack:** .NET 10 (`net10.0`), EF Core + SQLite, Microsoft Agent Framework 1.9 / Microsoft.Extensions.AI, xunit + Moq, Microsoft.Extensions.AI.Evaluation (evals).

**Spec:** `docs/superpowers/specs/2026-07-05-domain-authority-design.md`

## Global Constraints

- Never use the null-forgiving operator (`!`) — validate instead (repo convention).
- Domain entities: `sealed`, `[JsonConstructor]` private constructors, `[JsonInclude]` on persisted members (see `Campaign.cs` for the idiom). Services: primary constructors.
- Tool guard/exception messages are written **for the model to read** (it retries on them).
- Prefix all shell commands with `rtk` (e.g. `rtk dotnet test`, `rtk git commit`).
- Build: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`. Test: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln` (evals auto-skip without Azure credentials).
- One branch per phase: `feat/rolling-summary`, `feat/challenge-consequences`, `feat/combat-round`, `feat/campaign-journal`, `feat/domain-authority-evals`. Each phase branches from the previous phase's tip (stacked) unless the previous phase already merged.
- **Precondition:** the working tree has in-flight uncommitted changes this plan builds on (e.g. `GameTools/ToolGuard.cs`). They must be committed on `feat/campaign-creation-eval` before Phase 1 branches off.

---

## Phase 1 — Rolling summary + iteration cap (branch `feat/rolling-summary`)

### Task 1: Summary persistence (entity + repository)

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/ChatSessionEntity.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/IChatHistoryRepository.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteChatHistoryRepository.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Persistence/ChatSummaryPersistenceTests.cs` (create; follow the pattern of existing tests in `Tests/Persistence/`)

**Interfaces:**
- Produces: `record ChatSummary(string Text, int CoveredCount)` in namespace `WretchedWhispers.Infrastructure.Persistence`; `Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default)` and `Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default)` on `IChatHistoryRepository`. Task 2 consumes these.

- [ ] **Step 1: Write the failing test**

Look at an existing test in `Tests/Persistence/` first to copy its SQLite in-memory setup idiom. Then:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public sealed class ChatSummaryPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WretchedWhispersDbContext _db;
    private readonly SqliteChatHistoryRepository _repo;

    public ChatSummaryPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection).Options;
        _db = new WretchedWhispersDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new SqliteChatHistoryRepository(_db);
    }

    [Fact]
    public async Task GetSummary_NoSummarySaved_ReturnsNull()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        Assert.Null(await _repo.GetSummary(sessionId));
    }

    [Fact]
    public async Task SaveSummary_ThenGet_RoundTrips()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        await _repo.SaveSummary(sessionId, new ChatSummary("the tale so far", 42));

        var summary = await _repo.GetSummary(sessionId);

        Assert.NotNull(summary);
        Assert.Equal("the tale so far", summary.Text);
        Assert.Equal(42, summary.CoveredCount);
    }

    [Fact]
    public async Task SaveSummary_UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.SaveSummary(Guid.NewGuid(), new ChatSummary("x", 1)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
```

Note: `WretchedWhispersDbContext` may require an extra constructor arg or Identity setup — mirror whatever the existing persistence tests do to construct it; if none exist, adapt until `EnsureCreated` succeeds.

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatSummaryPersistenceTests"`
Expected: compile FAIL — `ChatSummary` / `GetSummary` not defined.

- [ ] **Step 3: Implement**

`ChatSessionEntity.cs` — add two properties:

```csharp
public string? SummaryText { get; set; }
public int SummaryCoveredCount { get; set; }
```

`IChatHistoryRepository.cs` — add the record and two methods:

```csharp
/// <summary>Rolling summary of a chat session: the text and how many leading messages it covers.</summary>
public sealed record ChatSummary(string Text, int CoveredCount);

public interface IChatHistoryRepository
{
    // ...existing members...
    Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default);
    Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default);
}
```

`SqliteChatHistoryRepository.cs` — implement:

```csharp
public async Task<ChatSummary?> GetSummary(Guid sessionId, CancellationToken ct = default)
{
    var session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    return session?.SummaryText is null
        ? null
        : new ChatSummary(session.SummaryText, session.SummaryCoveredCount);
}

public async Task SaveSummary(Guid sessionId, ChatSummary summary, CancellationToken ct = default)
{
    var session = await _db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
        ?? throw new InvalidOperationException($"Chat session {sessionId} not found");
    session.SummaryText = summary.Text;
    session.SummaryCoveredCount = summary.CoveredCount;
    await _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Add the EF migration**

Run:
```bash
rtk dotnet ef migrations add AddChatSessionSummary --project WrtechedWhispers/WretchedWhispers.Infrastructure --startup-project WrtechedWhispers/WretchedWhispers.Api
```
Expected: a new migration under `Infrastructure/Migrations/` adding nullable `SummaryText` (TEXT) and `SummaryCoveredCount` (INTEGER, default 0) to `ChatSessions`. If `dotnet ef` is missing: `rtk dotnet tool restore` (or `rtk dotnet tool install -g dotnet-ef`).

- [ ] **Step 5: Run test to verify it passes**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatSummaryPersistenceTests"`
Expected: 3 PASS.

- [ ] **Step 6: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(history): persist rolling chat summary with watermark"
```

### Task 2: Rolling reduction in ChatHistoryReducer

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Services/ChatHistoryReducer.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Services/AgentExecutor.cs:56` (call site)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Services/ChatHistoryReducerTests.cs` (create)

**Interfaces:**
- Consumes: `IChatHistoryRepository.GetSummary/SaveSummary`, `ChatSummary` (Task 1).
- Produces: `Task<IReadOnlyList<ChatMessage>> ReduceAsync(Guid chatSessionId, IReadOnlyList<ChatMessage> history, CancellationToken ct)` — signature gains `chatSessionId`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Tests.Services;

public sealed class ChatHistoryReducerTests
{
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<IChatHistoryRepository> _repo = new();
    private readonly Guid _sessionId = Guid.NewGuid();

    private ChatHistoryReducer CreateReducer() =>
        new(_chatClient.Object, _repo.Object, NullLogger<ChatHistoryReducer>.Instance);

    private static IReadOnlyList<ChatMessage> Messages(int count) =>
        Enumerable.Range(0, count).Select(i => new ChatMessage(ChatRole.User, $"msg {i}")).ToList();

    private void SetupSummarizerResponse(string text) =>
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    [Fact]
    public async Task UnderThreshold_NoStoredSummary_ReturnsHistoryUnchanged_NoModelCall()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);

        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(10), CancellationToken.None);

        Assert.Equal(10, result.Count);
        _chatClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnderThreshold_WithStoredSummary_PrependsSummary_SkipsCoveredMessages()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("earlier doom", 50));

        // 120 total, 50 covered -> tail of 70, under threshold: summary + 70 messages, no model call.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(120), CancellationToken.None);

        Assert.Equal(71, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Contains("earlier doom", result[0].Text);
        Assert.Equal("msg 50", result[1].Text);
        _chatClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OverThreshold_SummarizesTail_AdvancesWatermark()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        SetupSummarizerResponse("fresh summary");

        // 200 messages, none covered -> summarize oldest 100, keep 100.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(200), CancellationToken.None);

        Assert.Equal(101, result.Count);
        Assert.Contains("fresh summary", result[0].Text);
        _repo.Verify(r => r.SaveSummary(
            _sessionId,
            It.Is<ChatSummary>(s => s.Text == "fresh summary" && s.CoveredCount == 100),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task OverThreshold_EmptySummarizerResponse_DoesNotAdvanceWatermark()
    {
        _repo.Setup(r => r.GetSummary(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("kept", 20));
        SetupSummarizerResponse("   ");

        // 200 total, 20 covered -> tail 180 over threshold, but summarization fails.
        var result = await CreateReducer().ReduceAsync(_sessionId, Messages(200), CancellationToken.None);

        _repo.Verify(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains("kept", result[0].Text);   // stored summary still leads
        Assert.Equal(101, result.Count);           // stored summary + recent 100
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatHistoryReducerTests"`
Expected: compile FAIL — `ReduceAsync` has no `chatSessionId` parameter, constructor has no repository.

- [ ] **Step 3: Implement**

Replace `ChatHistoryReducer`'s constructor and `ReduceAsync` (keep `TargetCount`, `ThresholdCount`, `SummarizationInstructions`, and the class doc comment — update the doc comment's last sentence to mention the persisted watermark):

```csharp
public sealed class ChatHistoryReducer(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
    ILogger<ChatHistoryReducer> logger)
{
    // ...existing consts...

    public async Task<IReadOnlyList<ChatMessage>> ReduceAsync(
        Guid chatSessionId, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var stored = await chatHistoryRepository.GetSummary(chatSessionId, ct);
        var covered = stored?.CoveredCount ?? 0;
        var tail = history.Skip(covered).ToList();

        if (tail.Count <= ThresholdCount)
            return Compose(stored, tail);

        var olderCount = tail.Count - TargetCount;
        var toSummarize = new List<ChatMessage>(olderCount + 1);
        if (stored is not null)
            toSummarize.Add(SummaryMessage(stored.Text));
        toSummarize.AddRange(tail.Take(olderCount));

        var options = new ChatOptions { Instructions = SummarizationInstructions };
        var response = await chatClient.GetResponseAsync(toSummarize, options, ct);
        var summaryText = response.Text;
        var recent = tail.Skip(olderCount).ToList();

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            // Summarization produced nothing usable — keep the stored summary and the recent tail;
            // the watermark stays put so the next turn retries.
            logger.LogWarning("History summarization returned empty; keeping recent {Count} messages", recent.Count);
            return Compose(stored, recent);
        }

        var updated = new ChatSummary(summaryText, covered + olderCount);
        await chatHistoryRepository.SaveSummary(chatSessionId, updated, ct);

        logger.LogInformation(
            "Rolled summary forward — covered {Covered} of {Total} messages, sending {Sent}",
            updated.CoveredCount, history.Count, recent.Count + 1);

        return Compose(updated, recent);
    }

    private static ChatMessage SummaryMessage(string text) =>
        new(ChatRole.System, $"[Summary of the session so far]\n{text}");

    private static IReadOnlyList<ChatMessage> Compose(ChatSummary? summary, List<ChatMessage> tail)
    {
        if (summary is null)
            return tail;
        var result = new List<ChatMessage>(tail.Count + 1) { SummaryMessage(summary.Text) };
        result.AddRange(tail);
        return result;
    }
}
```

Add `using WretchedWhispers.Infrastructure.Persistence;`.

`AgentExecutor.cs:56` — update the call site:

```csharp
history = await historyReducer.ReduceAsync(chatSessionId, history, ct);
```

- [ ] **Step 4: Run tests + full build**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatHistoryReducerTests"` → 4 PASS.
Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln` → 0 errors (catches any other `ReduceAsync` callers; fix them the same way — the eval harness may call it).

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(history): rolling summarization with persisted watermark"
```

### Task 3: Function-loop iteration cap

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Services/AgentExecutor.cs:36-43`

- [ ] **Step 1: Implement**

In the `UseFunctionInvocation(configure: c => ...)` block add:

```csharp
// Hard ceiling on tool-call iterations per turn — bounds a runaway (but non-erroring) loop.
c.MaximumIterationsPerRequest = 15;
```

`MaximumIterationsPerRequest` is a property on `FunctionInvokingChatClient` (which `c` is). If that exact name doesn't compile against the installed Microsoft.Extensions.AI version, list the available members (`rtk grep -rn "MaximumIterations" ~/.nuget/packages/microsoft.extensions.ai/`) and use the equivalent iterations-cap property — do not silently skip the cap.

- [ ] **Step 2: Build + run all tests**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: build clean, all tests pass.

- [ ] **Step 3: Commit, push, open PR**

```bash
rtk git add -A && rtk git commit -m "feat(agent): cap function-invocation iterations per turn"
rtk git push -u origin feat/rolling-summary
rtk gh pr create --title "Rolling chat summary + function-loop iteration cap" --body "Phase 1 of docs/superpowers/specs/2026-07-05-domain-authority-design.md"
```

---

## Phase 2 — Challenge consequences + dice breakdowns (branch `feat/challenge-consequences`)

### Task 4: ChallengeOutcome breakdown fields

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Challenge/ChallengeOutcome.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs:336-371` (`Challenge` method)
- Test: extend the existing challenge tests under `WrtechedWhispers/WretchedWhispers.Tests/Characters/` (find with `rtk grep -rln "Challenge" WrtechedWhispers/WretchedWhispers.Tests/Characters`)

**Interfaces:**
- Produces: `ChallengeOutcome` gains `int Roll`, `int Modifier`, `int EffectiveDr`; factories become `Success(Natural natural, int roll, int modifier, int effectiveDr)` / `Fail(...)` (same order). Tasks 5–8 consume these.

- [ ] **Step 1: Write the failing test**

Add to the existing character challenge test class (create `Characters/Challenge/ChallengeOutcomeBreakdownTests.cs` if none fits):

```csharp
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests.Characters.Challenge;

public sealed class ChallengeOutcomeBreakdownTests : TestBase
{
    [Fact]
    public void Challenge_ReportsRollModifierAndEffectiveDr()
    {
        // Build a character the same way other Character tests do (CharacterCreationService or factory).
        var character = TestCharacters.Create(Dice); // reuse/extract whatever helper the existing tests use
        SetupDiceRoll(20, 14); // d20 -> 14

        var outcome = character.Challenge(new Dr(12), AbilityKind.Toughness, Dice);

        Assert.Equal(14, outcome.Roll);
        Assert.Equal(character.Abilities.Toughness.Modifier, outcome.Modifier);
        Assert.Equal(12, outcome.EffectiveDr);
        Assert.Equal(14 + character.Abilities.Toughness.Modifier >= 12, outcome.IsSuccess);
    }
}
```

(Adapt character construction to the existing test idiom — do not invent a new helper if one exists.)

- [ ] **Step 2: Run to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChallengeOutcomeBreakdownTests"`
Expected: compile FAIL — `Roll` not defined.

- [ ] **Step 3: Implement**

`ChallengeOutcome.cs`:

```csharp
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Challenge;

public class ChallengeOutcome
{
    private ChallengeOutcome(bool isSuccess, Natural natural, int roll, int modifier, int effectiveDr)
    {
        IsSuccess = isSuccess;
        Natural = natural;
        Roll = roll;
        Modifier = modifier;
        EffectiveDr = effectiveDr;
    }

    public bool IsSuccess { get; }
    public Natural Natural { get; }
    /// <summary>Raw d20 roll.</summary>
    public int Roll { get; }
    /// <summary>Ability modifier added to the roll.</summary>
    public int Modifier { get; }
    /// <summary>DR after encumbrance/injury adjustments — what the total was compared against.</summary>
    public int EffectiveDr { get; }

    public static ChallengeOutcome Success(Natural natural, int roll, int modifier, int effectiveDr) =>
        new(true, natural, roll, modifier, effectiveDr);

    public static ChallengeOutcome Fail(Natural natural, int roll, int modifier, int effectiveDr) =>
        new(false, natural, roll, modifier, effectiveDr);
}
```

`Character.Challenge` — final return becomes:

```csharp
var modifier = Abilities[ability].Modifier;
return nat is Natural.One ? ChallengeOutcome.Fail(nat, rollResults, modifier, challenge.Value)
    : nat is Natural.Twenty ? ChallengeOutcome.Success(nat, rollResults, modifier, challenge.Value)
    : outcome >= challenge.Value
        ? ChallengeOutcome.Success(nat, rollResults, modifier, challenge.Value)
        : ChallengeOutcome.Fail(nat, rollResults, modifier, challenge.Value);
```

Build the solution; fix any other `ChallengeOutcome.Success/Fail` callers (tests) by passing the new args.

- [ ] **Step 4: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): challenge outcome carries roll/modifier/DR breakdown"
```

### Task 5: Challenge consequences (domain)

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Core/Characters/Challenge/ChallengeConsequence.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Core/Characters/Challenge/ChallengeResult.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs` (new method near `Challenge`)
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/CharacterService.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Characters/Challenge/ChallengeConsequenceTests.cs` (create)

**Interfaces:**
- Produces: `enum ChallengeConsequence { None, Minor, Serious, Deadly }`; `int Character.SufferConsequence(ChallengeConsequence consequence, Dice dice)` (returns damage taken); `record ChallengeResult(ChallengeOutcome Outcome, int DamageTaken, bool IsDead)`; `CharacterService.ChallengePlayer(Guid characterId, Dr dr, AbilityKind ability, ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)` now returns `ChallengeResult` **and saves the character when damage was applied**. Task 6 consumes all of these.

- [ ] **Step 1: Write the failing tests**

```csharp
using Moq;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests.Characters.Challenge;

public sealed class ChallengeConsequenceTests : TestBase
{
    [Theory]
    [InlineData(ChallengeConsequence.Minor, 2)]   // d2
    [InlineData(ChallengeConsequence.Serious, 6)] // d6
    [InlineData(ChallengeConsequence.Deadly, 10)] // d10
    public void SufferConsequence_RollsSeverityDie_AppliesDamage(ChallengeConsequence severity, int expectedSides)
    {
        var character = TestCharacters.Create(Dice); // same construction idiom as Task 4
        var hpBefore = character.Hp.Current;
        SetupDiceRoll(expectedSides, 1); // severity die -> 1 damage

        var damage = character.SufferConsequence(severity, Dice);

        Assert.Equal(1, damage);
        Assert.Equal(hpBefore - 1, character.Hp.Current);
        MockRandomService.Verify(r => r.GenerateRandomRoll(expectedSides), Times.Once);
    }

    [Fact]
    public void SufferConsequence_None_NoRoll_NoDamage()
    {
        var character = TestCharacters.Create(Dice);
        var hpBefore = character.Hp.Current;

        var damage = character.SufferConsequence(ChallengeConsequence.None, Dice);

        Assert.Equal(0, damage);
        Assert.Equal(hpBefore, character.Hp.Current);
    }

    [Fact]
    public async Task ChallengePlayer_FailureWithConsequence_AppliesDamage_SavesCharacter()
    {
        var character = TestCharacters.Create(Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRolls(1 /* d20 fumble -> fail */, 4 /* d6 consequence */);

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, ChallengeConsequence.Serious);

        Assert.False(result.Outcome.IsSuccess);
        Assert.Equal(4, result.DamageTaken);
        repo.Verify(r => r.Save(character), Times.Once);
    }

    [Fact]
    public async Task ChallengePlayer_Success_NoConsequence_NoSave()
    {
        var character = TestCharacters.Create(Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRoll(20, 20); // natural 20 -> success

        var result = await service.ChallengePlayer(
            character.Id, new Dr(12), AbilityKind.Agility, ChallengeConsequence.Deadly);

        Assert.True(result.Outcome.IsSuccess);
        Assert.Equal(0, result.DamageTaken);
        repo.Verify(r => r.Save(It.IsAny<Character>()), Times.Never);
    }
}
```

Adjust `ICharactersRepository.Get`/`Save` mock signatures to the real interface (check `Core/Characters/ICharactersRepository.cs`).

- [ ] **Step 2: Run to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChallengeConsequenceTests"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`ChallengeConsequence.cs`:

```csharp
namespace WretchedWhispers.Core.Characters.Challenge;

/// <summary>GM ruling for what failing a challenge costs. The model picks the category; the domain rolls the numbers.</summary>
public enum ChallengeConsequence
{
    None,
    /// <summary>d2 damage — scrapes and bruises.</summary>
    Minor,
    /// <summary>d6 damage — a real wound.</summary>
    Serious,
    /// <summary>d10 damage — can kill outright.</summary>
    Deadly
}
```

`ChallengeResult.cs`:

```csharp
namespace WretchedWhispers.Core.Characters.Challenge;

public sealed record ChallengeResult(ChallengeOutcome Outcome, int DamageTaken, bool IsDead);
```

`Character.cs` — add next to `Challenge` (note `ReceiveDamage` is already private on Character; call it directly):

```csharp
public int SufferConsequence(ChallengeConsequence consequence, Dice dice)
{
    if (consequence is ChallengeConsequence.None)
        return 0;

    var severityDie = consequence switch
    {
        ChallengeConsequence.Minor => DiceExpr.D(1, 2),
        ChallengeConsequence.Serious => DiceExpr.D(1, 6),
        ChallengeConsequence.Deadly => DiceExpr.D(1, 10),
        _ => throw new ArgumentOutOfRangeException(nameof(consequence))
    };

    var damage = dice.Roll(severityDie);
    ReceiveDamage(damage, dice);
    return damage;
}
```

Add `using WretchedWhispers.Core.Characters.Challenge;` if not present.

`CharacterService.cs`:

```csharp
public async Task<ChallengeResult> ChallengePlayer(
    Guid characterId, Dr dr, AbilityKind ability,
    ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
{
    var character = await charactersRepository.Get(characterId);
    if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

    var outcome = character.Challenge(dr, ability, dice);

    var damageTaken = 0;
    if (!outcome.IsSuccess && consequenceOnFailure is not ChallengeConsequence.None)
    {
        damageTaken = character.SufferConsequence(consequenceOnFailure, dice);
        await charactersRepository.Save(character);
    }

    return new ChallengeResult(outcome, damageTaken, character.IsDead);
}
```

- [ ] **Step 4: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all PASS (fix any caller of the old `ChallengePlayer` return type — Task 6 covers the tool; if the build breaks there first, apply Task 6's tool change).

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): challenge consequences — GM picks severity, domain rolls damage"
```

### Task 6: ChallengeCharacter tool + DTO

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/CharacterTools.cs:53-64`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/Models/` — `ChallengeOutcomeDto` (find with `rtk grep -rln "ChallengeOutcomeDto" WrtechedWhispers/WretchedWhispers.Api`)

**Interfaces:**
- Consumes: `ChallengeConsequence`, `ChallengeResult`, new `ChallengePlayer` signature (Task 5).
- Produces: `record ChallengeOutcomeDto(bool IsSuccess, int Roll, int Modifier, int Dr, int DamageTaken, bool IsDead)` — the model-facing shape.

- [ ] **Step 1: Implement**

`ChallengeOutcomeDto`:

```csharp
public record ChallengeOutcomeDto(bool IsSuccess, int Roll, int Modifier, int Dr, int DamageTaken, bool IsDead);
```

`CharacterTools.ChallengeCharacter`:

```csharp
[Description("Challenge the character with an ability test against a difficulty rating. On failure, the chosen consequence is applied automatically as rolled damage.")]
[GameTool(SessionStage.Exploration)]
public async Task<ChallengeOutcomeDto> ChallengeCharacter(
    [Description("Level of the challenge, the higher the number the harder. Usually 12 for normal.")]
    int challengeDr,
    [Description("Ability kind to use: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
    AbilityKind abilityKind,
    [Description("What failure costs, chosen like a GM: 'None' (no harm), 'Minor' (d2 — scrapes), 'Serious' (d6 — a real wound), 'Deadly' (d10 — can kill). Match the fiction's stakes.")]
    ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
{
    ToolGuard.InRange(challengeDr, 2, 20, nameof(challengeDr), "12 is a normal challenge");
    var result = await characterService.ChallengePlayer(
        RequireCharacterId(), new Dr(challengeDr), abilityKind, consequenceOnFailure);
    return new ChallengeOutcomeDto(
        result.Outcome.IsSuccess, result.Outcome.Roll, result.Outcome.Modifier,
        result.Outcome.EffectiveDr, result.DamageTaken, result.IsDead);
}
```

Add `using WretchedWhispers.Core.Characters.Challenge;`.

Also update the Exploration stage prompt line in `StagePrompts.cs` from
`ALWAYS call ChallengeCharacter to test against a DR (usually 12). Never narrate success or failure without rolling.` to:

```
- When the character attempts ANY risky action, ALWAYS call ChallengeCharacter to test against a DR
  (usually 12), choosing consequenceOnFailure by the fiction's stakes — None for harmless stumbles,
  Minor for scrapes, Serious for real danger, Deadly when failure should maim or kill. Never narrate
  success, failure, or harm without rolling; weave the returned roll, modifier, DR, and damage into the prose.
```

- [ ] **Step 2: Build + run all tests**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: clean, all PASS (fix any test constructing the old 1-arg `ChallengeOutcomeDto`).

- [ ] **Step 3: Commit, push, open PR**

```bash
rtk git add -A && rtk git commit -m "feat(tools): ChallengeCharacter gains consequence ruling + full dice breakdown"
rtk git push -u origin feat/challenge-consequences
rtk gh pr create --title "Challenge consequences + dice breakdowns" --body "Phase 2 of docs/superpowers/specs/2026-07-05-domain-authority-design.md"
```

---

## Phase 3 — Domain-resolved combat round + campaign auto-start (branch `feat/combat-round`)

### Task 7: Combat round domain types + Encounter/Character support

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Core/Encounters/CombatRound.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Encounters/Encounter.cs` (add `EndByPlayerEscape`)
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs` (add `AttemptFlee`)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Encounters/CombatRoundTypesTests.cs` (create)

**Interfaces:**
- Produces (all in `WretchedWhispers.Core.Encounters` unless noted):
  - `enum PlayerRoundAction { Attack, Flee, Other }`
  - `enum EncounterEndReason { None, AllDefeated, PlayerFled, PlayerDead }`
  - `record AdversaryRetaliation(string AdversaryName, DefenceOutcome Outcome)`
  - `record CombatRoundOutcome(AttackOutcome? PlayerAttack, string? PlayerAttackTarget, ChallengeOutcome? FleeAttempt, IReadOnlyList<AdversaryRetaliation> Retaliations, IReadOnlyList<string> AdversariesFledThisRound, bool EncounterEnded, EncounterEndReason EndReason)`
  - `void Encounter.EndByPlayerEscape()` — ends without the no-active-adversaries guard
  - `ChallengeOutcome Character.AttemptFlee(Dice dice)` — Agility vs DR 12 with the armor's agility penalty
  Task 8 consumes all of these.

- [ ] **Step 1: Write the failing tests**

```csharp
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Tests.Encounters;

public sealed class CombatRoundTypesTests : TestBase
{
    private Encounter CreateStartedEncounter()
    {
        var encounter = Encounter.Create("Test", "desc", EncounterType.Hostile, Dice);
        encounter.AddAdversary(new Adversary(
            "Ghoul", new HitPoints(4, 4), new Armor(ArmorTier.None), 7,
            new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();
        return encounter;
    }

    [Fact]
    public void EndByPlayerEscape_EndsDespiteActiveAdversaries()
    {
        var encounter = CreateStartedEncounter();

        encounter.EndByPlayerEscape();

        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public void EndByPlayerEscape_NotStarted_Throws()
    {
        var encounter = Encounter.Create("Test", "desc", EncounterType.Hostile, Dice);
        Assert.Throws<InvalidOperationException>(encounter.EndByPlayerEscape);
    }

    [Fact]
    public void AttemptFlee_IsAgilityTestAgainstDr12()
    {
        var character = TestCharacters.Create(Dice);
        SetupDiceRoll(20, 15);

        var outcome = character.AttemptFlee(Dice);

        Assert.Equal(15, outcome.Roll);
        // effective DR = 12 + armor agility penalty (+ any injury/encumbrance adjustments)
        Assert.True(outcome.EffectiveDr >= 12);
    }
}
```

(Adapt `Adversary`/`AttackProfile` constructor args to the real signatures in `Core/Adversaries` — mirror `EncounterTools.AddAdversaryToEncounter`.)

- [ ] **Step 2: Run to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CombatRoundTypesTests"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`CombatRound.cs`:

```csharp
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;

namespace WretchedWhispers.Core.Encounters;

/// <summary>The player's declared action for one combat round. 'Other' means the player's action was
/// already resolved by a different tool this turn (scroll, item) — the round still runs retaliation.</summary>
public enum PlayerRoundAction { Attack, Flee, Other }

public enum EncounterEndReason { None, AllDefeated, PlayerFled, PlayerDead }

public sealed record AdversaryRetaliation(string AdversaryName, DefenceOutcome Outcome);

/// <summary>Everything that happened in one domain-resolved combat round, in resolution order:
/// player action, adversary retaliation, morale flights, and whether the encounter ended.</summary>
public sealed record CombatRoundOutcome(
    AttackOutcome? PlayerAttack,
    string? PlayerAttackTarget,
    ChallengeOutcome? FleeAttempt,
    IReadOnlyList<AdversaryRetaliation> Retaliations,
    IReadOnlyList<string> AdversariesFledThisRound,
    bool EncounterEnded,
    EncounterEndReason EndReason);
```

`Encounter.cs` — add next to `EndEncounter`:

```csharp
/// <summary>Ends the encounter because the player escaped — adversaries may still be active.</summary>
public void EndByPlayerEscape()
{
    if (!IsStarted) throw new InvalidOperationException("Can't end an encounter that hasn't started.");
    IsEnded = true;
}
```

`Character.cs` — add next to `Challenge`:

```csharp
/// <summary>Flee from combat: Agility vs DR 12, hindered by armor (MORK BORG flee rule).</summary>
public ChallengeOutcome AttemptFlee(Dice dice) =>
    Challenge(new Dr(12), AbilityKind.Agility, dice, Armor.AgilityPenalty);
```

If `Armor.AgilityPenalty` doesn't exist under that name, use whatever `ResolveDefence` (Character.cs:253) passes as the agility penalty — same expression.

- [ ] **Step 4: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CombatRoundTypesTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): combat round types, player flee, end-by-escape"
```

### Task 8: EncounterService.ResolveRound

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Encounters/EncounterService.cs` — add `ResolveRound`, **delete** `AttackAdversary` and `AttackPlayer`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Encounters/ResolveRoundTests.cs` (create)
- Modify: any tests that called the deleted methods (`rtk grep -rln "AttackAdversary\|AttackPlayer" WrtechedWhispers/WretchedWhispers.Tests`) — port them onto `ResolveRound` (e.g. `AttackHitRateTests` asserts hit-rate over many `ResolveRound(Attack)` calls, reading `outcome.PlayerAttack`)

**Interfaces:**
- Consumes: Task 7's types, `Character.Attack/Defend/AttemptFlee`, `Encounter.ProcessPlayerAttackOutcome/ProcessPlayerDefenceOutcome/EndEncounter/EndByPlayerEscape`.
- Produces: `Task<CombatRoundOutcome> EncounterService.ResolveRound(Guid encounterId, Guid characterId, PlayerRoundAction action, string? targetName = null)`. Task 9 consumes it.

- [ ] **Step 1: Write the failing tests**

```csharp
using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Tests.Encounters;

public sealed class ResolveRoundTests : TestBase
{
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();

    private EncounterService CreateService() =>
        new(Dice, _charactersRepo.Object, _encountersRepo.Object);

    private (Encounter encounter, Character character) Arrange(int adversaries = 1, int adversaryHp = 4)
    {
        var encounter = Encounter.Create("Fight", "desc", EncounterType.Hostile, Dice);
        for (var i = 0; i < adversaries; i++)
            encounter.AddAdversary(new Adversary(
                $"Ghoul {i + 1}", new HitPoints(adversaryHp, adversaryHp), new Armor(ArmorTier.None), 7,
                new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();

        var character = TestCharacters.Create(Dice);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        return (encounter, character);
    }

    [Fact]
    public async Task Attack_ResolvesPlayerAttack_ThenEveryLivingAdversaryRetaliates()
    {
        var (encounter, character) = Arrange(adversaries: 2, adversaryHp: 100);
        // d20 attack (hit but no kill vs 100hp), weapon dmg, then per-adversary defence rolls.
        // Feed generous rolls; assertions below don't depend on exact damage numbers.
        SetupDiceRolls(15, 3, 10, 2, 10, 2);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Ghoul 1");

        Assert.NotNull(outcome.PlayerAttack);
        Assert.Equal("Ghoul 1", outcome.PlayerAttackTarget);
        Assert.Equal(2, outcome.Retaliations.Count);
        Assert.False(outcome.EncounterEnded);
        _encountersRepo.Verify(r => r.Save(encounter), Times.Once);
        _charactersRepo.Verify(r => r.Save(character), Times.Once);
    }

    [Fact]
    public async Task Attack_KillsLastAdversary_AutoEnds_NoRetaliation()
    {
        var (encounter, character) = Arrange(adversaries: 1, adversaryHp: 1);
        SetupDiceRolls(20, 6, 1); // nat-20 hit, damage well past 1 HP

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Ghoul 1");

        Assert.True(outcome.EncounterEnded);
        Assert.Equal(EncounterEndReason.AllDefeated, outcome.EndReason);
        Assert.Empty(outcome.Retaliations);
        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public async Task Flee_Success_EndsEncounter_NoRetaliation()
    {
        var (encounter, character) = Arrange();
        SetupDiceRoll(20, 20); // guaranteed flee success

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Flee, null);

        Assert.NotNull(outcome.FleeAttempt);
        Assert.True(outcome.FleeAttempt.IsSuccess);
        Assert.True(outcome.EncounterEnded);
        Assert.Equal(EncounterEndReason.PlayerFled, outcome.EndReason);
        Assert.Empty(outcome.Retaliations);
        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public async Task Flee_Failure_WastesRound_RetaliationHappens()
    {
        var (encounter, character) = Arrange();
        SetupDiceRolls(1, 2, 1); // nat-1 flee fail, then adversary attack rolls

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Flee, null);

        Assert.False(outcome.FleeAttempt.IsSuccess);
        Assert.Single(outcome.Retaliations);
        Assert.False(encounter.IsEnded);
    }

    [Fact]
    public async Task Other_RunsRetaliationOnly()
    {
        var (encounter, character) = Arrange();
        SetupDiceRolls(2, 1); // adversary attack rolls only

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Other, null);

        Assert.Null(outcome.PlayerAttack);
        Assert.Null(outcome.FleeAttempt);
        Assert.Single(outcome.Retaliations);
    }

    [Fact]
    public async Task Attack_UnknownTargetName_FallsBackToFirstLiving()
    {
        var (encounter, character) = Arrange(adversaries: 1, adversaryHp: 100);
        SetupDiceRolls(15, 3, 10, 2);

        var outcome = await CreateService().ResolveRound(
            encounter.Id, character.Id, PlayerRoundAction.Attack, "Nonexistent");

        Assert.Equal("Ghoul 1", outcome.PlayerAttackTarget);
    }

    [Fact]
    public async Task NotStartedEncounter_Throws()
    {
        var encounter = Encounter.Create("Idle", "desc", EncounterType.Hostile, Dice);
        var character = TestCharacters.Create(Dice);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().ResolveRound(encounter.Id, character.Id, PlayerRoundAction.Attack, null));
    }
}
```

Dice-roll sequences are indicative — `Character.Attack`/`Defend` consume rolls for hit, damage, armor reduction, and injury tables. If a sequence starves, extend it (the `SetupDiceRolls` fallback returns random, which breaks determinism — always provide enough rolls). Adapt repository mock signatures to the real interfaces.

- [ ] **Step 2: Run to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ResolveRoundTests"`
Expected: compile FAIL — `ResolveRound` not defined.

- [ ] **Step 3: Implement**

In `EncounterService.cs`, delete `AttackAdversary` and `AttackPlayer`, add:

```csharp
public async Task<CombatRoundOutcome> ResolveRound(
    Guid encounterId, Guid characterId, PlayerRoundAction action, string? targetName = null)
{
    var encounter = await encountersRepository.Get(encounterId)
        ?? throw new InvalidOperationException("Encounter not found");
    var character = await charactersRepository.Get(characterId)
        ?? throw new InvalidOperationException("Character not found");
    if (!encounter.IsStarted || encounter.IsEnded)
        throw new InvalidOperationException("The encounter is not in active combat.");

    AttackOutcome? playerAttack = null;
    string? attackedName = null;
    ChallengeOutcome? fleeAttempt = null;
    var playerFled = false;
    var fledBefore = encounter.Adversaries.Where(a => a.IsFled).Select(a => a.Name).ToHashSet();

    switch (action)
    {
        case PlayerRoundAction.Attack:
        {
            var living = ActiveAdversaries(encounter);
            if (living.Count == 0)
                throw new InvalidOperationException("No living adversaries remain.");
            var target = living.FirstOrDefault(a =>
                    a.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                ?? living[0];
            var outcome = character.Attack(target.Armor, dice);
            encounter.ProcessPlayerAttackOutcome(outcome, target.Id, dice);
            playerAttack = outcome;
            attackedName = target.Name;
            break;
        }
        case PlayerRoundAction.Flee:
            fleeAttempt = character.AttemptFlee(dice);
            playerFled = fleeAttempt.IsSuccess;
            break;
        case PlayerRoundAction.Other:
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(action));
    }

    var retaliations = new List<AdversaryRetaliation>();
    if (!playerFled)
    {
        foreach (var adversary in ActiveAdversaries(encounter))
        {
            if (character.IsDead) break;
            var defence = character.Defend(adversary.Attack.DamageDie, dice);
            encounter.ProcessPlayerDefenceOutcome(defence, adversary.Id);
            retaliations.Add(new AdversaryRetaliation(adversary.Name, defence));
        }
    }

    var fledThisRound = encounter.Adversaries
        .Where(a => a.IsFled && !fledBefore.Contains(a.Name))
        .Select(a => a.Name)
        .ToList();

    var endReason =
        playerFled ? EncounterEndReason.PlayerFled
        : character.IsDead ? EncounterEndReason.PlayerDead
        : ActiveAdversaries(encounter).Count == 0 ? EncounterEndReason.AllDefeated
        : EncounterEndReason.None;

    if (endReason is EncounterEndReason.AllDefeated) encounter.EndEncounter();
    else if (endReason is EncounterEndReason.PlayerFled) encounter.EndByPlayerEscape();
    // PlayerDead: DeriveStage's IsDead check takes over; the encounter is left as-is.

    await charactersRepository.Save(character);
    await encountersRepository.Save(encounter);

    return new CombatRoundOutcome(
        playerAttack, attackedName, fleeAttempt, retaliations, fledThisRound,
        endReason is not EncounterEndReason.None, endReason);
}

private static IReadOnlyList<Adversary> ActiveAdversaries(Encounter encounter) =>
    encounter.Adversaries.Where(a => a is { IsDead: false, IsFled: false }).ToList();
```

Add usings for `WretchedWhispers.Core.Characters.Challenge` and `WretchedWhispers.Core.Adversaries` as needed.

- [ ] **Step 4: Port orphaned tests, run everything**

Rewrite tests that used the deleted service methods over `ResolveRound` (hit-rate style tests read `outcome.PlayerAttack.Value.Hit`).
Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all PASS. (EncounterTools still calls the deleted methods — if the Api project breaks the build, do Task 9's tool swap in this same commit.)

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): domain-resolved combat round replaces per-attack service calls"
```

### Task 9: ResolveCombatRound tool; delete per-attack tools

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/EncounterTools.cs` — add `ResolveCombatRound`, **delete** `AttackPlayer`, `AttackAdversary`, `EndEncounter` methods and the now-unused `AdversaryAttackOutcomeDto`/`CharacterAttackOutcomeDto` (verify unused first: `rtk grep -rln "AdversaryAttackOutcomeDto\|CharacterAttackOutcomeDto" WrtechedWhispers/`)
- Create: `WrtechedWhispers/WretchedWhispers.Api/GameTools/Models/CombatRoundOutcomeDto.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/CharacterTools.cs:157` — `CastScroll` gains Combat stage
- Modify: tests referencing the deleted tools (`rtk grep -rln "AttackPlayer\|AttackAdversary\|EndEncounter" WrtechedWhispers/WretchedWhispers.Tests`)

**Interfaces:**
- Consumes: `EncounterService.ResolveRound`, `CombatRoundOutcome` (Task 8).
- Produces: model-facing tool `ResolveCombatRound(string action, string? targetAdversaryName = null)` returning `CombatRoundOutcomeDto`; Combat stage tool set becomes `ResolveCombatRound`, `CastScroll`, `Roll`.

- [ ] **Step 1: Implement DTO**

`CombatRoundOutcomeDto.cs`:

```csharp
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.GameTools.Models;

public sealed record PlayerAttackDto(
    string Target, bool Hit, int Damage, bool Critical, bool Fumble,
    bool WeaponBroken, bool TargetArmorDegraded, int BaseDamageRoll, int DamageReduction);

public sealed record FleeAttemptDto(bool Success, int Roll, int Modifier, int Dr);

public sealed record RetaliationDto(
    string AdversaryName, int DamageDealt, bool Avoided, bool CriticalFreeAttack, bool FumbleDoubleDamage);

public sealed record CombatRoundOutcomeDto(
    PlayerAttackDto? PlayerAttack,
    FleeAttemptDto? FleeAttempt,
    IReadOnlyList<RetaliationDto> Retaliations,
    IReadOnlyList<string> AdversariesFled,
    bool EncounterEnded,
    string EndReason)
{
    public static CombatRoundOutcomeDto From(CombatRoundOutcome outcome) => new(
        outcome.PlayerAttack is { } attack && outcome.PlayerAttackTarget is { } target
            ? new PlayerAttackDto(target, attack.Hit, attack.Damage.Amount, attack.Critical, attack.Fumble,
                attack.WeaponBroken, attack.TargetArmorDegraded, attack.BaseDamageRoll, attack.DamageReduction)
            : null,
        outcome.FleeAttempt is { } flee
            ? new FleeAttemptDto(flee.IsSuccess, flee.Roll, flee.Modifier, flee.EffectiveDr)
            : null,
        outcome.Retaliations
            .Select(r => new RetaliationDto(r.AdversaryName, r.Outcome.DamageDealt, r.Outcome.Avoided,
                r.Outcome.CriticalFreeAttack, r.Outcome.FumbleDoubleDamage))
            .ToList(),
        outcome.AdversariesFledThisRound,
        outcome.EncounterEnded,
        outcome.EndReason.ToString());
}
```

- [ ] **Step 2: Implement tool swap**

In `EncounterTools.cs`, delete the `AttackPlayer`, `AttackAdversary`, and `EndEncounter` methods (and `LivingAdversaries()` if now unused), add:

```csharp
[Description("Resolve EXACTLY ONE combat round from the player's action: resolves the player's attack or flee attempt, then every living adversary's retaliation, morale, and ends the encounter automatically when the fight is over. Call it once per player combat action — never more.")]
[GameTool(SessionStage.Combat)]
public async Task<CombatRoundOutcomeDto> ResolveCombatRound(
    [Description("The player's action this round: 'Attack' (strike an adversary), 'Flee' (attempt to escape), or 'Other' (the player's action was already resolved with another tool this turn — enemies still respond)")]
    string action,
    [Description("Name of the adversary to attack (Attack only; defaults to the nearest living adversary)")]
    string? targetAdversaryName = null)
{
    if (!Enum.TryParse<PlayerRoundAction>(action, ignoreCase: true, out var roundAction))
        throw new ArgumentException(
            $"Action '{action}' is not valid. Expected one of: Attack, Flee, Other.");

    var outcome = await encounterService.ResolveRound(
        RequireEncounterId(), RequireCharacterId(), roundAction, targetAdversaryName);
    return CombatRoundOutcomeDto.From(outcome);
}
```

In `CharacterTools.cs`, change `CastScroll`'s attribute:

```csharp
[GameTool(SessionStage.Exploration, SessionStage.Combat)]
```

- [ ] **Step 3: Fix tests, build, run**

Update `AgentToolProviderTests`/stage-map tests expecting the old Combat tool set (now: `ResolveCombatRound`, `CastScroll`, `Roll`).
Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: clean, all PASS.

- [ ] **Step 4: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(tools): one ResolveCombatRound tool replaces per-attack combat tools"
```

### Task 10: Campaign auto-start

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/CampaignService.cs` — add `ConfigureCampaign`, auto-start in `JoinCampaign`, **delete** `StartCampaign` (verify no remaining callers: `rtk grep -rln "StartCampaign" WrtechedWhispers --include="*.cs"` — evals get updated in Task 11)
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/CampaignTools.cs` — `ConfigureCampaign` via service, **delete** the `StartCampaign` tool
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/CampaignAutoStartTests.cs` (create); update `CampaignServiceTests` if it used `StartCampaign`

**Interfaces:**
- Produces: `bool Campaign.IsConfigured` (`[JsonInclude]`, set by `Configure`); `Task<Campaign> CampaignService.ConfigureCampaign(Guid campaignId, DiceExpr dawnDice, string name, string description)`; auto-start rule: campaign starts the moment it is configured **and** has a player, whichever happens last.

- [ ] **Step 1: Write the failing tests**

```csharp
using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignAutoStartTests : TestBase
{
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();

    private CampaignService CreateService() =>
        new(_campaignsRepo.Object, _charactersRepo.Object, Dice);

    [Fact]
    public async Task Configure_ThenJoin_AutoStarts()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = CreateService();

        await service.ConfigureCampaign(campaign.Id, DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.False(campaign.IsActive()); // configured but no player yet

        await service.JoinCampaign(campaign.Id, character.Id);
        Assert.True(campaign.IsActive()); // both conditions met -> started
    }

    [Fact]
    public async Task Join_ThenConfigure_AutoStarts()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = CreateService();

        await service.JoinCampaign(campaign.Id, character.Id);
        Assert.False(campaign.IsActive()); // player joined, not configured yet

        await service.ConfigureCampaign(campaign.Id, DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.True(campaign.IsActive());
    }

    [Fact]
    public void Configure_SetsIsConfigured()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        Assert.False(campaign.IsConfigured);
        campaign.Configure(DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.True(campaign.IsConfigured);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignAutoStartTests"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`Campaign.cs` — JsonConstructor gains `bool isConfigured = false` (assign it), plus:

```csharp
[JsonInclude] public bool IsConfigured { get; private set; }
```

and in `Configure(...)`, after assigning name/description:

```csharp
IsConfigured = true;
```

`CampaignService.cs` — delete `StartCampaign`, add/modify:

```csharp
public async Task<Campaign> ConfigureCampaign(Guid campaignId, DiceExpr dawnDice, string name, string description)
{
    var campaign = await campaignsRepository.Get(campaignId);
    if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

    campaign.Configure(dawnDice, name, description);
    TryAutoStart(campaign);
    await campaignsRepository.SaveCampaign(campaign);
    return campaign;
}

public async Task JoinCampaign(Guid campaignId, Guid characterId)
{
    var character = await charactersRepository.Get(characterId);
    if (character is null) throw new ArgumentException($"Character with {characterId} doesn't exist.");

    var campaign = await campaignsRepository.Get(campaignId);
    if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

    campaign.JoinGame(character.Id);
    TryAutoStart(campaign);
    await campaignsRepository.SaveCampaign(campaign);
}

// The campaign begins the moment it is configured AND has a player — a deterministic domain rule,
// not a model decision. Order-independent.
private static void TryAutoStart(Campaign campaign)
{
    if (campaign is { IsConfigured: true, IsEnded: false } && campaign.Players.Count > 0 && !campaign.IsActive())
        campaign.Start();
}
```

(`IsStarted` is internal to Core, so `!campaign.IsActive()` + `IsEnded: false` together mean "not yet started". `Campaign.Start()` still throws if called twice — `TryAutoStart`'s guard prevents that.)

`CampaignTools.cs` — delete the `StartCampaign` tool method; rewrite `ConfigureCampaign` to use the service:

```csharp
[Description("Configure the campaign's name, description, and dawn roll pace. The campaign already exists; it begins automatically once it is configured and the character has been created.")]
[GameTool(SessionStage.CharacterCreation, SessionStage.CampaignSetup)]
public async Task<CampaignDto> ConfigureCampaign(
    [Description("Dice expression for dawn rolls (e.g., 'd100' for very slow, 'd6' for fast)")]
    string diceExpression,
    [Description("The name of the campaign")] string name,
    [Description("A description of the campaign's setting, goals, or theme")] string description)
{
    ToolGuard.DiceExpression(diceExpression, nameof(diceExpression));
    var campaign = await campaignService.ConfigureCampaign(
        RequireCampaignId(), DiceExpr.Parse(diceExpression), name, description);
    return CreateCampaignDto(campaign);
}
```

(`RequireCampaign()` and the `campaignsRepository` constructor dependency become unused if nothing else uses them — delete what dies.)

- [ ] **Step 4: Run everything**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all PASS (update any `CampaignServiceTests` that called `StartCampaign` to assert the auto-start rule instead).

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): campaign auto-starts when configured and joined"
```

### Task 11: Prompt rewrites + eval expectation update

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Prompts/StagePrompts.cs` (`CharacterCreation`, `CampaignSetup`, `Combat`)
- Modify: `WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs:17-18`

- [ ] **Step 1: Rewrite prompts**

`CharacterCreation` — STEP 2's numbered list becomes (STEP 1 and the closing narration guidance stay as-is):

```
STEP 2 — On the player's next message (their name), run the entire opening in ONE turn,
calling tools FIRST and then narrating their results (never invent stats or outcomes):
  1. CreateCharacter with the given name.
  2. ConfigureCampaign — give the campaign a doom-appropriate name and description and
     choose a fitting dawn-roll pace yourself (the world is ending; lean ominous). The
     campaign begins automatically once the character exists and the campaign is configured.
Then narrate their wretched origins as the rolled stats and pitiful gear are revealed
(weave in the REAL numbers the tools returned), and the rotting town they wake in. End by
handing control over -- describe the world around them and ask what they do. Do not present
a rigid A/B/C/D menu as if the list is the game; offer the world and let them act.
```

`CampaignSetup` becomes:

```
A character exists but the campaign has not started yet. Finish the setup seamlessly in this
turn -- do not interrogate the player with menus. Call ConfigureCampaign with a doom-appropriate
name, description, and a fitting dawn-roll pace you choose (the world is ending; lean ominous);
the campaign begins automatically. Then narrate the rotting world they wake into and end by
asking what they do.
```

`Combat` becomes:

```
Combat is underway. The player acts once per message.

If the player's message is a question, clarification, inventory/status check, or rules
discussion, answer from the Game State and STOP. Call no tools. A question is not a combat round.

When the player acts, call ResolveCombatRound EXACTLY ONCE:
- Attacking: action 'Attack' with the target's name.
- Fleeing: action 'Flee'.
- Anything else (cast a scroll, use an item): first verify the required item/resource exists in
  Game State (if it does not and cannot be obtained now, explain in-world and STOP — no round
  happens), then resolve it with its matching tool, then call ResolveCombatRound with action
  'Other' so the enemies respond.

The round result contains everything that happened: the player's outcome, every enemy's
retaliation, who fled, and whether the fight ended. Narrate exactly those results — real hits,
misses, damage, deaths — weaving the dice into the prose using the returned breakdown (e.g.
"the bolt bites for 8, doubled to 16 on the crit, 2 turned by rusted mail — 14 left").
NEVER invent an outcome the round result does not report.

Combat is brutal and fast in MORK BORG. One round per message, then return control to the player.
```

- [ ] **Step 2: Update eval expectations**

`CampaignCreationEvals.cs:17-18`:

```csharp
private static readonly string[] CreateCampaignTools =
    ["CreateCharacter", "ConfigureCampaign"];
```

Note for the reviewer: eval response caches under `.eval-results` may still hold the old three-call cached conversations; delete the cache directory if a cached eval run misleads (`rtk rm -rf WrtechedWhispers/WretchedWhispers.Evals/bin/**/.eval-results` — or just note it).

- [ ] **Step 3: Build + full test run + commit + PR**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: clean, all PASS.

```bash
rtk git add -A && rtk git commit -m "feat(prompts): stage prompts carry judgment only — sequencing moved to domain"
rtk git push -u origin feat/combat-round
rtk gh pr create --title "Domain-resolved combat round + campaign auto-start" --body "Phase 3 of docs/superpowers/specs/2026-07-05-domain-authority-design.md"
```

---

## Phase 4 — Campaign journal (branch `feat/campaign-journal`)

### Task 12: Journal domain

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/JournalEntry.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/CampaignService.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/CampaignJournalTests.cs` (create)

**Interfaces:**
- Produces: `enum JournalCategory { Npc, Location, Promise, Quest, Event }`; `record JournalEntry(JournalCategory Category, string Text, int Day, int Hour)`; `IReadOnlyList<JournalEntry> Campaign.JournalEntries`; `void Campaign.RecordJournalEntry(JournalCategory category, string text)`; `Task<Campaign> CampaignService.RecordJournalEntry(Guid campaignId, JournalCategory category, string text)`. Task 13 consumes these.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignJournalTests : TestBase
{
    [Fact]
    public void RecordJournalEntry_StampsCampaignDayAndHour()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");

        campaign.RecordJournalEntry(JournalCategory.Npc, "Grimlod the flagellant, met at the gallows");

        var entry = Assert.Single(campaign.JournalEntries);
        Assert.Equal(JournalCategory.Npc, entry.Category);
        Assert.Equal(campaign.CurrentDay, entry.Day);
        Assert.Equal(campaign.CurrentHour, entry.Hour);
    }

    [Fact]
    public void RecordJournalEntry_EmptyText_Throws()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");
        Assert.Throws<ArgumentException>(() => campaign.RecordJournalEntry(JournalCategory.Event, "  "));
    }

    [Fact]
    public void Journal_SurvivesJsonRoundTrip()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Doom", "The end");
        campaign.RecordJournalEntry(JournalCategory.Promise, "Swore to bring the hangman's rope by dawn");

        // Round-trip with the same serializer setup the campaign repository uses
        // (see Infrastructure/Persistence/Serialization — mirror its JsonSerializerOptions).
        var json = JsonSerializer.Serialize(campaign);
        var restored = JsonSerializer.Deserialize<Campaign>(json);

        Assert.NotNull(restored);
        var entry = Assert.Single(restored.JournalEntries);
        Assert.Equal("Swore to bring the hangman's rope by dawn", entry.Text);
    }
}
```

Check `Infrastructure/Persistence/Serialization/` for the options the campaign repository actually uses and mirror them in the round-trip test (private members need the right resolver — if a `CampaignSerialization` helper exists, use it).

- [ ] **Step 2: Run to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignJournalTests"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`JournalEntry.cs`:

```csharp
namespace WretchedWhispers.Core.Campaigns;

public enum JournalCategory { Npc, Location, Promise, Quest, Event }

/// <summary>An append-only fact in the campaign's fiction, stamped with campaign time. The GM's
/// durable memory: what is not written here is forgotten when chat history is summarized.</summary>
public sealed record JournalEntry(JournalCategory Category, string Text, int Day, int Hour);
```

`Campaign.cs`:
- JsonConstructor gains `List<JournalEntry>? journal = null` parameter; assign `Journal = journal ?? [];`
- Add member (follow the `Characters` idiom):

```csharp
[JsonInclude] internal List<JournalEntry> Journal { get; }

[JsonIgnore] public IReadOnlyList<JournalEntry> JournalEntries => Journal.AsReadOnly();

public void RecordJournalEntry(JournalCategory category, string text)
{
    if (string.IsNullOrWhiteSpace(text))
        throw new ArgumentException("Journal text must not be empty.", nameof(text));
    Journal.Add(new JournalEntry(category, text.Trim(), CurrentDay, CurrentHour));
}
```

- Update `Create(...)` to pass `[]` (or rely on the default null → `[]`).

`CampaignService.cs`:

```csharp
public async Task<Campaign> RecordJournalEntry(Guid campaignId, JournalCategory category, string text)
{
    var campaign = await campaignsRepository.Get(campaignId);
    if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

    campaign.RecordJournalEntry(category, text);
    await campaignsRepository.SaveCampaign(campaign);
    return campaign;
}
```

- [ ] **Step 4: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignJournalTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(core): append-only campaign journal stamped with campaign time"
```

### Task 13: Journal tool + prompt injection

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/GameTools/CampaignTools.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Services/SessionContext.cs` (`FormatSnapshot`)
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Prompts/NarratorPersona.cs`

**Interfaces:**
- Consumes: `CampaignService.RecordJournalEntry`, `JournalCategory`, `Campaign.JournalEntries` (Task 12).
- Produces: tool `RecordJournalEntry(JournalCategory category, string text)` in stages Exploration/Combat/Resolution; `## Campaign Journal`-style block inside the Game State snapshot.

- [ ] **Step 1: Implement**

`CampaignTools.cs` — add:

```csharp
[Description("Record a lasting fact in the campaign journal — the GM's memory of the fiction. Use it the moment something durable is established: an NPC met, a location discovered, a promise made, a quest taken, or a notable event (a death, a betrayal, a discovery).")]
[GameTool(SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution)]
public async Task<string> RecordJournalEntry(
    [Description("Kind of fact: 'Npc', 'Location', 'Promise', 'Quest', or 'Event'")]
    JournalCategory category,
    [Description("One concise line stating the fact, e.g. 'Grimlod the flagellant owes the character a lantern'")]
    string text)
{
    var campaign = await campaignService.RecordJournalEntry(RequireCampaignId(), category, text);
    return $"Recorded. The journal holds {campaign.JournalEntries.Count} entries.";
}
```

`SessionContext.FormatSnapshot` — inside the existing `if (Campaign is not null)` block, after the miseries line:

```csharp
if (Campaign.JournalEntries.Count > 0)
{
    // ponytail: full injection, cap/retrieval when journals outgrow the context budget
    sb.AppendLine("  Journal:");
    foreach (var entry in Campaign.JournalEntries)
        sb.AppendLine($"    [Day {entry.Day}, {entry.Category}] {entry.Text}");
}
```

`NarratorPersona.cs` — add to the "Output rules" list:

```
- Maintain the campaign journal: the moment the fiction establishes a durable fact — an NPC
  met, a location discovered, a promise made, a quest taken, a notable death or event — record
  it with RecordJournalEntry. The Journal in Game State is your only durable memory of the
  fiction; a fact you do not record will be forgotten.
```

- [ ] **Step 2: Build + full test run**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: clean, all PASS (update stage-map tests for the three stages gaining `RecordJournalEntry`).

- [ ] **Step 3: Commit, push, PR**

```bash
rtk git add -A && rtk git commit -m "feat(tools): campaign journal tool + snapshot injection"
rtk git push -u origin feat/campaign-journal
rtk gh pr create --title "Campaign journal — durable fictional memory" --body "Phase 4 of docs/superpowers/specs/2026-07-05-domain-authority-design.md"
```

---

## Phase 5 — Evals (branch `feat/domain-authority-evals`)

### Task 14: Capture tool results in the eval harness

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs`

**Interfaces:**
- Produces: `TurnOutcome` gains `IReadOnlyList<ToolResult> ToolResults` (the Api `ToolResult` event record: function name + result payload):

```csharp
public sealed record TurnOutcome(
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<ToolResult> ToolResults,
    ChatResponse Response,
    string Narrative);
```

- [ ] **Step 1: Implement**

In `RunTurnAsync`, keep the existing `toolCalls` list and additionally collect the events:

```csharp
var toolResults = new List<ToolResult>();
// in the event loop:
if (evt is ToolResult tr)
{
    toolCalls.Add(tr.Function);
    toolResults.Add(tr);
}
```

and return `new TurnOutcome(toolCalls, toolResults, response, narrative.ToString());`. Fix the record and any construction sites.

- [ ] **Step 2: Build + run**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln && rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
rtk git add -A && rtk git commit -m "feat(evals): TurnOutcome carries tool results for groundedness checks"
```

### Task 15: ToolCallContainsEvaluator + combat-round & journal evals

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallContainsEvaluator.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallContainsEvaluatorTests.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Evals/DomainAuthorityEvals.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs` — add `CreateExplorationAsync` if absent (mirror `CreateCombatAsync` but do not create/start an encounter; campaign configured + started + character joined)

**Interfaces:**
- Consumes: `EvalHost.CreateCombatAsync`, `TurnOutcome` (Task 14), `ToolCallOrderEvaluator` + `ExpectedToolCallOrderContext` idioms (copy their file layout).
- Produces: `ToolCallContainsEvaluator` (metric `"Tool Call Contains"`) + `RequiredToolCallsContext(string[] Required)` — passes when every required tool name appears among the actual calls (order-insensitive, extras allowed).

- [ ] **Step 1: Write the evaluator test (deterministic — no model needed)**

Copy the structure of `ToolCallOrderEvaluatorTests.cs`:

```csharp
[Fact]
public async Task Passes_WhenRequiredCallPresent_AmongOthers()
{
    var response = ResponseWithCalls("ChallengeCharacter", "RecordJournalEntry"); // same helper style as the order tests
    var result = await new ToolCallContainsEvaluator().EvaluateAsync(
        [], response, additionalContext: [new RequiredToolCallsContext(["RecordJournalEntry"])]);
    Assert.True(result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName).Value);
}

[Fact]
public async Task Fails_WhenRequiredCallMissing()
{
    var response = ResponseWithCalls("ChallengeCharacter");
    var result = await new ToolCallContainsEvaluator().EvaluateAsync(
        [], response, additionalContext: [new RequiredToolCallsContext(["RecordJournalEntry"])]);
    Assert.False(result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName).Value);
}
```

- [ ] **Step 2: Run to verify it fails, then implement the evaluator**

Mirror `ToolCallOrderEvaluator` exactly, with:

```csharp
public sealed class RequiredToolCallsContext(string[] required)
    : EvaluationContext(ContextName, string.Join(", ", required))
{
    public const string ContextName = "Required Tool Calls";
    public string[] Required { get; } = required;
}
```

(match however `ExpectedToolCallOrderContext` derives from `EvaluationContext` — copy its base-call shape) and the pass condition:

```csharp
bool passed = context.Required.All(r => actual.Contains(r, StringComparer.Ordinal));
```

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ToolCallContainsEvaluatorTests"` → PASS.

- [ ] **Step 3: Add the live eval scenarios**

`DomainAuthorityEvals.cs` — copy `CampaignCreationEvals`'s skeleton (`TryCreateAzureChatClient`, `CreateReportingConfiguration` — extract these two into a shared `Harness/EvalSupport.cs` static class instead of duplicating; update `CampaignCreationEvals` to use it):

```csharp
[Fact]
public async Task Combat_PlayerAttack_ResolvesExactlyOneRound()
{
    // ...standard skeleton, CreateCombatAsync host...
    var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!");

    EvaluationResult result = await run.EvaluateAsync(
        messages: [], modelResponse: outcome.Response,
        additionalContext: [new ExpectedToolCallOrderContext(["ResolveCombatRound"])]);

    var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
    Assert.True(metric.Value, $"Expected exactly one ResolveCombatRound; got [{string.Join(", ", outcome.ToolCalls)}]");
}

[Fact]
public async Task Exploration_MemorableNpc_GetsJournaled()
{
    // ...standard skeleton, CreateExplorationAsync host...
    var outcome = await host.CreateTurnRunner().RunTurnAsync(
        "I approach the gallows-keeper, ask his name, and swear I'll bring him the hangman's rope by dawn.");

    EvaluationResult result = await run.EvaluateAsync(
        messages: [], modelResponse: outcome.Response,
        additionalContext: [new RequiredToolCallsContext(["RecordJournalEntry"])]);

    var metric = result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
    Assert.True(metric.Value, $"Expected a RecordJournalEntry call; got [{string.Join(", ", outcome.ToolCalls)}]");
}
```

Register both evaluators in the reporting configuration's `evaluators:` list. If `CreateExplorationAsync` doesn't exist on `EvalHost`, add it by copying `CreateCombatAsync` and dropping the encounter seeding (campaign must be configured + started with the seeded character joined — after Phase 3, `Configure` + `JoinGame` auto-start it).

- [ ] **Step 4: Run + commit**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~DomainAuthorityEvals|FullyQualifiedName~ToolCallContains"`
Expected: evaluator tests PASS; live evals PASS with Azure credentials, SKIP without.

```bash
rtk git add -A && rtk git commit -m "feat(evals): combat-round and journal-recording scenarios"
```

### Task 16: Groundedness judge eval

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj` — add `Microsoft.Extensions.AI.Evaluation.Quality`, **same version** as the existing `Microsoft.Extensions.AI.Evaluation` reference
- Modify: `WrtechedWhispers/WretchedWhispers.Evals/DomainAuthorityEvals.cs`

- [ ] **Step 1: Add package**

```bash
rtk dotnet add WrtechedWhispers/WretchedWhispers.Evals package Microsoft.Extensions.AI.Evaluation.Quality
```

- [ ] **Step 2: Add the scenario**

```csharp
[Fact]
public async Task Combat_Narration_IsGroundedInToolResults()
{
    // ...standard skeleton; include new GroundednessEvaluator() in the reporting config's evaluators...
    var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!");

    var groundingContext = string.Join("\n",
        outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

    EvaluationResult result = await run.EvaluateAsync(
        messages: [new ChatMessage(ChatRole.User, "I strike the plague priest with my staff!")],
        modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
        additionalContext: [new GroundednessEvaluatorContext(groundingContext)]);

    var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
    Assert.NotNull(metric.Value);
    Assert.True(metric.Value >= 4, $"Groundedness {metric.Value}: narration drifted from tool results. Narrative: {outcome.Narrative}");
}
```

Adjust names to the package's actual API (`GroundednessEvaluator`, `GroundednessEvaluatorContext`, metric-name constant) — check with `rtk grep -rn "class Groundedness" ~/.nuget/packages/microsoft.extensions.ai.evaluation.quality/` after restore. `ToolResult`'s payload property may be named differently (check `Api/Models/GameTurnEvent.cs`).

- [ ] **Step 3: Run, commit, push, PR**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all PASS (live evals skip without credentials).

```bash
rtk git add -A && rtk git commit -m "feat(evals): LLM-judge groundedness check of narration vs tool results"
rtk git push -u origin feat/domain-authority-evals
rtk gh pr create --title "Evals: combat round, journal recording, groundedness" --body "Phase 5 of docs/superpowers/specs/2026-07-05-domain-authority-design.md"
```

---

## Final verification (after Phase 5)

- [ ] `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln` — clean
- [ ] `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln` — all green
- [ ] Manual smoke (if Azure credentials available): run the API, create a session, play through character creation → exploration challenge with a consequence → combat round → flee/kill → resolution. Verify: campaign auto-started, combat needed one tool call per round, journal entries appear in the snapshot, no GUID leaks.
- [ ] Update memory: combat redesign + deterministic stage management issues are resolved by this work.
