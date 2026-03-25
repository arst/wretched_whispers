---
created: "2026-03-25T14:30:00.000Z"
title: "Combat system redesign — player-driven rounds, hit mechanics audit"
area: api
files:
  - WrtechedWhispers/WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs
  - WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs
  - WrtechedWhispers/WretchedWhispers.Api/Prompts/CombatPrompts.cs
  - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/EncounterWrapperPlugin.cs
  - WrtechedWhispers/WretchedWhispers.Semantic/EncounterPlugin.cs
---

## Problem

Three interconnected issues with the combat system:

### 1. Combat sub-agent runs autonomously without player input
CombatAgentService loops internally for up to 30 iterations, calling AttackPlayer/AttackAdversary repeatedly without returning control to the player. The player says "attack" and watches a 19-round monologue. Combat should be player-driven: player says what to do → system resolves one round → returns result → waits for next player input.

### 2. AttackAdversary almost never hits
In testing, AttackAdversary returned `IsHit: false` for ~40 consecutive calls before a single hit. The encounter becomes unwinnable. Need to audit the hit roll mechanics in EncounterPlugin.AttackAdversary — check the DR, ability modifier application, and damage calculation.

### 3. Agent narrates combat without calling tools
The combat agent produced 19 rounds of narrative text describing attacks, hits, misses, and damage — all fabricated — before making any actual tool calls. The agent must call tools FIRST and narrate the results, not invent outcomes.

## Solution

### Combat flow redesign
Remove CombatAgentService's autonomous loop. Instead:
- When stage is Combat, TurnCoordinator uses the regular AgentExecutor (not a separate combat agent)
- Each player message triggers ONE round: adversary attacks (AttackPlayer) + player attack (AttackAdversary) + narrative of results
- The player participates every round by sending a message (e.g., "attack the rat", "defend", "flee")
- Stage stays Combat until encounter ends or character dies (DeriveStage handles this)

### Hit mechanics audit
- Read EncounterPlugin.AttackAdversary and trace the hit roll
- Check DR calculation, Strength modifier application
- Verify damage is applied correctly to adversary HP
- May need to adjust DR or make hits more likely for low-stat characters

### Prompt fix
- Combat stage prompt must instruct: call AttackPlayer for each adversary, call AttackAdversary for the player's target, narrate the RESULTS of those calls — never invent combat outcomes
