# Getting Better Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mörk Borg's codified "Getting Better" post-adventure progression: one atomic domain-rolled ritual (6d10 HP check + d6 per ability), gated by a full night's rest, with ability decreases disabled on StoryMode.

**Architecture:** Same atomic-resolution shape as `Challenge`/`ResolveCombatRound`: domain entity method rolls everything and returns an outcome record; `CharacterService` orchestrates load→roll→save; an Engine `[GameTool]` maps it to a DTO the model narrates. A `CanGetBetter` flag on `Character`, set by full-night rest and consumed by the ritual, is the anti-spam gate.

**Tech Stack:** .NET 10, xUnit + Moq, solution `WrtechedWhispers/WrtechedWhispers.sln` (dir typo intentional).

**Spec:** `docs/superpowers/specs/2026-07-23-getting-better-design.md`

## Global Constraints

- Never use the null-forgiving operator (`!`) — validate instead.
- Prefix all shell commands with `rtk` (e.g. `rtk dotnet test`).
- Dice mock is 0-based: `SetupDiceRoll(sides, result)` → die value `result + 1`; `SetupDiceRolls(...)` is one sequential any-sides queue; calling either again REPLACES the previous setup/queue.
- Sealed domain entities, `[JsonConstructor]` persistence, primary constructors for services.
- Test commands: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "<Filter>"`.
- Work on branch `feat/getting-better` (create from `main` in Task 1, step 0).
- RAW rule being implemented: roll 6d10; if total ≥ current max HP → max HP +d6 (current HP unchanged). Then per ability (order: Strength, Agility, Presence, Toughness): roll d6; d6 ≥ modifier → +1 (cap +6); d6 < modifier → −1 only when ability loss is enabled (floor −3 is unreachable in practice: d6 min 1 always ≥ any negative score, so decreases require score ≥ 2).

---

### Task 1: Difficulty knob — `AbilityLossOnGettingBetter`

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultySettings.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultyPresets.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs`

**Interfaces:**
- Consumes: existing `DifficultySettings` record, `DifficultyPresets.For(Difficulty)`.
- Produces: `DifficultySettings.AbilityLossOnGettingBetter` (bool, last positional param) — `false` for StoryMode, `true` for Grim/Doomed/Hardcore. Task 6 reads it.

- [ ] **Step 0: Create the branch**

```bash
rtk git checkout -b feat/getting-better main
```

- [ ] **Step 1: Write the failing test**

Append to the existing class in `DifficultyPresetsTests.cs`:

```csharp
[Fact]
public void AbilityLossOnGettingBetter_DisabledOnlyInStoryMode()
{
    Assert.False(DifficultyPresets.For(Difficulty.StoryMode).AbilityLossOnGettingBetter);
    Assert.True(DifficultyPresets.For(Difficulty.Grim).AbilityLossOnGettingBetter);
    Assert.True(DifficultyPresets.For(Difficulty.Doomed).AbilityLossOnGettingBetter);
    Assert.True(DifficultyPresets.For(Difficulty.Hardcore).AbilityLossOnGettingBetter);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~DifficultyPresetsTests"`
Expected: compile error — `AbilityLossOnGettingBetter` not defined.

- [ ] **Step 3: Implement**

In `DifficultySettings.cs`, append the parameter (doc comment on the record already exists — extend the param list):

```csharp
public sealed record DifficultySettings(
    int StartingHpBonus,
    DiceExpr MinorDamage,
    DiceExpr SeriousDamage,
    DiceExpr DeadlyDamage,
    DiceExpr DawnDice,
    string GmToneNote,
    // MORK BORG "Getting Better": whether a low ability roll (d6 < score) degrades the ability.
    // RAW says yes; StoryMode is improvement-only so forgiving campaigns don't regress.
    bool AbilityLossOnGettingBetter);
```

In `DifficultyPresets.cs`, add the named argument to each of the four presets:
StoryMode → `AbilityLossOnGettingBetter: false`; Grim, Doomed, Hardcore → `AbilityLossOnGettingBetter: true`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~DifficultyPresetsTests"`
Expected: PASS (all facts in the class).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultySettings.cs WrtechedWhispers/WretchedWhispers.Core/Campaigns/DifficultyPresets.cs WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DifficultyPresetsTests.cs
rtk git commit -m "feat(difficulty): AbilityLossOnGettingBetter knob - StoryMode improvement-only"
```

---

### Task 2: Rest gate — `Character.CanGetBetter`

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs` (JsonConstructor ~line 24-60, properties ~line 101, `Rest` ~line 304)
- Test: create `WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterTests.cs`

**Interfaces:**
- Consumes: `Character.Rest(int hours, Dice dice)` (full night = `hours >= 8`), `TestCharacters.Create(Dice, ..., startingOmens: 1)`.
- Produces: `[JsonInclude] public bool CanGetBetter { get; private set; }` — init `false` on creation, set `true` by a full night's rest (not partial, not infected), consumed by Task 3's `GetBetter`. Persists via the JsonConstructor param `bool canGetBetter = false`.

- [ ] **Step 1: Write the failing tests**

Create `GettingBetterTests.cs`:

```csharp
using Moq;
using Xunit;

namespace WretchedWhispers.Tests.Characters;

/// <summary>MORK BORG "Getting Better": post-adventure ritual, gated by a full night's rest.
/// Dice mock is 0-based (SetupDiceRolls value 3 = die shows 4).</summary>
public sealed class GettingBetterTests : TestBase
{
    [Fact]
    public void NewCharacter_CannotGetBetter()
    {
        var character = TestCharacters.Create(Dice);

        Assert.False(character.CanGetBetter);
    }

    [Fact]
    public void FullNightRest_EnablesGettingBetter()
    {
        // startingOmens 1 so the full rest doesn't also roll the omen-refill d2.
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d6 */);

        character.Rest(8, Dice);

        Assert.True(character.CanGetBetter);
    }

    [Fact]
    public void PartialRest_DoesNotEnableGettingBetter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d4 */);

        character.Rest(4, Dice);

        Assert.False(character.CanGetBetter);
    }

    [Fact]
    public void InfectedFullRest_DoesNotEnableGettingBetter()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        character.Infect();
        SetupDiceRolls(0 /* infection damage d6 */);

        character.Rest(8, Dice);

        Assert.False(character.CanGetBetter);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterTests"`
Expected: compile error — `CanGetBetter` not defined.

- [ ] **Step 3: Implement**

In `Character.cs`:

1. JsonConstructor: append parameter `bool canGetBetter = false` after `bool isDead = false`, and in the body add `CanGetBetter = canGetBetter;` after `IsDead = isDead;`. (Default keeps existing saved character JSON loading.)
2. Property, next to `IsDead` (~line 101):

```csharp
    /// <summary>MORK BORG "Getting Better" gate: set by a full night's rest, consumed by the ritual.</summary>
    [JsonInclude] public bool CanGetBetter { get; private set; }
```

3. In `Rest` (~line 312), after `Hp = Hp.Heal(heal);` and before the omens early-return:

```csharp
        if (isFullNightRest) CanGetBetter = true;
```

(The infected branch returns before this line — an infected rest earns no ritual.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterTests|FullyQualifiedName~RestOmenTests"`
Expected: PASS (new facts + the existing rest/omen facts unchanged).

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterTests.cs
rtk git commit -m "feat(character): CanGetBetter rest gate for the Getting Better ritual"
```

---

### Task 3: Domain ritual — `Character.GetBetter` + `GettingBetterOutcome` + `HitPoints.IncreaseMax`

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Core/Characters/GettingBetterOutcome.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/HitPoints.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs` (add `GetBetter` after `Rest`, ~line 320)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterTests.cs` (extend Task 2's file)

**Interfaces:**
- Consumes: `CanGetBetter` (Task 2), existing `Improve(AbilityKind, int)` / `Degrade(AbilityKind, int)` (Degrade requires a NEGATIVE delta), `Abilities[AbilityKind].Modifier`, `Dice.Roll(DiceExpr)`, `DiceExpr.D(6, 10)`, `DiceExpr.D6`.
- Produces:
  - `HitPoints.IncreaseMax(int amount)` → `HitPoints` with `Max` raised, `Current` unchanged.
  - `sealed record AbilityChange(AbilityKind Kind, int Roll, int Delta, int NewScore)`
  - `sealed record GettingBetterOutcome(int HpRoll, int HpGained, int NewMaxHp, IReadOnlyList<AbilityChange> Abilities)`
  - `Character.GetBetter(Dice dice, bool allowAbilityLoss)` → `GettingBetterOutcome`; throws `InvalidOperationException` when `!CanGetBetter`; clears the flag on success. Ability order: Strength, Agility, Presence, Toughness. Dice order: 6×d10, then d6 (only if HP check passed), then 4×d6.

- [ ] **Step 1: Write the failing tests**

Append to `GettingBetterTests.cs` (inside the class):

```csharp
    /// <summary>A character who has earned the ritual: created, then given one full night's rest.
    /// startingOmens 1 avoids the omen-refill d2; the second SetupDiceRolls call in each test
    /// replaces this queue with the ritual's own rolls.</summary>
    private WretchedWhispers.Core.Characters.Character CreateRested(
        int strength = 0, int agility = 0, int presence = 0, int toughness = 0)
    {
        var character = TestCharacters.Create(Dice, agility: agility, presence: presence,
            strength: strength, toughness: toughness, startingOmens: 1);
        SetupDiceRolls(0 /* heal d6 */);
        character.Rest(8, Dice);
        return character;
    }

    [Fact]
    public void GetBetter_WithoutRest_Throws()
    {
        var character = TestCharacters.Create(Dice);

        Assert.Throws<InvalidOperationException>(() => character.GetBetter(Dice, allowAbilityLoss: true));
    }

    [Fact]
    public void GetBetter_HpRollMeetsMax_IncreasesMaxOnly()
    {
        var character = CreateRested(); // maxHp 20, current 20, all abilities 0
        // 6d10: six 4s = 24 >= 20 -> passes; HP gain d6 -> 3; then 4 ability d6 -> all 1 (>= 0 -> +1).
        SetupDiceRolls(3, 3, 3, 3, 3, 3, /* hp d6 */ 2, /* abilities */ 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(24, outcome.HpRoll);
        Assert.Equal(3, outcome.HpGained);
        Assert.Equal(23, outcome.NewMaxHp);
        Assert.Equal(23, character.Hp.Max);
        Assert.Equal(20, character.Hp.Current); // RAW: only the maximum grows
    }

    [Fact]
    public void GetBetter_HpRollBelowMax_NoHpChange()
    {
        var character = CreateRested(); // maxHp 20
        // 6d10: six 1s = 6 < 20 -> no HP d6 is rolled; next four rolls are the ability d6s.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* abilities */ 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(6, outcome.HpRoll);
        Assert.Equal(0, outcome.HpGained);
        Assert.Equal(20, character.Hp.Max);
    }

    [Fact]
    public void GetBetter_AbilityRollMeetsScore_ImprovesByOne()
    {
        var character = CreateRested(); // all abilities 0
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // d6 = 1 >= 0 -> +1 each

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.All(outcome.Abilities, a => Assert.Equal(1, a.Delta));
        Assert.Equal(1, character.Abilities.Strength.Modifier);
        Assert.Equal(1, character.Abilities.Agility.Modifier);
        Assert.Equal(1, character.Abilities.Presence.Modifier);
        Assert.Equal(1, character.Abilities.Toughness.Modifier);
        // Strength +1 recalculates carrying capacity: 2 * (1 + 8).
        Assert.Equal(18, character.Inventory.MaxCapacity);
    }

    [Fact]
    public void GetBetter_AbilityRollBelowScore_LossAllowed_Degrades()
    {
        var character = CreateRested(strength: 3);
        // HP check fails (six 1s). Strength rolls first: d6 = 1 < 3 -> -1. Others (0): +1.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* str */ 0, /* agi, pre, tou */ 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(2, character.Abilities.Strength.Modifier);
        var strength = outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength);
        Assert.Equal(-1, strength.Delta);
        Assert.Equal(2, strength.NewScore);
        Assert.Equal(20, character.Inventory.MaxCapacity); // 2 * (2 + 8)
    }

    [Fact]
    public void GetBetter_AbilityRollBelowScore_LossDisabled_Unchanged()
    {
        var character = CreateRested(strength: 3); // StoryMode behaviour
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: false);

        Assert.Equal(3, character.Abilities.Strength.Modifier);
        Assert.Equal(0, outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength).Delta);
    }

    [Fact]
    public void GetBetter_AbilityAtCap_RollMeetsScore_Unchanged()
    {
        var character = CreateRested(strength: 6);
        // Strength d6 = 6 >= 6 -> would improve, but +6 is the cap.
        SetupDiceRolls(0, 0, 0, 0, 0, 0, /* str */ 5, /* others */ 0, 0, 0);

        var outcome = character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.Equal(6, character.Abilities.Strength.Modifier);
        Assert.Equal(0, outcome.Abilities.Single(a => a.Kind == AbilityKind.Strength).Delta);
    }

    [Fact]
    public void GetBetter_ConsumesTheRestGate()
    {
        var character = CreateRested();
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        character.GetBetter(Dice, allowAbilityLoss: true);

        Assert.False(character.CanGetBetter);
        Assert.Throws<InvalidOperationException>(() => character.GetBetter(Dice, allowAbilityLoss: true));
    }
```

Add `using WretchedWhispers.Core.Characters.Abilities;` and `using System.Linq;` to the file's usings if not already present (`AbilityKind`, `.Single`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterTests"`
Expected: compile error — `GetBetter` not defined.

- [ ] **Step 3: Implement**

`HitPoints.cs` — add below `Damage`:

```csharp
    /// <summary>MORK BORG "Getting Better": only the maximum grows; current HP is untouched (rest heals).</summary>
    public HitPoints IncreaseMax(int amount)
    {
        return this with { Max = Max + Math.Max(0, amount) };
    }
```

Create `GettingBetterOutcome.cs`:

```csharp
using WretchedWhispers.Core.Characters.Abilities;

namespace WretchedWhispers.Core.Characters;

/// <summary>One ability's roll in the Getting Better ritual. Delta is +1, -1, or 0 (capped,
/// floored, or loss disabled by difficulty).</summary>
public sealed record AbilityChange(AbilityKind Kind, int Roll, int Delta, int NewScore);

/// <summary>The full result of a Getting Better ritual: the 6d10 HP check (HpGained 0 when it
/// failed) and one entry per ability in Strength, Agility, Presence, Toughness order.</summary>
public sealed record GettingBetterOutcome(
    int HpRoll, int HpGained, int NewMaxHp, IReadOnlyList<AbilityChange> Abilities);
```

`Character.cs` — add after `Rest` (~line 320):

```csharp
    /// <summary>MORK BORG "Getting Better": roll 6d10 -- meet or beat max HP and it grows by d6
    /// (current HP untouched). Then a d6 against each ability: meet or beat the score for +1 (cap +6);
    /// below it, lose 1 only when the difficulty allows ability loss. Requires a full night's rest
    /// since the last ritual; consumes that rest.</summary>
    public GettingBetterOutcome GetBetter(Dice dice, bool allowAbilityLoss)
    {
        if (!CanGetBetter)
            throw new InvalidOperationException(
                "Getting Better requires a full night's rest since the last ritual.");

        var hpRoll = dice.Roll(DiceExpr.D(6, 10));
        var hpGained = 0;
        if (hpRoll >= Hp.Max)
        {
            hpGained = dice.Roll(DiceExpr.D6);
            Hp = Hp.IncreaseMax(hpGained);
        }

        var changes = new List<AbilityChange>();
        foreach (var kind in new[]
                 { AbilityKind.Strength, AbilityKind.Agility, AbilityKind.Presence, AbilityKind.Toughness })
        {
            var score = Abilities[kind].Modifier;
            var roll = dice.Roll(DiceExpr.D6);
            var delta = roll >= score
                ? score < 6 ? 1 : 0
                : allowAbilityLoss && score > -3 ? -1 : 0;
            if (delta > 0) Improve(kind, delta);
            if (delta < 0) Degrade(kind, delta);
            changes.Add(new AbilityChange(kind, roll, delta, Abilities[kind].Modifier));
        }

        CanGetBetter = false;
        return new GettingBetterOutcome(hpRoll, hpGained, Hp.Max, changes);
    }
```

(`AbilityKind` and `DiceExpr` are already in `Character.cs`'s usings.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterTests"`
Expected: PASS — all facts.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Characters/GettingBetterOutcome.cs WrtechedWhispers/WretchedWhispers.Core/Characters/HitPoints.cs WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterTests.cs
rtk git commit -m "feat(character): Getting Better ritual - 6d10 HP check and d6 per ability"
```

---

### Task 4: Service orchestration — `CharacterService.GetBetter`

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Core/Characters/CharacterService.cs`
- Test: create `WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterServiceTests.cs`

**Interfaces:**
- Consumes: `Character.GetBetter(Dice, bool)` (Task 3), `ICharactersRepository.Get(Guid)` / `.Save(Character)`.
- Produces: `CharacterService.GetBetter(Guid characterId, bool allowAbilityLoss)` → `Task<GettingBetterOutcome>`; throws `ArgumentException` for an unknown id. Task 5's tool calls this.

- [ ] **Step 1: Write the failing tests**

Create `GettingBetterServiceTests.cs`:

```csharp
using Moq;
using WretchedWhispers.Core.Characters;
using Xunit;

namespace WretchedWhispers.Tests.Characters;

public sealed class GettingBetterServiceTests : TestBase
{
    [Fact]
    public async Task GetBetter_RollsAndSavesOnce()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);
        SetupDiceRolls(0 /* heal d6 */);
        character.Rest(8, Dice);
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(character.Id)).ReturnsAsync(character);
        var service = new CharacterService(repo.Object, Dice);
        SetupDiceRolls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // 6d10 fail, four ability d6s

        var outcome = await service.GetBetter(character.Id, allowAbilityLoss: true);

        Assert.Equal(6, outcome.HpRoll);
        Assert.False(character.CanGetBetter);
        repo.Verify(r => r.Save(character), Times.Once);
    }

    [Fact]
    public async Task GetBetter_UnknownCharacter_Throws()
    {
        var repo = new Mock<ICharactersRepository>();
        repo.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Character?)null);
        var service = new CharacterService(repo.Object, Dice);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetBetter(Guid.NewGuid(), allowAbilityLoss: true));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterServiceTests"`
Expected: compile error — `CharacterService.GetBetter` not defined.

- [ ] **Step 3: Implement**

Append to `CharacterService`:

```csharp
    public async Task<GettingBetterOutcome> GetBetter(Guid characterId, bool allowAbilityLoss)
    {
        var character = await charactersRepository.Get(characterId);
        if (character is null) throw new ArgumentException($"Character with id {characterId} does not exist.");

        var outcome = character.GetBetter(dice, allowAbilityLoss);
        await charactersRepository.Save(character);
        return outcome;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GettingBetterServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Core/Characters/CharacterService.cs WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterServiceTests.cs
rtk git commit -m "feat(character): CharacterService.GetBetter orchestration"
```

---

### Task 5: Engine tool + DTO — `CharacterTools.GettingBetter`

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Engine/GameTools/Models/GettingBetterOutcomeDto.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/GameTools/CharacterTools.cs` (add tool after `ChallengeCharacter`, ~line 76)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Services/GameToolCatalogTests.cs` (two stage lists)

**Interfaces:**
- Consumes: `CharacterService.GetBetter(Guid, bool)` (Task 4), `DifficultySettings.AbilityLossOnGettingBetter` (Task 1), existing `RequireCharacterId()`, `DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim)` idiom (`CharacterTools.cs:68`).
- Produces: `[GameTool(SessionStage.Exploration, SessionStage.Resolution)] GettingBetter()` → `Task<GettingBetterOutcomeDto>`; catalog names it `Character.GettingBetter`.

- [ ] **Step 1: Write the failing tests**

In `GameToolCatalogTests.cs`:

1. Rename `Exploration_ExposesExactlyFifteenTools` → `Exploration_ExposesExactlySixteenTools` and insert `"Character.GettingBetter"` into its expected array after `"Character.ChallengeCharacter"` (the list is alphabetical).
2. In `Resolution_ExposesResolutionToolsAndNoCreation`, insert `"Character.GettingBetter"` after `"Character.DegradeCharacterAbility"`.
3. Append the DTO mapping fact to `WrtechedWhispers/WretchedWhispers.Tests/Characters/GettingBetterTests.cs`:

```csharp
    [Fact]
    public void GettingBetterOutcomeDto_MapsOutcomeFaithfully()
    {
        var outcome = new GettingBetterOutcome(24, 3, 23,
            new[] { new AbilityChange(AbilityKind.Strength, 1, -1, 2) });

        var dto = WretchedWhispers.Engine.GameTools.Models.GettingBetterOutcomeDto.From(outcome);

        Assert.Equal(24, dto.HpRoll);
        Assert.Equal(3, dto.HpGained);
        Assert.Equal(23, dto.NewMaxHp);
        var ability = Assert.Single(dto.Abilities);
        Assert.Equal("Strength", ability.Ability);
        Assert.Equal(1, ability.Roll);
        Assert.Equal(-1, ability.Delta);
        Assert.Equal(2, ability.NewScore);
    }
```

(Add `using WretchedWhispers.Core.Characters;` to the file if `GettingBetterOutcome`/`AbilityChange` aren't yet resolvable there.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GameToolCatalogTests"`
Expected: FAIL — both stage lists lack `Character.GettingBetter`.

- [ ] **Step 3: Implement**

Create `GettingBetterOutcomeDto.cs`:

```csharp
using WretchedWhispers.Core.Characters;

namespace WretchedWhispers.Engine.GameTools.Models;

public sealed record AbilityChangeDto(string Ability, int Roll, int Delta, int NewScore);

/// <summary>Result of the Getting Better ritual. Negative ability deltas are the RAW "or worse" --
/// narrate the regression, don't soften it into nothing. HpGained 0 means the 6d10 check failed.</summary>
public sealed record GettingBetterOutcomeDto(
    int HpRoll, int HpGained, int NewMaxHp, IReadOnlyList<AbilityChangeDto> Abilities)
{
    public static GettingBetterOutcomeDto From(GettingBetterOutcome outcome) => new(
        outcome.HpRoll, outcome.HpGained, outcome.NewMaxHp,
        outcome.Abilities
            .Select(a => new AbilityChangeDto(a.Kind.ToString(), a.Roll, a.Delta, a.NewScore))
            .ToList());
}
```

Add to `CharacterTools` after `ChallengeCharacter` (~line 76):

```csharp
    [Description("MORK BORG 'Getting Better': the post-adventure improvement ritual and the ONLY leveling mechanic. Call ONLY when the fiction concludes a genuine adventure or scenario -- a quest completed, a dungeon survived, a nemesis dead -- never after a routine fight. Requires a full night's rest since the last ritual (fails otherwise). The domain rolls everything: 6d10 vs max HP (max grows by d6 on success) and a d6 against each ability (improve, or on harder difficulties worsen). Narrate the returned result.")]
    [GameTool(SessionStage.Exploration, SessionStage.Resolution)]
    public async Task<GettingBetterOutcomeDto> GettingBetter()
    {
        var settings = DifficultyPresets.For(sessionContext.Campaign?.Difficulty ?? Difficulty.Grim);
        var outcome = await characterService.GetBetter(
            RequireCharacterId(), settings.AbilityLossOnGettingBetter);
        return GettingBetterOutcomeDto.From(outcome);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~GameToolCatalogTests|FullyQualifiedName~AgentToolProviderTests|FullyQualifiedName~GameToolsTests|FullyQualifiedName~GettingBetterTests"`
Expected: PASS — catalog lists updated, DTO mapping fact green, provider/tool tests still green.

- [ ] **Step 5: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/GameTools/Models/GettingBetterOutcomeDto.cs WrtechedWhispers/WretchedWhispers.Engine/GameTools/CharacterTools.cs WrtechedWhispers/WretchedWhispers.Tests/Services/GameToolCatalogTests.cs
rtk git commit -m "feat(engine): GettingBetter tool with atomic domain-rolled outcome"
```

---

### Task 6: Narrator prompt + full verification

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Engine/Prompts/NarratorPersona.cs` (~line 55, after the "Death is permanent" bullet)

**Interfaces:**
- Consumes: the `GettingBetter` tool name (Task 5).
- Produces: nothing downstream — prompt-only.

- [ ] **Step 1: Add the persona bullet**

Insert after the "Death is permanent and final..." bullet:

```
        - Getting Better is the codified post-adventure ritual and the ONLY leveling mechanic. When the
          fiction concludes a genuine adventure -- a quest completed, a dungeon survived, a nemesis dead --
          and the character has slept a full night since the last ritual, offer it and call the GettingBetter
          tool; the domain rolls everything. Narrate the returned result, including ability losses -- that is
          the dying world taking its due. Never use ImproveCharacterAbility or DegradeCharacterAbility for
          leveling; those are for story-driven blessings and curses only.
```

- [ ] **Step 2: Full build and test run**

Run: `rtk dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Expected: Build succeeded, 0 errors.

Run: `rtk dotnet test WrtechedWhispers/WrtechedWhispers.sln`
Expected: all tests pass (live DomainAuthorityEvals run only when Azure OpenAI creds are present; they cover narration groundedness — watch them since the persona changed).

- [ ] **Step 3: Commit**

```bash
rtk git add WrtechedWhispers/WretchedWhispers.Engine/Prompts/NarratorPersona.cs
rtk git commit -m "feat(prompts): Getting Better is the only leveling ritual"
```

- [ ] **Step 4: Finish the branch**

Use superpowers:finishing-a-development-branch — push `feat/getting-better` and open a PR to `main` (gh CLI not installed; use the compare URL `https://github.com/arst/wretched_whispers/compare/main...feat/getting-better`).

---

## Manual playtest (post-merge)

Conclude an adventure in a session, rest a full night, ask the GM about getting better → one tool call resolves the whole ritual, drawer shows new max HP/abilities. Calling again without a rest is refused in-world.
