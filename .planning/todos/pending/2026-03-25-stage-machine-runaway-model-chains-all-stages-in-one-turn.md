---
created: "2026-03-25T10:09:16.675Z"
title: "Stage machine runaway — model chains all stages in one turn"
area: api
files:
  - WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs
  - WrtechedWhispers/WretchedWhispers.Api/Services/StageTransitionFilter.cs
  - WrtechedWhispers/WretchedWhispers.Api/Services/StagePluginRegistry.cs
---

## Problem

When the user gives a character name, the model chains through all 6 stages in a single turn: CreateCharacter → ConfigureCampaign → StartCampaign → AdvanceTime (repeated) → character dies on Day 10. The "YOUR WRETCH HAS FALLEN" end card appears immediately after the first player message.

Three fix attempts failed:
1. StageTransitionFilter re-deriving stage mid-turn (made it worse — enabled stage escalation)
2. StageTransitionFilter with locked stage at turn start (didn't help — bug persists)
3. FunctionChoiceBehavior.Auto(functions:) whitelist (SK may not enforce strictly)

Root cause unknown. The code is too convoluted (GameSessionService has 10 responsibilities in 488 lines, zero logging) to trace the actual execution flow without a debugger. Needs observability before further debugging, or may surface naturally during planned refactoring.

## Solution

Being addressed as part of the GameSessionService refactoring (Phase 7 follow-up):
- Decompose GameSessionService so the turn flow is traceable
- Add structured logging/tracing to see stage derivation, function calls, blocked attempts
- Replace manual SSE with native .NET 10 Results.ServerSentEvents
- May need to only register stage-appropriate plugins on the kernel (instead of all + filter)
