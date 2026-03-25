# Phase 7: Deterministic State Machine and Context Injection - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-24
**Phase:** 07-deterministic-state-machine-and-context-injection
**Areas discussed:** Stage Definitions, Context Injection, Tool Gating, Instruction Rewrite

---

## Stage Definitions

### Stage granularity

| Option | Description | Selected |
|--------|-------------|----------|
| Coarse (4 stages) | character-creation → campaign-setup → gameplay → ended | |
| Medium (6 stages) | character-creation → campaign-setup → exploration → combat → resolution → ended | ✓ |
| Fine (8+ stages) | Separate stages for each individual step | |

**User's choice:** Medium (6 stages) with clarification — encounter is one battle. Exploration leads to encounters, which may or may not escalate to combat. Combat stage runs until the fight is resolved.
**Notes:** User suggested combat sub-agent that resolves mechanically and reports narrative result back to game master.

### Combat orchestration

| Option | Description | Selected |
|--------|-------------|----------|
| Sub-agent for combat | Separate constrained agent resolves encounter mechanically, returns narrative | ✓ |
| Same agent, locked tools | Game master stays in control with restricted combat tools | |
| Hybrid | Sub-agent does mechanics, game master narrates each round | |

**User's choice:** Sub-agent for combat

### Fight gate (who decides combat vs. non-combat)

| Option | Description | Selected |
|--------|-------------|----------|
| Player decides | Player chooses fight/flee/negotiate | |
| Model decides | Model uses narrative judgment | ✓ |
| Domain rules decide | Encounter type determines auto-combat | |

**User's choice:** Model decides based on narrative

### Combat sub-agent return value

**User's choice:** Narrative only — domain state (injuries, HP, loot) is mutated by plugin calls during combat and propagated via context. Sub-agent returns narrative, game master sees updated state automatically.
**Notes:** "I think we manage data through the context, no? So we return the narrative, but everything else is reflected in the context: injuries, hp, loot and so on."

### Post-combat handling

| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated resolution stage | Separate stage for loot, consequences, aftermath | ✓ |
| Fold into exploration | Combat results narrated as part of exploration return | |

**User's choice:** Dedicated resolution stage

---

## Context Injection

### Context structure

**User's choice:** Dynamic system prompt injection for the model + plugin wrapper layer for tools
**Notes:** "We need to build another layer on top of existing things that will take all possible parameters from the context injected as KernelServiceInjection or something similar. This layer should only request non-context params (e.g., campaign id is always in the context after we create it, but campaign name is something only game master can generate)."

### ID handling

| Option | Description | Selected |
|--------|-------------|----------|
| Hide IDs completely | Model never sees GUIDs, server resolves from context | ✓ |
| Opaque tokens | Short tokens like char:1 mapped server-side | |
| Raw IDs | Current approach — model handles GUIDs | |

**User's choice:** Hide IDs completely

---

## Tool Gating

### Tool restriction mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| Dynamic plugin registration | Only register stage-appropriate plugins per kernel build | ✓ |
| All tools, server validation | All plugins registered, server rejects wrong-stage calls | |
| Filtered function list | SK function filtering to expose stage-valid functions | |

**User's choice:** Dynamic plugin registration

### Stage transitions

| Option | Description | Selected |
|--------|-------------|----------|
| Auto-transition on plugin success | Stage advances when key plugin call succeeds | ✓ |
| Explicit transition tool | Model calls AdvanceStage() | |
| Hybrid | Setup auto-transitions, gameplay explicit | |

**User's choice:** Auto-transition on plugin success

### Campaign setup flow

| Option | Description | Selected |
|--------|-------------|----------|
| Single compound action | One SetupCampaign(name) call | |
| Separate but guided | Individual calls with context auto-filling IDs | ✓ |
| You decide | Claude's discretion | |

**User's choice:** Separate but guided, with guardrail validation — errors steer model back ("No character created yet — call CreateCharacter first")

---

## Instruction Rewrite

### Instruction structure

| Option | Description | Selected |
|--------|-------------|----------|
| Per-stage prompt fragments | Each stage has own focused instruction block | ✓ |
| Single prompt with stage section | One master prompt with CURRENT STAGE header | |
| External prompt files | Stage instructions as separate resource files | |

**User's choice:** Per-stage prompt fragments

### Narrator tone handling

| Option | Description | Selected |
|--------|-------------|----------|
| Inline in system prompt | Tone rules in every stage fragment | |
| Separate persona prefix | Fixed narrator persona prepended to all stages | ✓ |
| You decide | Claude's discretion | |

**User's choice:** Separate persona prefix

---

## Claude's Discretion

- SessionContext class/record internal structure
- Semantic Kernel DI mechanism for context injection
- Dynamic plugin registration implementation
- Combat sub-agent implementation details
- Stage persistence mechanism
- Stage instruction storage approach

## Deferred Ideas

None
