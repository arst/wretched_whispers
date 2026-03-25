# Phase 7: Deterministic State Machine and Context Injection - Context

**Gathered:** 2026-03-24
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the free-form 14-step agent instructions with an explicit session stage state machine where plugin tool calls drive transitions, and a session context object gives the model only the tools and information relevant to the current stage. The model narrates and picks tools; the system manages all state, IDs, and transitions.

</domain>

<decisions>
## Implementation Decisions

### Stage Definitions
- **D-01:** 6 stages: character-creation → campaign-setup → exploration → combat → resolution → ended
- **D-02:** Combat is a self-contained sub-agent — a tightly-constrained agent that resolves the entire encounter mechanically and returns only a narrative result to the game master
- **D-03:** Domain state (injuries, HP, loot, equipment damage) is mutated by plugin calls during combat and propagated via the context object — the combat sub-agent returns narrative only, the game master sees updated state automatically through context
- **D-04:** The narrative game master model decides whether encounters escalate to combat (organic storytelling, not rule-based). Some encounters are negotiation, some are unavoidable
- **D-05:** Dedicated resolution stage after combat handles loot, injury consequences, and narrative aftermath before returning to exploration
- **D-06:** Exploration → combat → resolution → exploration is the core gameplay loop until death or apocalypse triggers "ended"

### Context Injection
- **D-07:** Dynamic system prompt injection — model gets a composed prompt per turn: narrator persona (fixed) + stage instructions (dynamic) + context snapshot (current game state: character, campaign, encounter, stage)
- **D-08:** Plugin wrapper layer — new layer on top of existing plugins that uses Semantic Kernel DI (KernelArguments / service injection) to auto-fill context parameters (campaign ID, character ID, encounter ID). Model only provides parameters the context cannot supply (e.g., campaign name when creating, dice expression when rolling)
- **D-09:** IDs completely hidden from the model — model never sees GUIDs. Server-side context resolves all IDs from session state. Model interacts with narrative concepts ("your character", "the current encounter"), not technical identifiers

### Tool Gating
- **D-10:** Dynamic plugin registration per stage — kernel is rebuilt with only stage-appropriate plugins. In character-creation only CharacterPlugin is available, in combat only combat tools, etc. Model literally cannot call wrong-stage tools
- **D-11:** Auto-transition on plugin success — when a stage-completing plugin call succeeds (e.g., CreateCharacter), the stage advances automatically. No explicit "advance stage" tool needed
- **D-12:** Guardrail validation in plugin wrappers — if model tries to add a character to campaign but no character exists in context, return a corrective error message that steers the model back ("No character created yet — call CreateCharacter first"). If model tries to create a character when one already exists, return error. Errors guide, not just reject
- **D-13:** Campaign setup uses separate guided calls (CreateCampaign → AddCharacterToCampaign → StartCampaign) not a compound action, for narrative pacing between beats. Context auto-fills IDs for each step

### Instruction Rewrite
- **D-14:** Per-stage prompt fragments — each stage has its own focused instruction block loaded dynamically. No more monolithic 14-step instruction string
- **D-15:** Separate narrator persona prefix — a fixed "narrator persona" block (doom metal tone, dark humor, visceral language) prepended to every stage's prompt. Tone defined once, stage instructions added after. Clean separation of voice vs. mechanics
- **D-16:** System prompt composition: narrator persona (fixed) + stage instructions (per-stage fragment) + context snapshot (dynamic game state)

### Claude's Discretion
- How to structure the SessionContext class/record internally
- Which Semantic Kernel DI mechanism to use for context injection (KernelArguments, custom service, etc.)
- How to implement dynamic plugin registration (kernel rebuild vs. function filtering)
- Combat sub-agent implementation details (separate ChatCompletionAgent, prompt structure, tool set)
- How to persist stage in the database (new column on Campaign, separate table, or derived)
- How stage instructions are stored (embedded strings, resource files, or configuration)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Semantic Kernel Agent Orchestration
- `WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs` — Current agent orchestration, kernel building, agent creation, 14-step instructions, DeriveStatus
- `WrtechedWhispers/WretchedWhispers.Semantic/DicePlugin.cs` — Plugin pattern reference (DiceRollResult structured return)
- `WrtechedWhispers/WretchedWhispers.Semantic/CharacterPlugin.cs` — Character lifecycle tools
- `WrtechedWhispers/WretchedWhispers.Semantic/CampaignPlugin.cs` — Campaign lifecycle tools
- `WrtechedWhispers/WretchedWhispers.Semantic/EncounterPlugin.cs` — Encounter/combat tools

### Domain Model
- `WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs` — Campaign state: IsStarted, IsEnded, IsActive, Characters, Calendar, Encounters
- `WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs` — Character state, injuries, equipment

### Infrastructure
- `WrtechedWhispers/WretchedWhispers.Semantic/IChatHistoryRepository.cs` — Chat history management
- `WrtechedWhispers/WretchedWhispers.Api/Infrastructure/Settings.cs` — Configuration pattern

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **DeriveStatus()** in GameSessionService: already returns 3 states (character-creation / in-progress / ended) — extend to 6 stages
- **Plugin DI pattern**: plugins registered as Scoped, resolved via ImportPluginFromObject — same pattern for wrapper layer
- **DiceRollResult record**: structured plugin return type pattern — use same approach for combat results
- **ChatHistorySummarizationReducer**: existing history management — context injection should complement, not replace

### Established Patterns
- Kernel built per-turn in BuildKernelForSession() — natural point to inject stage-based plugin registration
- Agent instructions as string in CreateGameMasterAgent() — replace with composed prompt fragments
- Channel<SseEvent> bridge for streaming — unchanged, context injection is upstream of streaming

### Integration Points
- GameSessionService.ExecuteAgentTurnAsync — main integration point for stage machine and context
- BuildKernelForSession — where dynamic plugin registration happens
- CreateGameMasterAgent — where prompt composition happens
- StateUpdateEvent — already emits character/campaign state, may need stage field

</code_context>

<specifics>
## Specific Ideas

- Combat sub-agent should be fully mechanical — resolve the fight, mutate domain state via plugins, return narrative. Game master never touches combat mechanics
- Error messages from guardrails should be corrective and natural: "No character created yet — call CreateCharacter first" not "Error: Invalid state transition"
- The context snapshot should be formatted for narrative consumption, not as raw data dumps

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-deterministic-state-machine-and-context-injection*
*Context gathered: 2026-03-24*
