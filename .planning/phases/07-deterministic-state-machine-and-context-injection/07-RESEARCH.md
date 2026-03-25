# Phase 7: Deterministic State Machine and Context Injection - Research

**Researched:** 2026-03-24
**Domain:** Semantic Kernel agent orchestration, state machine design, dynamic plugin registration
**Confidence:** HIGH

## Summary

This phase replaces the monolithic 14-step agent instructions with a 6-stage state machine where plugin tool call side effects drive transitions deterministically. The core technical challenge is threefold: (1) dynamically controlling which Semantic Kernel plugins are visible to the model per stage, (2) composing system prompts from fragments (persona + stage instructions + context snapshot), and (3) building a plugin wrapper layer that auto-fills IDs from session context so the model never sees GUIDs.

Semantic Kernel 1.65.0 (the version in use) provides `FunctionChoiceBehavior.Auto(functions: [...])` which accepts a specific list of `KernelFunction` objects, making tool gating straightforward without rebuilding the kernel. The `IAutoFunctionInvocationFilter` interface enables intercepting function calls for guardrail validation and auto-transition logic. The existing `BuildKernelForSession()` and `CreateGameMasterAgent()` methods in `GameSessionService` are the natural integration points -- they already build a kernel and agent per turn.

**Primary recommendation:** Use `FunctionChoiceBehavior.Auto(functions: stageAllowedFunctions)` for tool gating, `IAutoFunctionInvocationFilter` for guardrails and auto-transitions, and a `SessionContext` record to compose dynamic system prompts and auto-fill plugin parameters via wrapper plugins.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** 6 stages: character-creation, campaign-setup, exploration, combat, resolution, ended
- **D-02:** Combat is a self-contained sub-agent -- a tightly-constrained agent that resolves the entire encounter mechanically and returns only a narrative result to the game master
- **D-03:** Domain state (injuries, HP, loot, equipment damage) is mutated by plugin calls during combat and propagated via the context object -- the combat sub-agent returns narrative only, the game master sees updated state automatically through context
- **D-04:** The narrative game master model decides whether encounters escalate to combat (organic storytelling, not rule-based)
- **D-05:** Dedicated resolution stage after combat handles loot, injury consequences, and narrative aftermath before returning to exploration
- **D-06:** Exploration -> combat -> resolution -> exploration is the core gameplay loop until death or apocalypse triggers "ended"
- **D-07:** Dynamic system prompt injection -- model gets a composed prompt per turn: narrator persona (fixed) + stage instructions (dynamic) + context snapshot (current game state)
- **D-08:** Plugin wrapper layer -- new layer on top of existing plugins that uses Semantic Kernel DI to auto-fill context parameters. Model only provides parameters the context cannot supply
- **D-09:** IDs completely hidden from the model -- server-side context resolves all IDs from session state
- **D-10:** Dynamic plugin registration per stage -- kernel is rebuilt with only stage-appropriate plugins. Model literally cannot call wrong-stage tools
- **D-11:** Auto-transition on plugin success -- when a stage-completing plugin call succeeds, the stage advances automatically
- **D-12:** Guardrail validation in plugin wrappers -- corrective error messages that steer the model back
- **D-13:** Campaign setup uses separate guided calls (CreateCampaign -> AddCharacterToCampaign -> StartCampaign) for narrative pacing
- **D-14:** Per-stage prompt fragments -- each stage has its own focused instruction block
- **D-15:** Separate narrator persona prefix -- a fixed "narrator persona" block prepended to every stage's prompt
- **D-16:** System prompt composition: narrator persona (fixed) + stage instructions (per-stage fragment) + context snapshot (dynamic game state)

### Claude's Discretion
- How to structure the SessionContext class/record internally
- Which Semantic Kernel DI mechanism to use for context injection (KernelArguments, custom service, etc.)
- How to implement dynamic plugin registration (kernel rebuild vs. function filtering)
- Combat sub-agent implementation details (separate ChatCompletionAgent, prompt structure, tool set)
- How to persist stage in the database (new column on Campaign, separate table, or derived)
- How stage instructions are stored (embedded strings, resource files, or configuration)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| MORK-01 | Full session lifecycle from character creation through 7th Misery or death | State machine (6 stages) + auto-transition + context injection enables deterministic lifecycle. Stage machine enforces correct ordering, plugin gating prevents wrong-stage actions, auto-transition advances stages on plugin success |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.SemanticKernel | 1.65.0 | Kernel, plugin system, function calling | Already in project, provides FunctionChoiceBehavior filtering |
| Microsoft.SemanticKernel.Agents.Core | 1.65.0 | ChatCompletionAgent for GM and combat sub-agent | Already in project, supports per-agent kernel with different plugins |
| Microsoft.SemanticKernel.Connectors.AzureOpenAI | 1.65.0 | Azure OpenAI chat completion | Already in project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xunit | 2.9.3 | Unit testing | Already in project -- test stage transitions and context injection |
| Moq | 4.20.72 | Mocking for unit tests | Already in project -- mock repositories for stage logic tests |

No new packages needed. All required capabilities exist in the current Semantic Kernel 1.65.0 stack.

## Architecture Patterns

### Recommended Project Structure
```
WretchedWhispers.Api/
  Services/
    GameSessionService.cs          # Modified: orchestrates stage machine
    SessionContext.cs              # NEW: session state record
    SessionStage.cs                # NEW: enum + stage definitions
    StagePluginRegistry.cs         # NEW: maps stages to allowed functions
    PromptComposer.cs              # NEW: composes system prompts from fragments
  Plugins/
    GameMasterPlugins/             # NEW: wrapper plugins that auto-fill IDs
      CharacterWrapperPlugin.cs
      CampaignWrapperPlugin.cs
      EncounterWrapperPlugin.cs
      DiceWrapperPlugin.cs
    CombatAgent/                   # NEW: combat sub-agent
      CombatAgentService.cs
      CombatPlugin.cs              # Thin wrapper over EncounterPlugin for combat-only tools
  Prompts/                         # NEW: stage prompt fragments
    NarratorPersona.cs             # Fixed persona text
    StagePrompts.cs                # Per-stage instruction fragments
WretchedWhispers.Semantic/        # UNCHANGED: existing plugins remain as-is
WretchedWhispers.Core/
  Campaigns/
    Campaign.cs                    # Modified: add Stage property
    SessionStage.cs                # NEW: stage enum in domain (or Api layer)
```

### Pattern 1: Function Filtering for Tool Gating (D-10)
**What:** Use `FunctionChoiceBehavior.Auto(functions: allowedFunctions)` to restrict which functions the model can call per stage, rather than rebuilding the kernel.
**When to use:** Every turn -- the stage determines the allowed function list.
**Why this over kernel rebuild:** Simpler, no need to rebuild kernel. All plugins are imported once, but only stage-appropriate functions are advertised to the model.

```csharp
// Source: https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/function-calling/function-choice-behaviors
// Build kernel once with all plugins
var kernel = BuildKernelForSession();

// Get only the functions allowed for the current stage
var allowedFunctions = stagePluginRegistry.GetFunctionsForStage(sessionContext.CurrentStage, kernel);

var agent = new ChatCompletionAgent
{
    Name = "Game_Master",
    Instructions = promptComposer.Compose(sessionContext),
    Kernel = kernel,
    Arguments = new KernelArguments(
        new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(functions: allowedFunctions)
        })
};
```

### Pattern 2: Auto-Transition via IAutoFunctionInvocationFilter (D-11)
**What:** Use Semantic Kernel's `IAutoFunctionInvocationFilter` to intercept plugin completions and trigger stage transitions.
**When to use:** When a stage-completing function succeeds (e.g., CreateCharacter completes character-creation stage).

```csharp
// Source: https://learn.microsoft.com/en-us/semantic-kernel/concepts/enterprise-readiness/filters
public sealed class StageTransitionFilter(SessionContext sessionContext) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        await next(context);

        // Check if this function call should trigger a stage transition
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName;

        if (sessionContext.ShouldTransition(pluginName, functionName))
        {
            sessionContext.AdvanceStage();
        }
    }
}
```

### Pattern 3: Plugin Wrapper for ID Auto-Fill (D-08, D-09)
**What:** Wrapper plugins that delegate to existing plugins but auto-fill IDs from SessionContext. The model sees simplified signatures without GUID parameters.
**When to use:** All wrapper plugins that replace direct plugin access.

```csharp
public sealed class CampaignWrapperPlugin(
    CampaignPlugin inner,
    SessionContext sessionContext)
{
    [KernelFunction]
    [Description("Create a new campaign with the specified dice expression, name, and description")]
    public async Task<CampaignDto> CreateCampaign(
        string diceExpression, string name, string description)
    {
        var result = await inner.CreateCampaign(diceExpression, name, description);
        sessionContext.SetCampaignId(result.Id);
        return result;
    }

    [KernelFunction]
    [Description("Add your character to the campaign")]
    public async Task AddCharacterToCampaign()
    {
        // Auto-fill both IDs from context -- model provides nothing
        var campaignId = sessionContext.CampaignId
            ?? throw new InvalidOperationException("No campaign created yet -- call CreateCampaign first");
        var characterId = sessionContext.CharacterId
            ?? throw new InvalidOperationException("No character created yet -- call CreateCharacter first");

        await inner.AddCharacterToCampaign(campaignId, characterId);
    }
}
```

### Pattern 4: Dynamic System Prompt Composition (D-07, D-14, D-15, D-16)
**What:** Compose the agent's system prompt from three fragments: narrator persona + stage instructions + context snapshot.
**When to use:** Every turn -- prompt is recomposed based on current stage and game state.

```csharp
public sealed class PromptComposer
{
    public string Compose(SessionContext context)
    {
        var persona = NarratorPersona.Text;                          // Fixed
        var stageInstructions = StagePrompts.For(context.Stage);     // Per-stage
        var snapshot = context.FormatSnapshot();                     // Dynamic

        return $"""
            {persona}

            ## Current Stage: {context.Stage}
            {stageInstructions}

            ## Game State
            {snapshot}
            """;
    }
}
```

### Pattern 5: Combat Sub-Agent (D-02, D-03)
**What:** A separate `ChatCompletionAgent` with its own kernel containing only combat tools (AttackPlayer, AttackAdversary, EndEncounter, Roll). It resolves the entire encounter mechanically and returns narrative.
**When to use:** When the game master transitions to the combat stage.

```csharp
// Combat sub-agent gets a separate kernel with combat-only tools
var combatKernel = BuildCombatKernel();
var combatAgent = new ChatCompletionAgent
{
    Name = "Combat_Resolver",
    Instructions = CombatPrompts.Instructions,
    Kernel = combatKernel,
    Arguments = new KernelArguments(
        new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        })
};

// Run combat to completion, collect narrative
var narrative = await RunCombatToCompletion(combatAgent, encounterContext);

// Domain state already mutated by plugin calls during combat
// Return narrative to game master's chat history
```

### Anti-Patterns to Avoid
- **Kernel rebuild per stage:** Don't create a new `Kernel` for each stage. Use `FunctionChoiceBehavior.Auto(functions: [...])` instead. Kernel rebuild is expensive and loses DI-resolved services.
- **Model managing IDs:** Don't expose GUID parameters to the model. Wrapper plugins auto-fill from context.
- **Monolithic instructions:** Don't put all stage instructions in a single string. Compose from fragments.
- **Implicit stage transitions:** Don't let the model decide when to transition stages. Use auto-transition on plugin success.
- **State in chat history only:** Don't rely on chat history for state. The `SessionContext` is the source of truth.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Function filtering per stage | Custom middleware that intercepts tool calls | `FunctionChoiceBehavior.Auto(functions: [...])` | Built into SK, handles serialization and model communication |
| Function call interception | Manual post-processing of agent responses | `IAutoFunctionInvocationFilter` | Built into SK, fires on every auto-invoked function with full context |
| Agent with separate tools | Custom HTTP calls to a second model | `ChatCompletionAgent` with its own `Kernel` | SK agents already support per-agent kernels and plugin sets |
| Prompt templating | Custom string interpolation | Compose from well-defined string constants | Simple string composition is sufficient; SK's prompt template engine is overkill for static fragments |

**Key insight:** Semantic Kernel 1.65.0 already provides the mechanisms needed for tool gating (FunctionChoiceBehavior.Auto with explicit function lists), function interception (IAutoFunctionInvocationFilter), and multi-agent orchestration (ChatCompletionAgent). The phase is about wiring these together, not building new infrastructure.

## Architecture Recommendations (Claude's Discretion Areas)

### SessionContext Structure
**Recommendation:** Use a mutable class (not record) since it accumulates state across the turn. Registered as Scoped in DI so it lives for the request.

```csharp
public sealed class SessionContext
{
    public Guid SessionId { get; init; }
    public SessionStage Stage { get; private set; }
    public Guid? CharacterId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? ActiveEncounterId { get; private set; }

    // Loaded at start of turn from DB
    public Character? Character { get; set; }
    public Campaign? Campaign { get; set; }
    public Encounter? ActiveEncounter { get; set; }

    public void AdvanceStage() { /* transition logic */ }
    public string FormatSnapshot() { /* narrative-friendly state dump */ }
}
```

### Context Injection Mechanism
**Recommendation:** Pass `SessionContext` via constructor DI into wrapper plugins (scoped service). The wrapper plugins read IDs from context and delegate to the inner plugins. This is simpler than KernelArguments injection and matches the existing DI pattern (plugins already use constructor DI).

### Dynamic Plugin Registration Approach
**Recommendation:** Use function filtering via `FunctionChoiceBehavior.Auto(functions: [...])` rather than kernel rebuild. Import all wrapper plugins into the kernel, then select which functions to advertise based on stage. This is more efficient and keeps the kernel stable.

### Stage Persistence
**Recommendation:** Derive stage from domain state (extending the existing `DeriveStatus` pattern) rather than persisting it. The 3-state `DeriveStatus` already exists; extend to 6 stages:
- `character-creation`: No character in context (campaign.Players.Count == 0)
- `campaign-setup`: Character exists but campaign not started (!campaign.IsStarted)
- `exploration`: Campaign active, no active encounter
- `combat`: Active encounter exists and is started
- `resolution`: Active encounter ended but not yet resolved (needs a resolution flag or "last encounter ended" check)
- `ended`: Campaign ended (IsEnded || character is dead || world ended)

This avoids schema migration and keeps the domain as source of truth. The resolution stage may need a simple flag (e.g., `NeedsResolution` on the encounter or session).

### Stage Instruction Storage
**Recommendation:** Static string constants in a dedicated `StagePrompts` class. Resource files add complexity without benefit for 6 short instruction blocks. Easy to read, easy to test, easy to modify.

### Combat Sub-Agent
**Recommendation:** A separate `ChatCompletionAgent` created per combat encounter with its own kernel containing only combat-related wrapper plugins. It runs in a loop until the encounter ends, then returns a narrative summary. The game master agent's chat history receives only the narrative result, not the combat turn-by-turn.

## Common Pitfalls

### Pitfall 1: Function List Stale After Stage Transition
**What goes wrong:** Stage transitions during a turn (e.g., CreateCharacter triggers transition to campaign-setup) but the agent's `FunctionChoiceBehavior` still has the old function list.
**Why it happens:** The agent is created once per turn with a fixed function list.
**How to avoid:** After a stage transition, the current turn completes with the old tools. The next player message triggers a new turn with the updated stage and function list. Document this as intentional: transitions take effect on the NEXT turn.
**Warning signs:** Model tries to call a function from the new stage in the same turn.

### Pitfall 2: Model Hallucinating IDs Despite Wrapper
**What goes wrong:** If any original plugin (with GUID parameters) is accidentally exposed, the model may hallucinate IDs.
**Why it happens:** Original plugins are imported alongside wrappers, and both get advertised.
**How to avoid:** Only import WRAPPER plugins into the kernel. The original plugins remain as DI services but are NOT imported as kernel plugins. Wrappers delegate to the originals via constructor injection.
**Warning signs:** Tool calls containing GUID parameters in the chat history.

### Pitfall 3: Combat Sub-Agent Losing Transaction Context
**What goes wrong:** The combat sub-agent's plugin calls don't participate in the same DB transaction as the game master's turn.
**Why it happens:** If combat agent uses a separate scope, it gets a different DbContext.
**How to avoid:** Run the combat sub-agent within the same DI scope as the game master turn. The scoped DbContext and repositories are shared. The existing transaction wrapping in `ExecuteAgentTurnAsync` covers both agents.
**Warning signs:** Partial combat state saved after a failure.

### Pitfall 4: Infinite Combat Loop
**What goes wrong:** Combat sub-agent keeps calling tools without resolving the encounter.
**Why it happens:** Model doesn't call EndEncounter when adversaries are dead, or keeps attacking dead adversaries.
**How to avoid:** Use `IAutoFunctionInvocationFilter` to detect when all adversaries are dead and auto-end the encounter. Set a maximum iteration count on the combat agent loop.
**Warning signs:** Combat taking more than 20-30 tool calls.

### Pitfall 5: Context Snapshot Too Large for Token Window
**What goes wrong:** The composed system prompt (persona + stage instructions + full game state) exceeds the model's context window or wastes tokens.
**Why it happens:** Character state with full inventory, all encounters, all adversaries serialized into the prompt.
**How to avoid:** Keep the context snapshot focused on stage-relevant state only. In character-creation, only show character stats. In combat, only show the active encounter and participating character. Use narrative formatting, not JSON dumps.
**Warning signs:** Slow response times, model ignoring parts of the context.

### Pitfall 6: Resolution Stage Has No Clear Exit Condition
**What goes wrong:** The resolution stage doesn't auto-transition back to exploration because there's no single "completing" plugin call.
**Why it happens:** Resolution involves multiple narrative beats (loot distribution, injury narration) with no definitive end action.
**How to avoid:** Either (a) add a `CompleteResolution` wrapper function that advances the stage, or (b) use AdvanceTime as the trigger (time passes = return to exploration). Option (a) is cleaner and more explicit.
**Warning signs:** Sessions stuck in resolution stage.

## Code Examples

### Stage Enum and Transition Rules
```csharp
public enum SessionStage
{
    CharacterCreation,
    CampaignSetup,
    Exploration,
    Combat,
    Resolution,
    Ended
}

// Maps (stage, pluginName, functionName) -> next stage
public static class StageTransitions
{
    private static readonly Dictionary<(SessionStage, string, string), SessionStage> _transitions = new()
    {
        { (SessionStage.CharacterCreation, "Character", "CreateCharacter"), SessionStage.CampaignSetup },
        { (SessionStage.CampaignSetup, "Campaign", "StartCampaign"), SessionStage.Exploration },
        { (SessionStage.Exploration, "Encounter", "StartEncounter"), SessionStage.Combat },
        { (SessionStage.Combat, "Encounter", "EndEncounter"), SessionStage.Resolution },
        { (SessionStage.Resolution, "Resolution", "CompleteResolution"), SessionStage.Exploration },
        // "ended" is derived from domain state (death/apocalypse), not from a transition
    };

    public static SessionStage? GetNextStage(SessionStage current, string plugin, string function)
    {
        return _transitions.TryGetValue((current, plugin, function), out var next) ? next : null;
    }
}
```

### Stage-to-Function Mapping
```csharp
public sealed class StagePluginRegistry
{
    public IReadOnlyList<KernelFunction> GetFunctionsForStage(SessionStage stage, Kernel kernel)
    {
        return stage switch
        {
            SessionStage.CharacterCreation => GetFunctions(kernel, "Character", ["CreateCharacter"]),
            SessionStage.CampaignSetup => GetFunctions(kernel, "Campaign",
                ["CreateCampaign", "AddCharacterToCampaign", "StartCampaign"]),
            SessionStage.Exploration => GetFunctions(kernel,
                ("Encounter", ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"]),
                ("Campaign", ["AdvanceTime", "Rest"]),
                ("Character", ["ChallengeCharacter", "AddItemToCharacterInventory", "BuyItem", "CastScroll"]),
                ("Dice", ["Roll"])),
            SessionStage.Combat => GetFunctions(kernel,
                ("Encounter", ["AttackPlayer", "AttackAdversary", "EndEncounter"]),
                ("Dice", ["Roll"])),
            SessionStage.Resolution => GetFunctions(kernel,
                ("Character", ["AddItemToCharacterInventory", "RemoveItemFromCharacterInventory",
                    "InfectCharacter", "CureInfection", "ImproveCharacterAbility", "DegradeCharacterAbility"]),
                ("Campaign", ["AdvanceTime"]),
                ("Resolution", ["CompleteResolution"])),
            SessionStage.Ended => [],
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
    }

    private static List<KernelFunction> GetFunctions(
        Kernel kernel, params (string Plugin, string[] Functions)[] specs)
    {
        var result = new List<KernelFunction>();
        foreach (var (plugin, functions) in specs)
        {
            foreach (var func in functions)
            {
                result.Add(kernel.Plugins.GetFunction(plugin, func));
            }
        }
        return result;
    }
}
```

### Guardrail Error Messages (D-12)
```csharp
// In CharacterWrapperPlugin
[KernelFunction]
[Description("Create a new character with starting stats and gear")]
public async Task<CharacterDto> CreateCharacter(string name)
{
    if (sessionContext.CharacterId is not null)
        throw new InvalidOperationException(
            "A character already exists for this session. You cannot create another one.");

    var result = await inner.CreateCharacter(name);
    sessionContext.SetCharacterId(result.Id);
    return result;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `ToolCallBehavior` | `FunctionChoiceBehavior` | SK 1.x series (2024) | New API supports explicit function lists, filtering |
| Manual function call handling | `IAutoFunctionInvocationFilter` | SK 1.x filters (2024) | Cleaner interception with full context (chat history, iteration count) |
| Single monolithic agent | Multi-agent with `ChatCompletionAgent` | SK Agents Framework (2024-2025) | Each agent gets own kernel, plugins, instructions |
| Kernel.Clone() for agent isolation | Per-agent kernel via `new ChatCompletionAgent { Kernel = ... }` | SK 1.65.0 | Clean separation without cloning overhead |

## Open Questions

1. **Resolution stage exit trigger**
   - What we know: Resolution involves narrative aftermath, loot, injury consequences
   - What's unclear: Whether a dedicated `CompleteResolution` function is the right trigger, or if AdvanceTime should serve as the transition
   - Recommendation: Add a `CompleteResolution` wrapper function -- explicit is better than implicit

2. **Combat sub-agent chat history management**
   - What we know: Combat sub-agent needs its own chat thread to avoid polluting game master history
   - What's unclear: Whether combat narrative summary should be injected as a system message or user message into the game master's history
   - Recommendation: Inject as assistant message with combat narrative, so game master can reference it naturally

3. **Stage derivation vs. persistence for "resolution" stage**
   - What we know: Most stages can be derived from domain state. Resolution is tricky -- it's the gap between encounter ended and "player has moved on"
   - What's unclear: Whether to add a `NeedsResolution` flag to encounter or persist stage explicitly
   - Recommendation: Add a `IsResolved` boolean to Encounter entity. Resolution stage = last encounter is ended but not resolved. This keeps derivation pure.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | `WretchedWhispers.Tests/WretchedWhispers.Tests.csproj` |
| Quick run command | `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~WretchedWhispers.Tests" --no-build -q` |
| Full suite command | `dotnet test WrtechedWhispers/WrtechedWhispers.sln` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| MORK-01.SM-01 | Stage derived correctly from domain state (6 stages) | unit | `dotnet test --filter "FullyQualifiedName~StageDerivation" -x` | Wave 0 |
| MORK-01.SM-02 | Stage transitions fire on correct plugin calls | unit | `dotnet test --filter "FullyQualifiedName~StageTransition" -x` | Wave 0 |
| MORK-01.SM-03 | Function filtering returns correct functions per stage | unit | `dotnet test --filter "FullyQualifiedName~StagePluginRegistry" -x` | Wave 0 |
| MORK-01.WP-01 | Wrapper plugins auto-fill IDs from SessionContext | unit | `dotnet test --filter "FullyQualifiedName~WrapperPlugin" -x` | Wave 0 |
| MORK-01.WP-02 | Guardrails return corrective errors on invalid state | unit | `dotnet test --filter "FullyQualifiedName~Guardrail" -x` | Wave 0 |
| MORK-01.PC-01 | Prompt composition includes persona + stage + snapshot | unit | `dotnet test --filter "FullyQualifiedName~PromptCompos" -x` | Wave 0 |
| MORK-01.CB-01 | Combat sub-agent runs with combat-only tools | integration | manual (requires LLM) | manual-only |

### Sampling Rate
- **Per task commit:** `dotnet test WrtechedWhispers/WrtechedWhispers.sln --no-build -q`
- **Per wave merge:** `dotnet test WrtechedWhispers/WrtechedWhispers.sln`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs` -- covers MORK-01.SM-01
- [ ] `WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs` -- covers MORK-01.SM-02
- [ ] `WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs` -- covers MORK-01.SM-03
- [ ] `WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs` -- covers MORK-01.WP-01, WP-02
- [ ] `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs` -- covers MORK-01.PC-01

## Sources

### Primary (HIGH confidence)
- [Semantic Kernel Function Choice Behaviors](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/function-calling/function-choice-behaviors) - FunctionChoiceBehavior.Auto(functions: [...]) API, function filtering
- [Semantic Kernel Filters](https://learn.microsoft.com/en-us/semantic-kernel/concepts/enterprise-readiness/filters) - IAutoFunctionInvocationFilter for interception and termination
- [Configuring Agents with Plugins](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-functions) - Per-agent kernel, plugin import patterns
- [Semantic Kernel Function Calling](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/function-calling/) - Auto function calling flow, parameter handling

### Secondary (MEDIUM confidence)
- [Semantic Kernel Agent Orchestration](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-orchestration/) - Multi-agent patterns
- [GitHub Discussion #7600](https://github.com/microsoft/semantic-kernel/discussions/7600) - Community patterns for function exclusion

### Codebase (HIGH confidence)
- `GameSessionService.cs` -- Current orchestration, BuildKernelForSession(), CreateGameMasterAgent()
- `CharacterPlugin.cs`, `CampaignPlugin.cs`, `EncounterPlugin.cs`, `DicePlugin.cs` -- Existing plugin signatures
- `Campaign.cs` -- Domain model, IsStarted/IsEnded/IsActive patterns
- `ServiceCollectionExtensions.cs` -- DI registration patterns (Scoped for web API)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - using existing packages, verified API compatibility with SK 1.65.0
- Architecture: HIGH - patterns verified against official SK documentation, code examples from Microsoft Learn
- Pitfalls: MEDIUM - some based on general agent orchestration experience, combat loop risk is theoretical

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (stable -- SK 1.65.0 APIs are GA)
