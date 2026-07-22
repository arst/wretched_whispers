# New Character, Same World Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After character death, let the player bury the wretch and roll a new one into the same world (map, journal, misery clock persist), or abandon the world permanently.

**Architecture:** Death stays derived, never stored. New stored state: a `Fallen` graveyard list on `Campaign` plus additional chat-session rows (one chronicle per wretch, active = newest by `StartedAt`). Burying empties `Players` → stage derives to `CharacterCreation` → existing creation tools take over. The old chronicle is compacted into a past-tense "epitaph" summary seeded as the new chronicle's `ChatSummary`, which `ChatHistoryReducer.Compose` already injects every turn. Status gains one derived value, `"fallen"`, which the UI keys the choice panel on.

**Tech Stack:** .NET 10 (Core/Infrastructure/Engine/Api), xUnit + Moq + WebApplicationFactory, Next.js + Zustand + Tailwind v4 (doom palette).

**Spec:** `docs/superpowers/specs/2026-07-22-new-character-same-world-design.md`

## Global Constraints

- Solution: `WrtechedWhispers/WrtechedWhispers.sln` (directory typo is intentional). Build/test from repo root with absolute or repo-relative paths.
- Never use the null-forgiving operator (`!`) — use proper validation (`Assert.NotNull` + `.Value`, guard clauses).
- Sealed domain entities with `[JsonConstructor]`; new persisted fields are optional constructor params defaulting to null (backward-compatible blobs).
- Test idioms: `TestBase` gives `Dice` + `MockRandomService` (`SetupDiceRoll(sides, result)` is 0-based: stored result + 1 = rolled value). `SetupDiceRolls(...)` queues sequential rolls.
- Run tests excluding live evals: `--filter "FullyQualifiedName!~Evals"`. The full suite (with evals, costs API calls) runs once at the end because a stage prompt changes.
- Prefix shell commands with `rtk`.
- World-ended (7 miseries) and `Campaign.End()` stay terminal. Only character death is recoverable.

---

### Task 1: Domain — `FallenCharacter` + `Campaign.BuryCharacter`

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/FallenCharacter.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/CampaignGraveyardTests.cs` (new)

**Interfaces:**
- Consumes: existing `Campaign` internals (`Characters`, `Journal`, `CurrentDay`, `RecordJournalEntry`, `JournalCategory.Event`).
- Produces: `public sealed record FallenCharacter(Guid Id, string Name, int DayDied);` — `campaign.BuryCharacter(Guid characterId, string name)` (throws `ArgumentException` if id not in `Characters`) — `campaign.FallenCharacters` (`IReadOnlyList<FallenCharacter>`). Later tasks rely on these exact names.

- [ ] **Step 1: Write the failing tests**

Create `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/CampaignGraveyardTests.cs` (mirrors `CampaignMapTests` idiom, including the JSON backward-compat test):

```csharp
using System.Text.Json;
using Xunit;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignGraveyardTests : TestBase
{
    private readonly JsonSerializerOptions _options = AggregateJsonOptions.Create();

    private static Campaign StartedCampaignWith(Guid characterId)
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        campaign.JoinGame(characterId);
        campaign.Start();
        return campaign;
    }

    [Fact]
    public void BuryCharacter_MovesPlayerToGraveyard_StampedWithCurrentDay()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);

        campaign.BuryCharacter(characterId, "Grimnir");

        Assert.Empty(campaign.Players);
        var fallen = Assert.Single(campaign.FallenCharacters);
        Assert.Equal(characterId, fallen.Id);
        Assert.Equal("Grimnir", fallen.Name);
        Assert.Equal(campaign.CurrentDay, fallen.DayDied);
    }

    [Fact]
    public void BuryCharacter_RecordsJournalEntry()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);

        campaign.BuryCharacter(characterId, "Grimnir");

        Assert.Contains(campaign.JournalEntries,
            e => e.Category == JournalCategory.Event && e.Text.Contains("Grimnir"));
    }

    [Fact]
    public void BuryCharacter_UnknownId_Throws()
    {
        var campaign = StartedCampaignWith(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => campaign.BuryCharacter(Guid.NewGuid(), "Nobody"));
    }

    [Fact]
    public void BuryCharacter_CampaignStaysActive()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);

        campaign.BuryCharacter(characterId, "Grimnir");

        Assert.True(campaign.IsActive());
    }

    [Fact]
    public void Graveyard_RoundTripsThroughJson()
    {
        var characterId = Guid.NewGuid();
        var campaign = StartedCampaignWith(characterId);
        campaign.BuryCharacter(characterId, "Grimnir");

        var json = JsonSerializer.Serialize(campaign, _options);
        var restored = JsonSerializer.Deserialize<Campaign>(json, _options);

        Assert.NotNull(restored);
        var fallen = Assert.Single(restored.FallenCharacters);
        Assert.Equal("Grimnir", fallen.Name);
    }

    [Fact]
    public void Campaign_DeserializesFromBlobWithoutFallenField()
    {
        // Backward compat: blobs persisted before the graveyard existed must still load.
        var campaign = Campaign.Create(Difficulty.Grim, "Doom", "The end");
        var json = JsonSerializer.Serialize(campaign, _options);
        using var doc = JsonDocument.Parse(json);
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (prop.Name != "Fallen")
                stripped[prop.Name] = prop.Value;
        var legacyJson = JsonSerializer.Serialize(stripped);

        var restored = JsonSerializer.Deserialize<Campaign>(legacyJson, _options);

        Assert.NotNull(restored);
        Assert.Empty(restored.FallenCharacters);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignGraveyardTests"`
Expected: compile error — `BuryCharacter` / `FallenCharacters` / `FallenCharacter` not defined.

- [ ] **Step 3: Implement**

Create `WrtechedWhispers/WretchedWhispers.Core/Campaigns/FallenCharacter.cs`:

```csharp
namespace WretchedWhispers.Core.Campaigns;

/// <summary>A dead wretch remembered in the campaign's graveyard. The dead stay dead.</summary>
public sealed record FallenCharacter(Guid Id, string Name, int DayDied);
```

Modify `Campaign.cs`:

1. `[JsonConstructor]` signature — append parameter `List<FallenCharacter>? fallen = null` after `string? currentLocationName = null`, and in the body add `Fallen = fallen ?? [];`
2. After the `CurrentLocationName` property, add:

```csharp
    [JsonInclude] internal List<FallenCharacter> Fallen { get; }

    [JsonIgnore] public IReadOnlyList<FallenCharacter> FallenCharacters => Fallen.AsReadOnly();
```

3. After `SetPartyLocation`, add:

```csharp
    public void BuryCharacter(Guid characterId, string name)
    {
        if (!Characters.Remove(characterId))
            throw new ArgumentException("Character is not part of this campaign.", nameof(characterId));
        Fallen.Add(new FallenCharacter(characterId, name, CurrentDay));
        RecordJournalEntry(JournalCategory.Event, $"Here fell {name}.");
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignGraveyardTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Campaigns/FallenCharacter.cs WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs WrtechedWhispers/WretchedWhispers.Tests/Campaigns/CampaignGraveyardTests.cs
rtk git commit -m "feat(campaign): graveyard list and BuryCharacter"
```

---

### Task 2: Repository — newest-first chronicle ordering

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteChatHistoryRepository.cs:36-42` (`GetSessionsForCampaign`)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Persistence/ChatHistoryRoundTripTests.cs`

**Interfaces:**
- Produces: `GetSessionsForCampaign` returns chat-session ids **newest-first by `StartedAt`**. Every existing `sessions.FirstOrDefault()` call site (`TurnCoordinator.cs:83`, `SessionEndpoints.GetSessionDetail`, `SessionEndpoints.GetSessionMessages`) now picks the active chronicle with no code change. Task 5's successor endpoint relies on this.

- [ ] **Step 1: Write the failing test**

Add to `ChatHistoryRoundTripTests.cs` (uses the class's existing `_repo` and `_db`; `StartedAt` is set from `DateTime.UtcNow` inside `CreateSession`, so force distinct timestamps by updating the entity directly):

```csharp
    [Fact]
    public async Task GetSessionsForCampaign_ReturnsNewestFirst()
    {
        var campaignId = Guid.NewGuid();
        var older = await _repo.CreateSession(campaignId);
        var newer = await _repo.CreateSession(campaignId);

        // Make ordering unambiguous regardless of clock resolution.
        var olderEntity = _db.Db.ChatSessions.Single(s => s.Id == older);
        olderEntity.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        await _db.Db.SaveChangesAsync();

        var sessions = await _repo.GetSessionsForCampaign(campaignId);

        Assert.Equal(new[] { newer, older }, sessions);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GetSessionsForCampaign_ReturnsNewestFirst"`
Expected: FAIL (insertion order returns `older` first) — or flaky-pass if timestamps differ; the entity backdate makes it deterministic: FAIL.

- [ ] **Step 3: Implement**

In `SqliteChatHistoryRepository.GetSessionsForCampaign`, add ordering:

```csharp
    public async Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default)
    {
        // Newest-first: the head of the list is the ACTIVE chronicle (one chat session per wretch).
        return await _db.ChatSessions
            .Where(s => s.CampaignId == campaignId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }
```

- [ ] **Step 4: Run persistence + coordinator tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatHistoryRoundTrip|FullyQualifiedName~TurnCoordinator|FullyQualifiedName~SessionEndpoint"`
Expected: all pass (single-session campaigns are unaffected by ordering).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteChatHistoryRepository.cs WrtechedWhispers/WretchedWhispers.Tests/Persistence/ChatHistoryRoundTripTests.cs
rtk git commit -m "feat(chronicles): GetSessionsForCampaign returns newest-first"
```

---

### Task 3: Derived status `"fallen"`

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs` (add `DeriveStatus()`)
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/StateUpdateMapper.cs:76`
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs:337-344` (`DeriveStatus` helper)
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/TurnCoordinator.cs:103-109` (refusal message)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs`

**Interfaces:**
- Consumes: `DeriveStage()`, `Character.IsDead`, `Campaign.WorldEnded`, `Campaign.IsEnded`.
- Produces: `context.DeriveStatus()` → `"fallen"` when stage is `Ended` because the character died while `!WorldEnded && !IsEnded`; otherwise `StatusFor(stage)`. The static `StatusFor(SessionStage)` keeps its stage-only mapping. Task 6 (frontend) keys the death panel on the literal string `"fallen"`.

- [ ] **Step 1: Write the failing tests**

In `StageDerivationTests.cs`:

1. **Update** the existing regression test `Dead_character_maps_to_ended_status_even_while_campaign_is_active` (lines 191-212): its intent — a dead character must never read "in-progress" — is preserved, but the expected status is now `"fallen"`. Replace the final assertion block:

```csharp
        Assert.True(character.IsDead);
        Assert.True(campaign.IsActive()); // the trap the old campaign-only status logic fell into
        Assert.Equal("fallen", ctx.DeriveStatus());
```

and rename the method to `Dead_character_maps_to_fallen_status_even_while_campaign_is_active`.

2. **Add** three new tests (same file, using the class's existing `CreateTestCharacter` / `CreateTestCampaign` helpers and the lethal-defend idiom from `Character_dead_returns_Ended`):

```csharp
    [Fact]
    public void DeriveStatus_DeadCharacter_WorldAlsoEnded_IsEnded_NotFallen()
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(1);
        var character = CreateTestCharacter(Dice, maxHp: 1);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();
        character.Defend(DiceExpr.D6, Dice); // lethal
        for (var day = 0; day < 100 && !campaign.WorldEnded; day++)
            campaign.AdvanceTime(24, Dice); // dawn rolls of 1 trigger a misery each day

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.True(campaign.WorldEnded);
        Assert.Equal("ended", ctx.DeriveStatus());
    }

    [Fact]
    public void DeriveStatus_AbandonedCampaign_IsEnded_NotFallen()
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(1);
        var character = CreateTestCharacter(Dice, maxHp: 1);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();
        character.Defend(DiceExpr.D6, Dice); // lethal
        campaign.End();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.Equal("ended", ctx.DeriveStatus());
    }

    [Fact]
    public void DeriveStatus_BuriedCharacter_ActiveCampaign_IsCharacterCreation()
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(1);
        var character = CreateTestCharacter(Dice, maxHp: 1);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();
        character.Defend(DiceExpr.D6, Dice); // lethal
        campaign.BuryCharacter(character.Id, character.Name);

        // Post-burial the loader finds no player, so the context has no character.
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.Campaign = campaign;

        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
        Assert.Equal("character-creation", ctx.DeriveStatus());
    }
```

Note: `CreateTestCampaign` in this file may hardcode a dawn dice; if `AdvanceTime(24, ...)` with all-1 rolls does not accumulate miseries (check `CalendarOfNechrubel.DawnRoll` semantics while implementing), replace the world-ended arrangement with whatever existing world-ended test `World_ended_returns_Ended` (line 214) uses — reuse its arrangement verbatim.

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StageDerivationTests"`
Expected: compile error — `DeriveStatus` not defined.

- [ ] **Step 3: Implement**

In `SessionContext.cs`, below `StatusFor`:

```csharp
    // "fallen" is the one status that is not a pure function of the stage: the stage is Ended, but the
    // death is recoverable — the player may bury the wretch and roll a new one. World-ended and an
    // explicitly ended campaign remain terminal.
    public string DeriveStatus()
    {
        var stage = DeriveStage();
        if (stage == SessionStage.Ended
            && Character is { IsDead: true }
            && Campaign is { WorldEnded: false, IsEnded: false })
            return "fallen";
        return StatusFor(stage);
    }
```

In `StateUpdateMapper.cs` line 76, replace `var status = SessionContext.StatusFor(derivedStage);` with:

```csharp
        var status = context.DeriveStatus();
```

In `SessionEndpoints.cs` `DeriveStatus` helper (line 343), replace `return SessionContext.StatusFor(context.DeriveStage());` with:

```csharp
        return context.DeriveStatus();
```

In `TurnCoordinator.cs` lines 103-109, differentiate the refusal:

```csharp
        if (stage == SessionStage.Ended)
        {
            logger.LogInformation("Turn refused — session already ended. Session={SessionId}", sessionId);
            writer.TryWrite(StateUpdateMapper.Map(context));
            writer.TryWrite(new TurnError(context.DeriveStatus() == "fallen"
                ? "The wretch has fallen. Roll a new one or abandon this world."
                : "This story has ended. Begin a new character to continue."));
            return;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~StageDerivationTests|FullyQualifiedName~StateUpdateMapper|FullyQualifiedName~TurnCoordinator"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs WrtechedWhispers/WretchedWhispers.Engine/Services/StateUpdateMapper.cs WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs WrtechedWhispers/WretchedWhispers.Engine/Services/TurnCoordinator.cs WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs
rtk git commit -m "feat(status): derived 'fallen' status for recoverable death"
```

---

### Task 4: Epitaph seeding — `ChatHistoryReducer.SeedEpitaphAsync`

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/ChatHistoryReducer.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Services/ChatHistoryReducerTests.cs`

**Interfaces:**
- Consumes: existing `IChatClient`, `IChatHistoryRepository` (`LoadSession`, `GetSummary`, `SaveSummary`), `SummaryMessage` helper.
- Produces: `Task<bool> SeedEpitaphAsync(Guid fallenChronicleId, Guid newChronicleId, CancellationToken ct)` — summarizes the fallen chronicle (including its stored rolling summary if any) in past tense and stores it as the NEW chronicle's `ChatSummary(text, coveredCount: 0)`. Returns false (never throws) on empty history, blank summary, or any exception. Task 5's endpoint calls this best-effort.

- [ ] **Step 1: Write the failing tests**

Add to `ChatHistoryReducerTests.cs` (reuses the class's `_chatClient`, `_repo`, `CreateReducer`, `Messages`, `SetupSummarizerResponse` helpers):

```csharp
    [Fact]
    public async Task SeedEpitaph_SummarizesFallenChronicle_SeedsNewChronicleAtZero()
    {
        var fallenId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(30));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        SetupSummarizerResponse("Grimnir died screaming beneath Galgenbeck.");

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, newId, CancellationToken.None);

        Assert.True(seeded);
        _repo.Verify(r => r.SaveSummary(
            newId,
            It.Is<ChatSummary>(s => s.Text.Contains("Grimnir") && s.CoveredCount == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedEpitaph_IncludesStoredRollingSummary_SkipsCoveredMessages()
    {
        var fallenId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(120));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSummary("earlier doom", 50));
        IEnumerable<ChatMessage>? sent = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => sent = m)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "the tale, finished")));

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, newId, CancellationToken.None);

        Assert.True(seeded);
        Assert.NotNull(sent);
        var sentList = sent.ToList();
        // stored summary + 70 uncovered messages, not the covered 50 again
        Assert.Equal(71, sentList.Count);
        Assert.Contains("earlier doom", sentList[0].Text);
    }

    [Fact]
    public async Task SeedEpitaph_EmptyChronicle_ReturnsFalse_NoModelCall()
    {
        var fallenId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(seeded);
        _chatClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SeedEpitaph_SummarizerThrows_ReturnsFalse()
    {
        var fallenId = Guid.NewGuid();
        _repo.Setup(r => r.LoadSession(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Messages(10));
        _repo.Setup(r => r.GetSummary(fallenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSummary?)null);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model unavailable"));

        var seeded = await CreateReducer().SeedEpitaphAsync(fallenId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(seeded);
        _repo.Verify(r => r.SaveSummary(It.IsAny<Guid>(), It.IsAny<ChatSummary>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SeedEpitaph"`
Expected: compile error — `SeedEpitaphAsync` not defined.

- [ ] **Step 3: Implement**

In `ChatHistoryReducer.cs`, add below `SummarizationInstructions`:

```csharp
    private const string EpitaphInstructions =
        """
        A MORK BORG player character has died. Summarize their chronicle as a finished tale, in
        PAST TENSE and THIRD PERSON — the wretch is dead and stays dead. A new doomed soul will
        walk the same dying world; this summary is the history they inherit. Preserve:
        - The fallen wretch's name, their deeds, and how they died
        - NPCs met, their relationships and current dispositions
        - Locations visited and the state they were left in
        - Unresolved hooks, promises, quests, and threats still loose in the world
        - World events: miseries suffered, omens, the grinding approach of the end
        Do NOT carry over the dead character's hit points, inventory, or any second-person "you"
        phrasing. Write terse, doom-laden narrative prose.
        """;
```

And add the method after `ReduceAsync`:

```csharp
    /// <summary>
    /// Compacts a dead wretch's chronicle into a past-tense epitaph and seeds it as the successor
    /// chronicle's summary (coveredCount 0 — it precedes all of the new session's messages, which
    /// Compose then injects every turn). Best-effort: burial must never fail on a summarizer hiccup,
    /// because the world's hard state lives on the campaign and is re-injected every turn regardless.
    /// </summary>
    public async Task<bool> SeedEpitaphAsync(Guid fallenChronicleId, Guid newChronicleId, CancellationToken ct)
    {
        try
        {
            var history = await chatHistoryRepository.LoadSession(fallenChronicleId, ct);
            if (history is null || history.Count == 0)
                return false;

            var stored = await chatHistoryRepository.GetSummary(fallenChronicleId, ct);
            var toSummarize = new List<ChatMessage>();
            if (stored is not null)
                toSummarize.Add(SummaryMessage(stored.Text));
            toSummarize.AddRange(history.Skip(stored?.CoveredCount ?? 0));

            var options = new ChatOptions { Instructions = EpitaphInstructions };
            var response = await chatClient.GetResponseAsync(toSummarize, options, ct);
            if (string.IsNullOrWhiteSpace(response.Text))
            {
                logger.LogWarning("Epitaph summarization returned empty for chronicle {ChronicleId}", fallenChronicleId);
                return false;
            }

            await chatHistoryRepository.SaveSummary(newChronicleId, new ChatSummary(response.Text, 0), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Epitaph summarization failed for chronicle {ChronicleId}", fallenChronicleId);
            return false;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChatHistoryReducer"`
Expected: all pass (existing + 4 new).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Services/ChatHistoryReducer.cs WrtechedWhispers/WretchedWhispers.Tests/Services/ChatHistoryReducerTests.cs
rtk git commit -m "feat(chronicles): epitaph summary seeds the successor chronicle"
```

---

### Task 5: API — successor + abandon endpoints, journal `fallen` array

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Sessions/SessionEndpointTests.cs`

**Interfaces:**
- Consumes: `campaign.BuryCharacter(Guid, string)` (Task 1), newest-first `GetSessionsForCampaign` (Task 2), `chatHistoryReducer.SeedEpitaphAsync(Guid, Guid, CancellationToken)` (Task 4 — `ChatHistoryReducer` is registered scoped in `AddGameAgent`, injectable directly).
- Produces: `POST /sessions/{id}/successor` → 200 `{ status: "character-creation" }`, 404 not-owner, 409 `{ error }` when character alive/missing or world/campaign ended. `POST /sessions/{id}/abandon` → 200 `{ status: "ended" }`, 404 not-owner, 409 when not active (already ended or never started). `GET /sessions/{id}/journal` response becomes `{ entries, fallen: [{ name, dayDied }] }`.

- [ ] **Step 1: Write the failing tests**

Add to `SessionEndpointTests.cs`, following the file's existing register/login + scoped-DI seeding pattern (copy the exact auth-token and seeding helpers already used by the journal ownership test in this file — reuse, don't reinvent). Test set:

```csharp
    [Fact]
    public async Task Successor_WithLivingCharacter_ReturnsConflict()
    {
        // Seed: campaign owned by user A with one LIVING character (existing seeding helper).
        // POST /sessions/{id}/successor with A's token.
        // Assert: 409.
    }

    [Fact]
    public async Task Successor_NotOwner_ReturnsNotFound()
    {
        // Seed campaign for user A; POST successor with user B's token. Assert: 404.
    }

    [Fact]
    public async Task Successor_DeadCharacter_BuriesAndOpensNewChronicle()
    {
        // Seed campaign + character, then inside a DI scope: damage the character to death
        // (character.Defend with a lethal setup, or set HP via the same idiom the seeding helper
        // uses to construct characters — the domain must see IsDead == true), save it.
        // Record the existing chat session count for the campaign.
        // POST successor with owner's token. Assert: 200, body.status == "character-creation".
        // In a fresh DI scope assert: campaign.Players is empty, campaign.FallenCharacters has one
        // entry with the character's name, chat session count increased by one, and
        // GetSessionsForCampaign.First() is the NEW session id.
    }

    [Fact]
    public async Task Abandon_ActiveCampaign_EndsIt()
    {
        // Seed active campaign. POST /sessions/{id}/abandon with owner token.
        // Assert: 200, body.status == "ended"; fresh scope: campaign.IsEnded is true.
    }

    [Fact]
    public async Task Abandon_AlreadyEnded_ReturnsConflict()
    {
        // Seed campaign, End() + save it in a scope. POST abandon. Assert: 409.
    }

    [Fact]
    public async Task Journal_IncludesFallenCharacters()
    {
        // Seed campaign with a buried character (BuryCharacter in a scope + save).
        // GET /sessions/{id}/journal. Assert: fallen array contains { name, dayDied }.
    }
```

Flesh the comments into real code using this file's established helpers — the assertions named in the comments are the required ones. Note the epitaph path: the WebApplicationFactory registers whatever `IChatClient` the test host has; if `SeedEpitaphAsync` cannot reach a model it returns false and the endpoint still succeeds — that IS the graceful-degradation path, so the successor test must not assert a summary exists.

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SessionEndpointTests"`
Expected: new tests FAIL with 404 (routes not mapped).

- [ ] **Step 3: Implement**

In `SessionEndpoints.cs`:

1. Register routes after the journal/map lines (~line 38):

```csharp
        group.MapPost("/{sessionId:guid}/successor", CreateSuccessor);
        group.MapPost("/{sessionId:guid}/abandon", AbandonSession);
```

2. Add handlers (place after `GetSessionJournal`):

```csharp
    private static async Task<IResult> CreateSuccessor(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo,
        ChatHistoryReducer chatHistoryReducer)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        if (campaign.WorldEnded || campaign.IsEnded)
            return Results.Conflict(new { error = "This world has ended. Nothing walks it now." });

        var firstPlayerId = campaign.Players.FirstOrDefault();
        if (firstPlayerId == Guid.Empty)
            return Results.Conflict(new { error = "No character to bury." });

        var character = await charactersRepo.Get(firstPlayerId);
        if (character is null || !character.IsDead)
            return Results.Conflict(new { error = "The wretch still breathes." });

        var chronicles = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id);
        var fallenChronicleId = chronicles.FirstOrDefault();

        campaign.BuryCharacter(character.Id, character.Name);
        await campaignsRepo.SaveCampaign(campaign, userId);

        // The new chronicle becomes the active one (newest-first ordering). Epitaph is best-effort.
        var newChronicleId = await chatHistoryRepo.CreateSession(campaign.Id);
        if (fallenChronicleId != Guid.Empty)
            await chatHistoryReducer.SeedEpitaphAsync(fallenChronicleId, newChronicleId, http.RequestAborted);

        return Results.Ok(new { status = "character-creation" });
    }

    private static async Task<IResult> AbandonSession(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        if (!campaign.IsActive())
            return Results.Conflict(new { error = "This campaign has already ended." });

        campaign.End();
        await campaignsRepo.SaveCampaign(campaign, userId);
        return Results.Ok(new { status = "ended" });
    }
```

3. In `GetSessionJournal`, extend the response (replace `return Results.Ok(new { entries });`):

```csharp
        var fallen = campaign.FallenCharacters
            .Select(f => new { name = f.Name, dayDied = f.DayDied })
            .ToList();

        return Results.Ok(new { entries, fallen });
```

(`ChatHistoryReducer` needs `using WretchedWhispers.Engine.Services;` — already imported at the top of this file.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SessionEndpointTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs WrtechedWhispers/WretchedWhispers.Tests/Sessions/SessionEndpointTests.cs
rtk git commit -m "feat(api): successor and abandon endpoints, fallen in journal"
```

---

### Task 6: Engine prompts + snapshot graveyard block

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs` (`FormatSnapshot`)
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Prompts/StagePrompts.cs` (`CharacterCreation` const)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs` (snapshot test appended there — it already builds contexts; if the repo has a dedicated FormatSnapshot test file, use that instead)

**Interfaces:**
- Consumes: `Campaign.FallenCharacters` (Task 1).
- Produces: snapshot text containing `Fallen wretches` when the graveyard is non-empty — the narrator's authoritative signal that predecessors exist and are unrecoverable.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void FormatSnapshot_ListsFallenWretches()
    {
        var characterId = Guid.NewGuid();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(characterId);
        campaign.Start();
        campaign.BuryCharacter(characterId, "Grimnir");

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.Campaign = campaign;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("Fallen wretches", snapshot);
        Assert.Contains("Grimnir", snapshot);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~FormatSnapshot_ListsFallenWretches"`
Expected: FAIL — snapshot has no graveyard block.

- [ ] **Step 3: Implement**

In `SessionContext.FormatSnapshot`, inside the `if (Campaign is not null)` block, after the map block (after the `Party location` lines):

```csharp
            if (Campaign.FallenCharacters.Count > 0)
            {
                sb.AppendLine("  Fallen wretches (dead, gone, unrecoverable):");
                foreach (var f in Campaign.FallenCharacters)
                    sb.AppendLine($"    - {f.Name}, died day {f.DayDied}");
            }
```

In `StagePrompts.cs`, append to the `CharacterCreation` const (inside the raw string, after the STEP 2 paragraph):

```
        SUCCESSOR OPENINGS — if Game State lists fallen wretches, this is not the world's first
        tale: a predecessor died here and the world ground on without them. Frame the opening as
        another doomed soul stepping into the SAME dying world — the map, journal, and miseries in
        Game State are its living history; reference them. The dead stay dead: never offer the
        fallen wretch as playable, never revive them, and their gear is lost with the corpse.
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName!~Evals"`
Expected: all pass (full non-eval suite — prompt consts are asserted nowhere, snapshot test green).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Services/SessionContext.cs WrtechedWhispers/WretchedWhispers.Engine/Prompts/StagePrompts.cs WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs
rtk git commit -m "feat(engine): graveyard in snapshot, successor framing in creation prompt"
```

---

### Task 7: Frontend — death panel, fallen status, graveyard block

**Files:**
- Modify: `wretched-whispers-web/src/types/api.ts`
- Create: `wretched-whispers-web/src/components/session/DeathPanel.tsx`
- Modify: `wretched-whispers-web/src/app/sessions/play/page.tsx`
- Modify: `wretched-whispers-web/src/components/journal/JournalDrawer.tsx`
- Modify: `wretched-whispers-web/src/components/session/SessionCard.tsx`

**Interfaces:**
- Consumes: status string `"fallen"` (Task 3, via `SessionDetailDto.status` and SSE `state_update`), `POST /sessions/{id}/successor`, `POST /sessions/{id}/abandon`, journal response `fallen: { name, dayDied }[]` (Task 5).
- Produces: `DeathPanel({ sessionId, characterName })` component; `FallenCharacterDto` type.

- [ ] **Step 1: Extend types**

In `types/api.ts`:

1. `SessionPreviewDto.status` union (line 12) gains `"fallen"`:

```typescript
  status: "character-creation" | "in-progress" | "ended" | "fallen";
```

2. Add near `JournalEntryDto`:

```typescript
export interface FallenCharacterDto {
  name: string;
  dayDied: number;
}
```

(If `SessionDetailDto.status` / store status are plain `string`, they need no change.)

- [ ] **Step 2: Create DeathPanel**

`wretched-whispers-web/src/components/session/DeathPanel.tsx`:

```tsx
"use client";

import { useState } from "react";
import { apiFetch } from "@/lib/api";

interface DeathPanelProps {
  sessionId: string;
  characterName: string | null;
}

/** Shown when the session status is "fallen": the wretch is dead, the world is not.
 *  Both actions reload the page — the session loader derives the new state from scratch. */
export default function DeathPanel({ sessionId, characterName }: DeathPanelProps) {
  const [busy, setBusy] = useState<"successor" | "abandon" | null>(null);
  const [error, setError] = useState("");

  async function post(action: "successor" | "abandon") {
    setBusy(action);
    setError("");
    try {
      const res = await apiFetch(`/sessions/${sessionId}/${action}`, { method: "POST" });
      if (!res.ok) {
        throw new Error(`Request failed (${res.status})`);
      }
      window.location.reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "The void refused.");
      setBusy(null);
    }
  }

  return (
    <div className="border border-doom-pink bg-doom-card p-6 text-center">
      <p className="font-display text-doom-pink text-xl tracking-wider mb-1">
        {characterName ?? "The wretch"} has perished
      </p>
      <p className="text-doom-ash text-sm mb-5">
        The world grinds on without them. Another doomed soul may take up the tale — the map,
        the chronicle, and the miseries remain.
      </p>
      {error && <p className="text-doom-pink text-sm mb-3">{error}</p>}
      <div className="flex justify-center gap-4">
        <button
          onClick={() => post("successor")}
          disabled={busy !== null}
          className="border border-doom-yellow text-doom-yellow px-4 py-2 text-sm uppercase tracking-wider hover:bg-doom-yellow/10 disabled:opacity-50"
        >
          {busy === "successor" ? "Digging a grave..." : "Roll a new wretch"}
        </button>
        <button
          onClick={() => post("abandon")}
          disabled={busy !== null}
          className="border border-doom-ash text-doom-ash px-4 py-2 text-sm uppercase tracking-wider hover:bg-doom-ash/10 disabled:opacity-50"
        >
          {busy === "abandon" ? "Turning away..." : "Abandon this world"}
        </button>
      </div>
    </div>
  );
}
```

(Match `Button` component usage instead of raw `<button>` if the existing `Button` variants fit — check `components/ui/Button.tsx` while implementing; raw buttons above are the fallback.)

- [ ] **Step 3: Wire the play page**

In `app/sessions/play/page.tsx`:

1. Add `const showDeathPanel = status === "fallen" && !isStreaming;` next to `showEndCard` (line 59).
2. Where `<ChatInput ... />` renders (line 243), replace with:

```tsx
      {showDeathPanel ? (
        <DeathPanel sessionId={id} characterName={characterData?.name ?? null} />
      ) : (
        <ChatInput onSend={handleSend} disabled={isStreaming} status={status} />
      )}
```

3. Import `DeathPanel`. If `characterData` has no `name` field in the store, pass `null` (the panel copes) — do NOT add store plumbing for the name alone; check `CharacterData` in `stores/sessionStore.ts` first, it likely already carries `name`.
4. Verify `showEndCard` (status `"ended"`) still renders its end card — `"fallen"` must NOT trigger it (strict equality already guarantees this).

- [ ] **Step 4: Journal drawer graveyard block**

In `JournalDrawer.tsx`:

1. Extend the fetch state and response handling:

```tsx
  const [fallen, setFallen] = useState<FallenCharacterDto[]>([]);
```

and in the fetch `.then`:

```tsx
        .then((data) => {
          setEntries(data.entries);
          setFallen(data.fallen ?? []);
        })
```

2. Render a GRAVEYARD block after the MISERIES block (bone/ash styling, matching the drawer's section idiom):

```tsx
        {fallen.length > 0 && (
          <div className="border border-doom-ash/40 p-3 mb-4">
            <p className="font-display text-doom-bone text-sm tracking-wider mb-2">GRAVEYARD</p>
            {fallen.map((f, i) => (
              <p key={i} className="text-doom-ash text-sm">
                &#9760; {f.name} — died day {f.dayDied}
              </p>
            ))}
          </div>
        )}
```

3. Import `FallenCharacterDto` from `@/types/api`.

- [ ] **Step 5: Session card fallen chip**

In `SessionCard.tsx`, the two `Record<SessionPreviewDto["status"], string>` maps now fail to compile until `"fallen"` is added — add entries:

```tsx
// statusStyles
  fallen: "border-doom-pink text-doom-pink",
// statusLabels
  fallen: "☠ Fallen",
```

Distinguish the ended label so the two aren't identical: change `ended` label to `"Ended"` (keep the skull on `fallen` only) — or keep both skulls if the diff is smaller; the requirement is only that `fallen` reads distinctly from `ended`.

- [ ] **Step 6: Typecheck**

Run: `cd wretched-whispers-web && rtk npx tsc --noEmit`
Expected: no errors (the exhaustive `Record` types prove every status is handled).

- [ ] **Step 7: Commit**

```bash
rtk git add wretched-whispers-web/src
rtk git commit -m "feat(web): death panel, fallen status, graveyard block"
```

---

### Task 8: Full verification

**Files:** none new.

- [ ] **Step 1: Full build**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Expected: 0 errors.

- [ ] **Step 2: Full test suite INCLUDING live evals**

The `CharacterCreation` stage prompt changed, so the eval gate applies.

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all pass, including DomainAuthority/CampaignCreation evals. If an eval fails, read its transcript output before touching anything — prompt regressions here mean the successor paragraph confused the opening flow, and the fix is wording, not code.

- [ ] **Step 3: Frontend typecheck (repeat, post-merge of all tasks)**

Run: `cd wretched-whispers-web && rtk npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 4: Manual playtest checklist (user-assisted)**

1. Kill a character (or seed a dead one): play page shows the death panel, chat input gone, session card shows "Fallen".
2. "Roll a new wretch": page reloads into splash → narrator opens with successor framing referencing the world's history; journal shows GRAVEYARD; map/miseries unchanged.
3. Create the new character; verify play continues in Exploration and old transcript is gone from the UI.
4. On a second fallen character (or fresh seed), "Abandon this world": status flips to ended; turns refused with the ended message.

- [ ] **Step 5: Commit any fixups, then hand off for PR**

```bash
rtk git status
```

Branch/PR flow happens per the repo's usual finishing routine (feature branch `feat/meat-grinder-loop`, push, compare-URL PR — gh CLI is not installed).

---

## Self-review notes

- **Spec coverage:** graveyard+BuryCharacter (T1), newest-first chronicles (T2), fallen status + refusal copy (T3), epitaph seeding incl. degradation (T4), successor/abandon/journal-fallen endpoints (T5), snapshot graveyard + successor prompt (T6), death panel/graveyard UI/fallen chip (T7), eval gate (T8). Deferred items (looting, memorial, past-chronicles viewer, successor eval) are out of scope per spec.
- **Type consistency:** `FallenCharacter(Guid Id, string Name, int DayDied)` / `FallenCharacters` / `BuryCharacter(Guid, string)` / `SeedEpitaphAsync(Guid, Guid, CancellationToken)` / status literal `"fallen"` used identically across tasks.
- **Known judgment points for implementers:** Task 3's world-ended arrangement may need the existing `World_ended_returns_Ended` arrangement instead of the dawn-roll loop; Task 5's tests reuse this file's seeding helpers rather than the sketched comments; Task 7 checks whether `CharacterData` already carries `name` and whether `Button` variants fit.
