# Difficulty Levels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a player pick a difficulty (Story Mode / Grim / Doomed / Hardcore) when creating a session; the choice bundles starting-HP bonus, challenge consequence dice, doom pace (dawn die), and GM tone.

**Architecture:** A pure preset table in Core maps a `Difficulty` enum to a `DifficultySettings` record. Difficulty is stored only on the `Campaign` aggregate (serialized in the existing JSON blob — no EF migration). HP bonus is applied at character creation; consequence dice are threaded into challenge resolution; the dawn die and GM-tone line derive from the preset. The model no longer sets the dawn die.

**Tech Stack:** .NET 10 / C# (xUnit + Moq), EF Core + SQLite (JSON-blob persistence), Microsoft.Extensions.AI tools; Next.js 16 / React 19 / Tailwind v4 frontend.

## Global Constraints

- Prefix every shell command with `rtk` (token proxy); applies inside `&&` chains too.
- NEVER use the null-forgiving operator `!` on non-nullable values — validate or use `?? default` instead.
- Build: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`. Test: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`. The `WrtechedWhispers` directory typo is intentional.
- Commit trailers on every commit:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s`
- Sealed domain entities, `[JsonConstructor]` + `[JsonInclude]`, static factory methods, primary-constructor services.
- Default difficulty everywhere is `Difficulty.Grim`. Grim's numbers equal current `main` behavior (Serious d4 / Deadly d6, dawn d6, no HP bonus).
- Preset numbers (verbatim):

  | Level | HP bonus | Minor | Serious | Deadly | Dawn |
  |---|---|---|---|---|---|
  | StoryMode | 8 | d2 | d2 | d4 | d8 |
  | Grim | 0 | d2 | d4 | d6 | d6 |
  | Doomed | 0 | d2 | d6 | d10 | d6 |
  | Hardcore | 0 | d4 | d8 | d12 | d4 |

- Branch: `feat/difficulty-levels` (already created off `main`).

---

## File Structure

**Create (Core):**
- `WretchedWhispers.Core/Campaigns/Difficulty.cs` — the enum.
- `WretchedWhispers.Core/Campaigns/DifficultySettings.cs` — the settings record.
- `WretchedWhispers.Core/Campaigns/DifficultyPresets.cs` — the preset table.

**Create (tests):**
- `WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs`

**Create (API):**
- `WretchedWhispers.Api/Models/CreateSessionRequest.cs`

**Create (frontend):**
- `wretched-whispers-web/src/components/session/DifficultyPicker.tsx`

**Modify:** `Campaign.cs`, `CampaignService.cs`, `Character.cs`, `CharacterService.cs`, `CharacterCreationService.cs`, `CharacterTools.cs`, `CampaignTools.cs`, `PromptComposer.cs`, `StagePrompts.cs`, `SessionEndpoints.cs`, `SessionPreviewDto.cs`, `SessionDetailDto.cs`; frontend `types/api.ts`, `sessions/page.tsx`, `SessionCard.tsx`; and the tests that construct campaigns/challenges.

---

## Task 1: Core difficulty types + presets

**Files:**
- Create: `WretchedWhispers.Core/Campaigns/Difficulty.cs`
- Create: `WretchedWhispers.Core/Campaigns/DifficultySettings.cs`
- Create: `WretchedWhispers.Core/Campaigns/DifficultyPresets.cs`
- Test: `WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs`

**Interfaces:**
- Produces:
  - `enum Difficulty { StoryMode, Grim, Doomed, Hardcore }` (string-serialized)
  - `record DifficultySettings(int StartingHpBonus, DiceExpr MinorDamage, DiceExpr SeriousDamage, DiceExpr DeadlyDamage, DiceExpr DawnDice, string GmToneNote)`
  - `DifficultyPresets.For(Difficulty) → DifficultySettings`

- [ ] **Step 1: Write the failing test**

Create `WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs`:

```csharp
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class DifficultyPresetsTests
{
    [Theory]
    [InlineData(Difficulty.StoryMode, 8, 2, 2, 4, 8)]
    [InlineData(Difficulty.Grim, 0, 2, 4, 6, 6)]
    [InlineData(Difficulty.Doomed, 0, 2, 6, 10, 6)]
    [InlineData(Difficulty.Hardcore, 0, 4, 8, 12, 4)]
    public void For_returns_expected_settings(
        Difficulty level, int hpBonus, int minor, int serious, int deadly, int dawn)
    {
        var s = DifficultyPresets.For(level);

        Assert.Equal(hpBonus, s.StartingHpBonus);
        Assert.Equal(DiceExpr.D(1, minor), s.MinorDamage);
        Assert.Equal(DiceExpr.D(1, serious), s.SeriousDamage);
        Assert.Equal(DiceExpr.D(1, deadly), s.DeadlyDamage);
        Assert.Equal(DiceExpr.D(1, dawn), s.DawnDice);
        Assert.False(string.IsNullOrWhiteSpace(s.GmToneNote));
    }

    [Fact]
    public void Grim_matches_current_main_balance()
    {
        var s = DifficultyPresets.For(Difficulty.Grim);
        Assert.Equal(0, s.StartingHpBonus);
        Assert.Equal(DiceExpr.D(1, 4), s.SeriousDamage);
        Assert.Equal(DiceExpr.D(1, 6), s.DeadlyDamage);
    }
}
```

Note: this assumes `DiceExpr` has value equality. If `Assert.Equal(DiceExpr.D(1,2), ...)` fails on reference equality, compare a rolled/tostring form instead — check `DiceExpr` (it is used with `==` in tests elsewhere, so value equality is expected).

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~DifficultyPresetsTests"`
Expected: FAIL — `Difficulty`/`DifficultyPresets` do not exist (compile error).

- [ ] **Step 3: Create the three Core types**

`WretchedWhispers.Core/Campaigns/Difficulty.cs`:

```csharp
using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>How punishing a session is. Chosen at creation, stored on the Campaign. String-serialized
/// so it round-trips readably in the campaign JSON blob and over the HTTP API.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Difficulty>))]
public enum Difficulty
{
    StoryMode,
    Grim,
    Doomed,
    Hardcore
}
```

`WretchedWhispers.Core/Campaigns/DifficultySettings.cs`:

```csharp
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>The concrete knobs a difficulty level resolves to. Pure value object.</summary>
public sealed record DifficultySettings(
    int StartingHpBonus,
    DiceExpr MinorDamage,
    DiceExpr SeriousDamage,
    DiceExpr DeadlyDamage,
    DiceExpr DawnDice,
    string GmToneNote);
```

`WretchedWhispers.Core/Campaigns/DifficultyPresets.cs`:

```csharp
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>Maps each difficulty level to its settings. The single source of the difficulty numbers.</summary>
public static class DifficultyPresets
{
    public static DifficultySettings For(Difficulty level) => level switch
    {
        Difficulty.StoryMode => new DifficultySettings(
            StartingHpBonus: 8,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 2),
            DeadlyDamage: DiceExpr.D(1, 4),
            DawnDice: DiceExpr.D(1, 8),
            GmToneNote: "Difficulty: STORY MODE. Be forgiving — favor tension over death. Prefer None or Minor consequences; reserve Deadly for reckless, self-destructive acts."),
        Difficulty.Grim => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 4),
            DeadlyDamage: DiceExpr.D(1, 6),
            DawnDice: DiceExpr.D(1, 6),
            GmToneNote: "Difficulty: GRIM. Measured danger. Default to None or Minor; use Serious only for genuine peril; reserve Deadly for explicit death-traps."),
        Difficulty.Doomed => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 6),
            DeadlyDamage: DiceExpr.D(1, 10),
            DawnDice: DiceExpr.D(1, 6),
            GmToneNote: "Difficulty: DOOMED. True MORK BORG — unfair and grim. Let Serious and Deadly consequences fall as the fiction demands."),
        Difficulty.Hardcore => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 4),
            SeriousDamage: DiceExpr.D(1, 8),
            DeadlyDamage: DiceExpr.D(1, 12),
            DawnDice: DiceExpr.D(1, 4),
            GmToneNote: "Difficulty: HARDCORE. Merciless — the world wants them dead. Reach readily for Serious and Deadly consequences."),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~DifficultyPresetsTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Campaigns/Difficulty.cs WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultySettings.cs WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultyPresets.cs WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs
rtk git commit -m "feat(core): difficulty enum, settings, and preset table

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 2: Campaign stores Difficulty; dawn die derives from it; remove dawn die from model control

This is a signature-migration task: `Campaign.Create` and `Campaign.Configure` change shape, so every caller must be updated in the same task to keep the build green.

**Files:**
- Modify: `WretchedWhispers.Core/Campaigns/Campaign.cs`
- Modify: `WretchedWhispers.Core/Campaigns/CampaignService.cs`
- Modify: `WretchedWhispers.Api/GameTools/CampaignTools.cs`
- Modify: `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` (create-session call site → `Difficulty.Grim` for now; Task 6 wires the request)
- Modify: `WretchedWhispers.Api/Prompts/StagePrompts.cs` (drop dawn-pace instructions)
- Modify (tests): every `Campaign.Create(...)` / `.Configure(...)` call site.
- Test: `WretchedWhispers.Tests/Campaigns/CampaignDifficultyTests.cs` (new)

**Interfaces:**
- Consumes: `Difficulty`, `DifficultyPresets.For` (Task 1).
- Produces:
  - `Campaign.Difficulty` (get)
  - `Campaign.Create(Difficulty difficulty, string name, string description)`
  - `Campaign.Configure(string name, string description)` (dawnDice dropped)
  - `CampaignService.ConfigureCampaign(Guid campaignId, string name, string description)`

- [ ] **Step 1: Write the failing test**

Create `WretchedWhispers.Tests/Campaigns/CampaignDifficultyTests.cs`:

```csharp
using System.Text.Json;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class CampaignDifficultyTests
{
    private static readonly JsonSerializerOptions Options = AggregateJsonOptions.Create();

    [Fact]
    public void Create_stores_difficulty_and_derives_dawn_die()
    {
        var campaign = Campaign.Create(Difficulty.Hardcore, "Doom", "A test");

        Assert.Equal(Difficulty.Hardcore, campaign.Difficulty);
        // Round-trips through JSON; dawn die is internal, so assert via serialization.
        var json = JsonSerializer.Serialize(campaign, Options);
        Assert.Contains("\"Hardcore\"", json);
    }

    [Fact]
    public void Configure_preserves_difficulty_dawn_die()
    {
        var campaign = Campaign.Create(Difficulty.StoryMode, "Old", "Old desc");
        campaign.Configure("New name", "New desc");

        Assert.Equal(Difficulty.StoryMode, campaign.Difficulty);
        Assert.Equal("New name", campaign.Name);
    }

    [Fact]
    public void Deserializing_a_blob_without_difficulty_defaults_to_grim()
    {
        // Simulate a pre-feature persisted campaign: serialize, then strip the Difficulty property.
        var campaign = Campaign.Create(Difficulty.Doomed, "Legacy", "desc");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(campaign, Options));
        Assert.NotNull(node); // xUnit NotNull narrows nullability — avoids the null-forgiving operator
        node.AsObject().Remove("Difficulty"); // key may be camelCased ("difficulty") depending on options — check the serialized output

        var restored = JsonSerializer.Deserialize<Campaign>(node.ToJsonString(), Options);
        Assert.NotNull(restored);
        Assert.Equal(Difficulty.Grim, restored.Difficulty);
    }
}
```

Note: `AggregateJsonOptions` lives in `WretchedWhispers.Infrastructure.Persistence.Serialization` (see `ServiceCollectionExtensions`). If the property name in JSON is camelCased, adjust the `Contains`/`Remove` key accordingly — check the serialized output.

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CampaignDifficultyTests"`
Expected: FAIL — `Campaign.Create(Difficulty, …)` and `Campaign.Difficulty` do not exist.

- [ ] **Step 3: Modify `Campaign.cs`**

In `WretchedWhispers.Core/Campaigns/Campaign.cs`:

Add `Difficulty difficulty = Difficulty.Grim` as the LAST parameter of the `[JsonConstructor]` private ctor, and assign it. The default makes pre-feature blobs (missing the property) deserialize as Grim:

```csharp
[JsonConstructor]
private Campaign(Guid id, string name, string description, int currentDay, int currentHour,
    List<Guid> characters, CalendarOfNechrubel calendar,
    DiceExpr dawnDice, List<Guid> encounters,
    bool isStarted = false, bool isEnded = false, bool isConfigured = false,
    List<JournalEntry>? journal = null, Difficulty difficulty = Difficulty.Grim)
{
    // ... existing assignments ...
    Difficulty = difficulty;
}
```

Add the property (near the other `[JsonInclude]` props):

```csharp
[JsonInclude] public Difficulty Difficulty { get; private set; }
```

Replace `Create`:

```csharp
public static Campaign Create(Difficulty difficulty, string name, string description)
{
    var settings = DifficultyPresets.For(difficulty);
    return new Campaign(Guid.NewGuid(), name, description, 1, 0, [], new CalendarOfNechrubel(),
        settings.DawnDice, [], difficulty: difficulty);
}
```

Replace `Configure` (drop `dawnDice`; keep the existing DawnDice set at Create):

```csharp
public void Configure(string name, string description)
{
    if (IsStarted) throw new InvalidOperationException("Cannot configure a campaign that is already started.");

    Name = name;
    Description = description;
    IsConfigured = true;
}
```

- [ ] **Step 4: Modify `CampaignService.cs`**

Replace `CreateCampaign` and `ConfigureCampaign`:

```csharp
public async Task CreateCampaign(Difficulty difficulty, string name, string description)
{
    var campaign = Campaign.Create(difficulty, name, description);
    await campaignsRepository.SaveCampaign(campaign);
}

public async Task<Campaign> ConfigureCampaign(Guid campaignId, string name, string description)
{
    var campaign = await campaignsRepository.Get(campaignId);
    if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

    campaign.Configure(name, description);
    TryAutoStart(campaign);
    await campaignsRepository.SaveCampaign(campaign);
    return campaign;
}
```

- [ ] **Step 5: Modify `CampaignTools.cs` (drop the dawn-die param the model set)**

Replace the `ConfigureCampaign` tool:

```csharp
[Description("Configure the campaign's name and description. The campaign already exists; it begins automatically once it is configured and the character has been created.")]
[GameTool(SessionStage.CharacterCreation, SessionStage.CampaignSetup)]
public async Task<CampaignDto> ConfigureCampaign(
    [Description("The name of the campaign")] string name,
    [Description("A description of the campaign's setting, goals, or theme")] string description)
{
    var campaign = await campaignService.ConfigureCampaign(RequireCampaignId(), name, description);
    return CreateCampaignDto(campaign);
}
```

Remove the now-unused `DiceExpr`/`ToolGuard.DiceExpression` usage if it leaves an unused `using` (build will warn, not fail).

- [ ] **Step 6: Update `StagePrompts.cs` (remove dawn-pace instructions)**

In `CharacterCreation`, replace step 2's bullet:

```
  2. ConfigureCampaign — give the campaign a doom-appropriate name and description.
     The campaign begins automatically once the character exists and the campaign is configured.
```

In `CampaignSetup`, replace the ConfigureCampaign sentence:

```
        A character exists but the campaign has not started yet. Finish the setup seamlessly in this
        turn -- do not interrogate the player with menus. Call ConfigureCampaign with a doom-appropriate
        name and description; the campaign begins automatically. Then narrate the rotting world they wake
        into and end by asking what they do.
```

- [ ] **Step 7: Update `SessionEndpoints.cs` create-session call site (temporary Grim)**

At the `Campaign.Create(...)` call in `CreateSession` (currently `DiceExpr.Parse("d6"), "New Campaign", ...`):

```csharp
var campaign = Campaign.Create(
    Difficulty.Grim,
    "New Campaign",
    "A new journey into doom");
```

Add `using WretchedWhispers.Core.Campaigns;` if not present. (Task 6 replaces `Difficulty.Grim` with the request value.)

- [ ] **Step 8: Build; fix every remaining call site**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Then find all call sites: `rtk grep -rn "Campaign.Create(\|\.Configure(\|ConfigureCampaign(\|CreateCampaign(" --include=*.cs WrtechedWhispers`

For each **`Campaign.Create(<dawnDice>, name, desc)`** → **`Campaign.Create(Difficulty.Grim, name, desc)`** (drop the DiceExpr arg; add `using WretchedWhispers.Core.Campaigns;`). Known sites include `WretchedWhispers.Evals/Harness/EvalHost.cs`, `WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs` (`MakeExplorationContext`, `MakeEndedContext`), `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs` (`CreateActiveCampaign`, `CreateEndedCampaign`), and any Campaign/StageDerivation tests.

For each **`campaign.Configure(<dawnDice>, name, desc)`** → **`campaign.Configure(name, desc)`**.
For each **`campaignService.ConfigureCampaign(id, <dawnDice>, name, desc)`** → **`campaignService.ConfigureCampaign(id, name, desc)`**.

Re-run build until clean: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Expected: `0 errors`.

- [ ] **Step 9: Run the new + affected tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~Campaign|FullyQualifiedName~PromptComposer|FullyQualifiedName~StageDerivation|FullyQualifiedName~TurnCoordinator"`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
rtk git add -A
rtk git commit -m "feat(core): store difficulty on Campaign; dawn die derives from it

Difficulty is chosen at creation and drives the dawn (doom) die; the model no
longer sets dawn pace via ConfigureCampaign. Backward-compatible: campaigns
without a Difficulty field deserialize as Grim.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 3: Challenge consequences use difficulty dice

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs` (`SufferConsequence`)
- Modify: `WretchedWhispers.Core/Characters/CharacterService.cs` (`ChallengePlayer`)
- Modify: `WretchedWhispers.Api/GameTools/CharacterTools.cs` (`ChallengeCharacter`)
- Modify: `WretchedWhispers.Tests/Characters/Challenge/ChallengeConsequenceTests.cs`

**Interfaces:**
- Consumes: `DifficultySettings`, `DifficultyPresets.For`, `Campaign.Difficulty`.
- Produces:
  - `Character.SufferConsequence(ChallengeConsequence consequence, DifficultySettings settings, Dice dice) → int`
  - `CharacterService.ChallengePlayer(Guid, Dr, AbilityKind, DifficultySettings settings, ChallengeConsequence = None)`

- [ ] **Step 1: Update the failing test**

In `WretchedWhispers.Tests/Characters/Challenge/ChallengeConsequenceTests.cs`, change the theory to drive dice from a `DifficultySettings` and pass it through. Replace the `SufferConsequence_RollsSeverityDie_AppliesDamage` test:

```csharp
[Theory]
[InlineData(ChallengeConsequence.Minor, 2)]
[InlineData(ChallengeConsequence.Serious, 4)]
[InlineData(ChallengeConsequence.Deadly, 6)]
public void SufferConsequence_RollsSeverityDie_AppliesDamage(ChallengeConsequence severity, int expectedSides)
{
    var character = TestCharacters.Create(Dice);
    var hpBefore = character.Hp.Current;
    MockRandomService.Invocations.Clear();
    SetupDiceRoll(expectedSides, 0); // severity die -> 1 damage

    // Grim: Minor d2 / Serious d4 / Deadly d6 — matches the InlineData sides.
    var settings = DifficultyPresets.For(Difficulty.Grim);
    var damage = character.SufferConsequence(severity, settings, Dice);

    Assert.Equal(1, damage);
    Assert.Equal(hpBefore - 1, character.Hp.Current);
    MockRandomService.Verify(r => r.GenerateRandomRoll(expectedSides), Times.Once);
}
```

Update the `ChallengePlayer_*` tests to pass a settings arg. For each `service.ChallengePlayer(character.Id, new Dr(12), AbilityKind.X, ChallengeConsequence.Y)` add the settings before the consequence:

```csharp
var result = await service.ChallengePlayer(
    character.Id, new Dr(12), AbilityKind.Agility, DifficultyPresets.For(Difficulty.Grim),
    ChallengeConsequence.Serious);
```

Add `using WretchedWhispers.Core.Campaigns;` to the test file. Note the `d6 consequence -> 4 damage` comment in `ChallengePlayer_FailureWithConsequence_AppliesDamage` is now a d4 for Grim; the mock returns a fixed value regardless of sides, so `Assert.Equal(4, result.DamageTaken)` still holds — leave the assertion, fix the comment to `d4`.

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChallengeConsequenceTests"`
Expected: FAIL — `SufferConsequence`/`ChallengePlayer` signatures don't match.

- [ ] **Step 3: Modify `Character.SufferConsequence`**

Replace the method body's severity mapping to read from settings:

```csharp
public int SufferConsequence(ChallengeConsequence consequence, DifficultySettings settings, Dice dice)
{
    if (consequence is ChallengeConsequence.None)
        return 0;

    var severityDie = consequence switch
    {
        ChallengeConsequence.Minor => settings.MinorDamage,
        ChallengeConsequence.Serious => settings.SeriousDamage,
        ChallengeConsequence.Deadly => settings.DeadlyDamage,
        _ => throw new ArgumentOutOfRangeException(nameof(consequence))
    };

    var damage = dice.Roll(severityDie);
    ReceiveDamage(damage, dice);
    return damage;
}
```

Add `using WretchedWhispers.Core.Campaigns;` to `Character.cs`.

- [ ] **Step 4: Modify `CharacterService.ChallengePlayer`**

```csharp
public async Task<ChallengeResult> ChallengePlayer(
    Guid characterId, Dr dr, AbilityKind ability, DifficultySettings settings,
    ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
{
    var character = await charactersRepository.Get(characterId);
    if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

    var outcome = character.Challenge(dr, ability, dice);

    var damageTaken = 0;
    if (!outcome.IsSuccess && consequenceOnFailure is not ChallengeConsequence.None)
    {
        damageTaken = character.SufferConsequence(consequenceOnFailure, settings, dice);
        await charactersRepository.Save(character);
    }

    return new ChallengeResult(outcome, damageTaken, character.IsDead);
}
```

Add `using WretchedWhispers.Core.Campaigns;`.

- [ ] **Step 5: Modify `CharacterTools.ChallengeCharacter`**

Resolve settings from the campaign's difficulty (fallback Grim, no null-forgiving), and generalize the description (dice now vary by level):

```csharp
[Description("Challenge the character with an ability test against a difficulty rating. On failure, the chosen consequence is applied automatically as rolled damage.")]
[GameTool(SessionStage.Exploration)]
public async Task<ChallengeOutcomeDto> ChallengeCharacter(
    [Description("Level of the challenge, the higher the number the harder. Usually 12 for normal.")]
    int challengeDr,
    [Description("Ability kind to use: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
    AbilityKind abilityKind,
    [Description("What failure costs, chosen like a GM: 'None' (no harm), 'Minor' (scrapes), 'Serious' (a real wound), 'Deadly' (can kill). Follow the difficulty guidance in your instructions when choosing.")]
    ChallengeConsequence consequenceOnFailure = ChallengeConsequence.None)
{
    ToolGuard.InRange(challengeDr, 2, 20, nameof(challengeDr), "12 is a normal challenge");
    var settings = DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim);
    var result = await characterService.ChallengePlayer(
        RequireCharacterId(), new Dr(challengeDr), abilityKind, settings, consequenceOnFailure);
    return new ChallengeOutcomeDto(
        result.Outcome.IsSuccess, result.Outcome.Roll, result.Outcome.Modifier,
        result.Outcome.Roll + result.Outcome.Modifier,
        result.Outcome.EffectiveDr, result.DamageTaken, result.IsDead);
}
```

`CharacterTools` already has `using WretchedWhispers.Core.Campaigns;`.

- [ ] **Step 6: Run tests + build**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln` (expect 0 errors)
Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~ChallengeConsequenceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
rtk git add -A
rtk git commit -m "feat: challenge consequence dice scale with difficulty

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 4: Starting HP bonus by difficulty

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Create/CharacterCreationService.cs`
- Modify: `WretchedWhispers.Api/GameTools/CharacterTools.cs` (`CreateCharacter`)
- Test: `WretchedWhispers.Tests/Characters/Create/CharacterCreationDifficultyTests.cs` (new)

**Interfaces:**
- Consumes: `Difficulty`, `DifficultyPresets.For`, `Campaign.Difficulty`.
- Produces: `CharacterCreationService.Create(string name, Difficulty difficulty) → Task<Character>`

- [ ] **Step 1: Write the failing test**

First check the existing `CharacterCreation` eval/test helpers to reuse the DI/dice setup. Create `WretchedWhispers.Tests/Characters/Create/CharacterCreationDifficultyTests.cs`:

```csharp
using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Create;

public class CharacterCreationDifficultyTests : TestBase
{
    private CharacterCreationService CreateService()
    {
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Save(It.IsAny<Character>())).Returns(Task.CompletedTask);
        return new CharacterCreationService(repo.Object, Dice);
    }

    [Fact]
    public async Task StoryMode_adds_eight_to_rolled_hp()
    {
        // Force every roll to its minimum so base HP is deterministic: Toughness mod + d8.
        // With all 3d6 sums minimal, ability mods are -3; d8 roll = 1 => base = max(1, -3 + 1) = 1.
        SetupDiceRollsAlwaysMin();
        var story = await CreateService().Create("A", Difficulty.StoryMode);

        SetupDiceRollsAlwaysMin();
        var grim = await CreateService().Create("B", Difficulty.Grim);

        Assert.Equal(grim.Hp.Max + 8, story.Hp.Max);
    }
}
```

Note: use whatever deterministic-dice helper `TestBase` provides. If there is no "always min" helper, set up the `MockRandomService` to return 0 for every `GenerateRandomRoll(...)` (inspect `TestBase`/`TestCharacters` for the idiom, e.g. `MockRandomService.Setup(r => r.GenerateRandomRoll(It.IsAny<int>())).Returns(0);`). The assertion only needs the two characters rolled identically, so seeding both with the same fixed rolls is sufficient — adjust the helper name to the real one.

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CharacterCreationDifficultyTests"`
Expected: FAIL — `Create(string, Difficulty)` does not exist.

- [ ] **Step 3: Modify `CharacterCreationService`**

```csharp
public async Task<Character> Create(string name, Difficulty difficulty)
{
    var id = Guid.NewGuid();
    var abilities = RollAbilities();
    var equipment = RollStartingEquipment(abilities);
    var maxHp = RollStartingHealthPoints(abilities) + DifficultyPresets.For(difficulty).StartingHpBonus;

    var character = Character.Create(id, name, maxHp, abilities, equipment, dice);
    await charactersRepository.Save(character);

    return character;
}
```

Add `using WretchedWhispers.Core.Campaigns;`.

- [ ] **Step 4: Modify `CharacterTools.CreateCharacter` to pass the campaign difficulty**

```csharp
var difficulty = sessionContext.Campaign?.Difficulty ?? Difficulty.Grim;
var character = await characterCreationService.Create(name, difficulty);
```

- [ ] **Step 5: Build; fix any other `CharacterCreationService.Create(` callers**

Run: `rtk grep -rn "characterCreationService.Create(\|CharacterCreationService(.*).Create(\|\.Create(name)" --include=*.cs WrtechedWhispers`
Update any remaining `.Create(name)` → `.Create(name, Difficulty.Grim)` (e.g. eval harness). Then:
Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln` (expect 0 errors)

- [ ] **Step 6: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~CharacterCreationDifficultyTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
rtk git add -A
rtk git commit -m "feat: starting HP bonus scales with difficulty

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 5: GM tone note in the prompt

**Files:**
- Modify: `WretchedWhispers.Api/Services/PromptComposer.cs`
- Modify: `WretchedWhispers.Api/Prompts/StagePrompts.cs` (Exploration: keep only frequency/integrity)
- Modify: `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs`

**Interfaces:**
- Consumes: `Campaign.Difficulty`, `DifficultyPresets.For`.

- [ ] **Step 1: Write the failing test**

Add to `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs`:

```csharp
[Fact]
public void Compose_includes_difficulty_tone_note()
{
    var context = BuildContextForStage(SessionStage.Exploration); // sets an active campaign (Grim default)

    var result = _composer.Compose(context);

    Assert.Contains("Difficulty: GRIM", result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~PromptComposerTests.Compose_includes_difficulty_tone_note"`
Expected: FAIL — tone note not present.

- [ ] **Step 3: Modify `PromptComposer.Compose`**

```csharp
using WretchedWhispers.Api.Prompts;
using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Api.Services;

public sealed class PromptComposer
{
    public string Compose(SessionContext context)
    {
        var stage = context.DeriveStage();
        var persona = NarratorPersona.Text;
        var stageInstructions = StagePrompts.For(stage);
        var snapshot = context.FormatSnapshot();
        var toneNote = DifficultyPresets.For(context.Campaign?.Difficulty ?? Difficulty.Grim).GmToneNote;

        return $"""
            {persona}

            ## Current Stage: {stage}
            {stageInstructions}

            ## Difficulty
            {toneNote}

            ## Game State
            {snapshot}
            """;
    }
}
```

- [ ] **Step 4: Trim the Exploration prompt's hard-coded severity leaning**

In `StagePrompts.cs`, replace the `ChallengeCharacter` bullet in `Exploration` so severity guidance defers to the difficulty note (keeps frequency + integrity):

```
        - Call ChallengeCharacter only when a risky action has real, uncertain stakes AND a plausible way to
          fail. Routine or low-stakes actions need no roll — just narrate them. When you do test, use a DR
          (usually 12) and choose consequenceOnFailure to match the stakes AND the Difficulty guidance in
          your instructions. Whenever you roll, never narrate success, failure, or harm without it; weave the
          returned roll, modifier, DR, and damage into the prose.
```

- [ ] **Step 5: Run tests**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~PromptComposerTests"`
Expected: PASS (existing assertions about persona/stage text still hold; the new section is additive).

- [ ] **Step 6: Commit**

```bash
rtk git add -A
rtk git commit -m "feat: per-difficulty GM tone note in the composed prompt

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 6: API — accept difficulty at creation; expose it on session DTOs

**Files:**
- Create: `WretchedWhispers.Api/Models/CreateSessionRequest.cs`
- Modify: `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs`
- Modify: `WretchedWhispers.Api/Models/SessionPreviewDto.cs`
- Modify: `WretchedWhispers.Api/Models/SessionDetailDto.cs`

**Interfaces:**
- Produces: `POST /sessions` accepts `{ "difficulty": "Grim" }`; `SessionPreviewDto.Difficulty`, `SessionDetailDto.Difficulty`.

- [ ] **Step 1: Write the failing test**

Locate the endpoint integration test project/pattern (search `WebApplicationFactory` under `WretchedWhispers.Tests`). If an integration harness exists, add a test that POSTs `{ "difficulty": "Hardcore" }` and asserts the created session's preview reports `Hardcore`. If no HTTP integration harness exists, cover this at the unit level instead: assert `Campaign.Create(Difficulty.Hardcore, …).Difficulty == Difficulty.Hardcore` is surfaced by the preview mapping — i.e. add the mapping and a small mapper test. Prefer the integration test if the harness is present.

Minimal mapper-level test (create `WretchedWhispers.Tests/Api/SessionDtoDifficultyTests.cs` only if there is no HTTP harness):

```csharp
using WretchedWhispers.Core.Campaigns;
using Xunit;

namespace WretchedWhispers.Tests.Api;

public class SessionDtoDifficultyTests
{
    [Fact]
    public void Preview_dto_carries_difficulty()
    {
        var dto = new WretchedWhispers.Api.Models.SessionPreviewDto(
            Guid.NewGuid(), "n", "d", null, null, null, "in-progress", Difficulty.Doomed, null);
        Assert.Equal(Difficulty.Doomed, dto.Difficulty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~SessionDtoDifficultyTests"`
Expected: FAIL — `SessionPreviewDto` has no `Difficulty` param.

- [ ] **Step 3: Add `CreateSessionRequest`**

`WretchedWhispers.Api/Models/CreateSessionRequest.cs`:

```csharp
using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Api.Models;

public record CreateSessionRequest(Difficulty Difficulty = Difficulty.Grim);
```

- [ ] **Step 4: Add `Difficulty` to the DTOs**

`SessionPreviewDto.cs` — add `Difficulty Difficulty` before `LastPlayed`:

```csharp
using WretchedWhispers.Core.Campaigns;

namespace WretchedWhispers.Api.Models;

public record SessionPreviewDto(
    Guid SessionId,
    string CampaignName,
    string Description,
    string? CharacterName,
    int? CurrentHp,
    int? MaxHp,
    string Status,
    Difficulty Difficulty,
    DateTime? LastPlayed
);
```

`SessionDetailDto.cs` — add `Difficulty Difficulty` to the record (place it after `Status`; note the frontend/type ordering in Task 7). Add `using WretchedWhispers.Core.Campaigns;`.

- [ ] **Step 5: Wire the endpoint**

In `SessionEndpoints.cs`:

`CreateSession` — accept the optional request body and use its difficulty:

```csharp
private static async Task<IResult> CreateSession(
    HttpContext http,
    ICampaignsRepository campaignsRepo,
    IChatHistoryRepository chatHistoryRepo,
    CreateSessionRequest? request = null)
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var campaign = Campaign.Create(
        request?.Difficulty ?? Difficulty.Grim,
        "New Campaign",
        "A new journey into doom");

    await campaignsRepo.SaveCampaign(campaign, userId);
    await chatHistoryRepo.CreateSession(campaign.Id);

    return Results.Created(
        $"/sessions/{campaign.Id}",
        new CreateSessionResponse(campaign.Id, campaign.Id));
}
```

`ListSessions` — pass `campaign.Difficulty` into the `SessionPreviewDto` (add the arg in the `previews.Add(new SessionPreviewDto(...))` call, before `lastPlayed`).

`GetSessionDetail` — add `campaign.Difficulty` to the `SessionDetailDto(...)` construction (matching the field position chosen in Step 4).

- [ ] **Step 6: Build + tests**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln` (expect 0 errors)
Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: PASS (full suite; live evals may skip without Azure creds).

- [ ] **Step 7: Commit**

```bash
rtk git add -A
rtk git commit -m "feat(api): accept difficulty on POST /sessions; expose on session DTOs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Task 7: Frontend — difficulty picker

**Files:**
- Modify: `wretched-whispers-web/src/types/api.ts`
- Create: `wretched-whispers-web/src/components/session/DifficultyPicker.tsx`
- Modify: `wretched-whispers-web/src/app/sessions/page.tsx`
- Modify: `wretched-whispers-web/src/components/session/SessionCard.tsx`

**Interfaces:**
- Consumes: `POST /sessions` body `{ difficulty }`; `SessionPreviewDto.difficulty`.

- [ ] **Step 1: Add types**

In `src/types/api.ts`, add the union and the DTO fields:

```typescript
export type Difficulty = "StoryMode" | "Grim" | "Doomed" | "Hardcore";
```

Add `difficulty: Difficulty;` to `SessionPreviewDto` (before `lastPlayed`) and to `SessionDetailDto` (after `status`), matching the C# field order.

- [ ] **Step 2: Create `DifficultyPicker.tsx`**

`src/components/session/DifficultyPicker.tsx`:

```tsx
"use client";

import { useState } from "react";
import type { Difficulty } from "@/types/api";
import Button from "@/components/ui/Button";

const LEVELS: { key: Difficulty; label: string; blurb: string }[] = [
  { key: "StoryMode", label: "Story Mode", blurb: "Experience the world. Death is rare; wounds are shallow." },
  { key: "Grim", label: "Grim", blurb: "Measured danger. Bleak, but survivable if you're careful." },
  { key: "Doomed", label: "Doomed", blurb: "True MORK BORG. Unfair, brutal, and often fatal." },
  { key: "Hardcore", label: "Hardcore", blurb: "The world wants you dead. It usually gets its way." },
];

interface DifficultyPickerProps {
  onConfirm: (difficulty: Difficulty) => void;
  onCancel: () => void;
  busy?: boolean;
}

export default function DifficultyPicker({ onConfirm, onCancel, busy }: DifficultyPickerProps) {
  const [selected, setSelected] = useState<Difficulty>("Grim");

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-[#0a0a0a]/70 px-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Choose difficulty"
        className="w-full max-w-md bg-doom-dark border border-doom-card p-6"
      >
        <h2 className="font-display text-doom-yellow text-xl tracking-wider mb-4">
          CHOOSE YOUR DOOM
        </h2>
        <div className="flex flex-col gap-2 mb-6">
          {LEVELS.map((lvl) => (
            <button
              key={lvl.key}
              onClick={() => setSelected(lvl.key)}
              className={`text-left p-3 border transition-colors ${
                selected === lvl.key
                  ? "border-doom-yellow bg-doom-yellow/10"
                  : "border-doom-card hover:border-doom-yellow/30"
              }`}
            >
              <div className="font-display text-doom-bone text-sm uppercase tracking-wider">
                {lvl.label}
              </div>
              <div className="text-doom-ash text-xs mt-1">{lvl.blurb}</div>
            </button>
          ))}
        </div>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button variant="primary" onClick={() => onConfirm(selected)} loading={busy}>
            Begin
          </Button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Wire the picker into `sessions/page.tsx`**

Add `import DifficultyPicker from "@/components/session/DifficultyPicker";` and `import type { Difficulty } from "@/types/api";`. Add a `pickerOpen` state; change the "New Session" button to open the picker; move the POST into a `confirm` handler that sends the body:

```tsx
const [pickerOpen, setPickerOpen] = useState(false);
```

Replace `handleCreateSession` with a difficulty-aware version:

```tsx
async function handleCreateSession(difficulty: Difficulty) {
  setCreating(true);
  setError("");
  try {
    const res = await apiFetch("/sessions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ difficulty }),
    });
    if (!res.ok) {
      throw new Error(`Failed to create session (${res.status})`);
    }
    const data: CreateSessionResponse = await res.json();
    router.push(`/sessions/${data.sessionId}`);
  } catch (err) {
    setError(err instanceof Error ? err.message : "The abyss refused your offering.");
    setCreating(false);
    setPickerOpen(false);
  }
}
```

Change the button’s `onClick` to `() => setPickerOpen(true)` and remove its `loading={creating}` (the picker shows the busy state). Render the picker near the end of the returned JSX:

```tsx
{pickerOpen && (
  <DifficultyPicker
    onConfirm={handleCreateSession}
    onCancel={() => setPickerOpen(false)}
    busy={creating}
  />
)}
```

- [ ] **Step 4: Show difficulty on `SessionCard`**

In `SessionCard.tsx`, add a small label in the footer row (after the HP span):

```tsx
<span className="uppercase tracking-wider text-doom-ash/80">
  {session.difficulty}
</span>
```

- [ ] **Step 5: Typecheck + lint**

Run: `cd /home/arst/Projects/wretched_whispers/wretched-whispers-web && rtk npx tsc --noEmit`
Expected: `No errors found`.
Run: `rtk npx eslint src/components/session/DifficultyPicker.tsx src/app/sessions/page.tsx src/components/session/SessionCard.tsx src/types/api.ts`
Expected: exit 0.

- [ ] **Step 6: Manual verification**

Rebuild/restart the app (`npm run dev` for hot reload, or `npm run build && npm start`). Click "New Session" → the picker appears with Grim selected → choose a level → Begin → a session is created and the card shows the chosen difficulty.

- [ ] **Step 7: Commit**

```bash
cd /home/arst/Projects/wretched_whispers
rtk git add wretched-whispers-web/src
rtk git commit -m "feat(web): difficulty picker on new session

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0196wdJtXmageBJaytjBth3s"
```

---

## Final verification

- [ ] Full backend suite: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln` — all pass (live evals may skip without Azure creds).
- [ ] Frontend: `rtk npx tsc --noEmit` clean.
- [ ] Manual end-to-end: create a Story Mode session → character has notably higher HP; create a Hardcore session → challenge failures hit harder and doom accrues faster.
- [ ] Push branch and open PR:

```bash
rtk git push -u origin feat/difficulty-levels
```
