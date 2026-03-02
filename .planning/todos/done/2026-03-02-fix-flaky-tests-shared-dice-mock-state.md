---
created: 2026-03-02T15:40:26.670Z
title: Fix flaky tests caused by shared static Dice mock state
area: testing
files:
  - WrtechedWhispers/WretchedWhispers.Tests/Characters/Powers/PowerPoolTests.cs:110
  - WrtechedWhispers/WretchedWhispers.Tests/Campaigns/World/CalendarOfNechrubelTests.cs:51
  - WrtechedWhispers/WretchedWhispers.Core/Dices/Dice.cs
---

## Problem

4 tests fail intermittently due to shared static `Dice` mock state across test classes. The `Dice` class uses a static `SetRandomGenerator()` method, and when tests run in parallel, one test's mock setup bleeds into another's execution.

Failing tests:
1. `PowerPoolTests.ResetForNewDay_WithChangedAbilities_ShouldUseNewPresenceModifier` — expects 3, gets 2
2. `CalendarOfNechrubelTests.WorldEnded_WhenSevenMiseriesTriggered_ReturnsTrue` — "Too many attempts to pick a misery"
3. `CalendarOfNechrubelTests.DawnRoll_WhenWorldEnded_ThrowsInvalidOperationException` — same error
4. `CalendarOfNechrubelTests.DawnRoll_CallMultipleTimes_AccumulatesMiseries` — same error

Root cause: `SeededRandomService` is shared as a singleton via static `Dice.SetRandomGenerator()`. Tests that set up specific random sequences conflict when run in parallel.

## Solution

Options:
- Inject `IRandomService` per-test instead of using static setter
- Use xUnit test collections to prevent parallel execution of Dice-dependent tests
- Refactor `Dice` to accept `IRandomService` as a parameter instead of static global
