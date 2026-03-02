# Feature Landscape

**Domain:** Web-based AI-powered text RPG (Mork Borg setting, LLM Game Master)
**Researched:** 2026-03-02
**Confidence:** MEDIUM (based on training data knowledge of AI Dungeon, NovelAI, KoboldAI, and similar products through early 2025; no live verification available)

## Competitive Context

The primary competitors and reference points in this space:
- **AI Dungeon** (Latitude) -- the original AI text adventure, now with custom scenarios, multiplayer, image generation, and memory systems
- **NovelAI** -- focused on creative writing with RPG modules, strong on customization and privacy
- **KoboldAI / KoboldCPP** -- open-source AI storytelling with local model support
- **Character.AI** -- conversational AI roleplay (not strictly RPG but overlapping audience)
- **LitRPG Adventures** -- D&D-focused AI content generator
- **Sillytavern** -- open-source chat frontend for AI roleplay

Wretched Whispers differentiates from ALL of these by having a **real rules engine**. AI Dungeon and NovelAI have no mechanical backing -- the LLM makes up rules as it goes. This project enforces Mork Borg rules through a typed domain, making the AI a narrator that must call game functions rather than freeform everything.

---

## Table Stakes

Features users expect. Missing = product feels incomplete or unusable.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Text input for player actions** | Core interaction loop -- player types what they want to do | Low | Already works in console; needs web text input |
| **Streamed LLM responses** | AI Dungeon established this pattern; text-that-appears feels alive | Medium | Listed in PROJECT.md requirements; SSE or WebSocket from backend |
| **Character sheet display** | Players need to see HP, abilities, inventory, status at all times | Medium | Data exists in domain; needs persistent sidebar/panel UI |
| **Session save/load** | Users expect to close browser and come back later | High | Currently in-memory only; needs SQLite persistence + session resume |
| **Character creation flow** | First thing every player does; domain already rolls stats | Medium | Already in domain; needs guided UI (show rolls, name input, equipment reveal) |
| **Combat narration with mechanical results** | Core gameplay loop; attacks, defense, damage must feel real | Low | Already implemented in domain + semantic plugins; needs web presentation |
| **Message history / conversation scrollback** | Users expect to scroll up and re-read narrative | Low | Standard chat UI pattern; must persist across sessions |
| **Basic authentication** | Multi-tenant requires knowing who the player is | Medium | PROJECT.md specifies email/password; standard JWT/cookie auth |
| **Game session list** | Players with multiple campaigns need to pick which to continue | Low | CRUD for campaigns already in domain; needs list UI |
| **Responsive text layout** | Text-heavy app must be readable on various screen sizes | Medium | Not mobile-app, but responsive web is minimum expectation |
| **Error handling for LLM failures** | LLM calls fail, timeout, or return garbage; user needs graceful recovery | Medium | Critical for production -- retry logic, user-facing error messages, no silent failures |
| **Loading/thinking indicators** | Users need to know the AI is working, not frozen | Low | Skeleton/typing indicator during LLM processing |

---

## Differentiators

Features that set the product apart. Not expected, but create significant value.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Mechanically enforced rules** | No other AI RPG enforces real TTRPG rules -- AI Dungeon/NovelAI let LLMs make up mechanics. This is THE differentiator. | Already built | Domain layer is the product's moat; surface it visually |
| **Visible dice rolls and mechanical outcomes** | Show "d20 + STR(+2) = 14 vs DR 12 -- HIT" alongside narrative. Makes the game feel real, not hallucinated. | Medium | Domain returns structured outcomes; render them as roll animations or formatted results |
| **Mork Borg aesthetic/theme** | Doom-metal visual design (dark, grimy, textured) matching the RPG's signature look. No other AI game does this. | Medium | CSS/design work; Mork Borg has a very distinctive yellow/black/pink aesthetic that fans recognize |
| **Character status injuries rendered visually** | Broken hand, lost eye, stabbed lung -- show these as persistent status effects on the character sheet. Unique to having a real domain. | Medium | Data already in Character model; needs iconography/status display |
| **Calendar of Nechrubel / Misery tracker** | Visual doom clock showing accumulated Miseries. Creates tension and is Mork Borg-specific. No competitor has this. | Low | Domain tracks Miseries; needs a visual tracker (7 slots, filling up) |
| **Campaign pacing controls** | Player chooses dawn dice (d100 slow to d2 fast). Unique to Mork Borg and gives players agency over session length. | Low | Already in domain (CampaignPlugin.CreateCampaign); needs UI during campaign setup |
| **Encounter reaction system** | NPCs can be friendly/hostile/unknown with rolled initial reactions, not just "everything attacks you." Adds tactical depth. | Low | Already in domain (InitialReaction); LLM narrates the result |
| **Multi-agent narrative** | Character biography agent and campaign lore agent create richer stories than single-LLM games. | Already built | Orchestration console already has 3 agents; expose benefits in web |
| **History summarization for long sessions** | Maintains narrative coherence across long campaigns without context window blowout. | Already built | ChatHistorySummarizationReducer already implemented |
| **Dark/atmospheric sound design** | Ambient doom-metal soundtrack or sound effects for dice rolls, combat, Misery triggers. Immersive. | Medium | Not in scope for MVP but strong differentiator; could add later |
| **Exportable session transcripts** | Download your campaign as a readable story. Players love sharing their adventures. | Low | Conversation history already exists; format as markdown/PDF |
| **"How did I die?" death summary** | When character dies, generate a narrative recap of their doomed journey. Mork Borg characters die often; make death meaningful. | Low | LLM can generate from history; character biography agent already exists |

---

## Anti-Features

Features to explicitly NOT build. These are tempting traps that would dilute the product or waste development time.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Freeform player-defined rules** | The ENTIRE differentiator is that rules are enforced mechanically. Letting players override the domain destroys the value proposition. | Keep the domain as law. The LLM interprets intent and calls domain functions. |
| **Real-time multiplayer** | Massive complexity for marginal value. Mork Borg works fine solo. PROJECT.md explicitly scopes this out. | Solo campaigns only. Each player has their own sessions. |
| **Image generation for scenes** | AI Dungeon has this, but it adds huge cost (API fees), latency, and is not core to a TEXT RPG. The Mork Borg aesthetic is better served by CSS/design. | Invest in strong typography, color, and CSS theming to evoke Mork Borg's visual style. |
| **Custom scenario editor / world builder** | AI Dungeon lets users create scenarios. This is a huge feature surface. Wretched Whispers should nail one game system perfectly first. | Focus on Mork Borg core rules. Published dungeons (Rotblack Sludge, etc.) come later. |
| **Voice input/output** | Adds speech-to-text and TTS complexity. Not what text RPG users expect or want. | Text is the medium. Keep it. |
| **Character class system (initially)** | Mork Borg classes (Fanged Deserter, etc.) add complexity. The classless system works for v1. | Ship with classless characters. Add classes as a later enhancement. |
| **Mobile app** | PROJECT.md scopes this out. Responsive web is sufficient. Native mobile is a different product. | Ensure responsive web design works on mobile browsers. |
| **Social features / profiles / leaderboards** | Solo RPG. Social features distract from the core loop of "sit down and play." | Maybe add exportable transcripts for sharing, but no social graph. |
| **LLM model selection** | KoboldAI/NovelAI let you pick models. This adds testing burden and inconsistent experiences. | Pick one model, tune it well, keep it consistent. Provider abstraction through SemanticKernel is fine for the backend, but don't expose model choice to users. |
| **Undo/rewind functionality** | AI Dungeon has "undo" and "retry." This undermines Mork Borg's philosophy of brutal consequences and permanent death. | Death is permanent. Choices matter. This IS the game. |

---

## Feature Dependencies

```
Authentication --> Session Management --> All gameplay features
                                     \-> Game Session List

Character Creation --> Campaign Creation --> Campaign Start --> Gameplay Loop
                                        \-> Character Sheet Display

Gameplay Loop = Text Input --> LLM Processing --> Streamed Response
                          \-> Domain Tool Calls --> Mechanical Results Display
                          \-> History Storage --> Session Resume

Session Management --> Save/Load --> History Summarization (for long sessions)

Streamed Responses --> Loading Indicators (complementary, not dependent)

Mork Borg Aesthetic --> Character Status Display (injuries)
                   \-> Misery Tracker
                   \-> Dice Roll Display

API Layer --> All frontend features (everything routes through the API)
```

### Critical Path (must build in order):
1. API layer exposing domain operations
2. Authentication (who is this player?)
3. Session persistence (SQLite, save game state + conversation)
4. Basic chat UI with streamed responses
5. Character creation UI flow
6. Campaign creation UI flow
7. Character sheet sidebar
8. Full gameplay loop (the LLM calls domain tools, results render in chat)

### Can Build in Parallel (after API layer exists):
- Mork Borg aesthetic/CSS theming
- Dice roll visualizations
- Misery tracker
- Character status/injury display
- Death summary generation

---

## MVP Recommendation

### Must Ship (Table Stakes):

1. **Authentication** (email/password) -- gate to everything
2. **Session management** (create, list, continue, save) -- players need persistence
3. **Character creation flow** -- first thing players do, must feel polished
4. **Chat interface with streamed LLM responses** -- the core interaction
5. **Character sheet display** -- players need to see their state
6. **Message history with scrollback** -- read what happened
7. **Error handling and loading states** -- production-readiness basics

### Ship with MVP as Differentiators:

8. **Visible dice rolls / mechanical outcomes** -- LOW additional effort (data exists), HIGH differentiation. Show the numbers next to the narrative.
9. **Misery tracker** -- LOW effort (7-slot visual counter), HIGH thematic value. The doom clock IS Mork Borg.
10. **Character injury/status indicators** -- MEDIUM effort, but the domain already tracks broken limbs, infections, etc. Surfacing this is unique.

### Defer:

- **Sound design** -- Nice but not critical. Add after core gameplay is solid.
- **Exportable transcripts** -- LOW effort but LOW urgency. Post-MVP.
- **Death summary** -- Cool feature, easy to add later, not blocking anything.
- **Multi-agent orchestration in web** -- The console has 3 agents (GM, biography, lore). Start with single agent for web (simpler), add orchestration after the basic loop works.
- **Campaign pacing UI** -- The domain supports it, but the LLM can handle this in conversation for v1. Dedicated UI later.

---

## Mork Borg-Specific Feature Notes

### What Makes Mork Borg Different from Generic Fantasy

The feature set should reflect these aspects of the game system:

1. **Characters are fragile and disposable** -- HP is low (often 1-8), death comes fast. The UI should make character creation quick and death impactful but not punishing (easy to roll a new character).

2. **The world is ending on a timer** -- The Calendar of Nechrubel is not optional flavor, it IS the game. Every dawn roll matters. The Misery tracker must be prominent.

3. **Equipment degrades** -- Armor degrades on critical hits, weapons break on fumbles. The character sheet must show equipment condition.

4. **Scarcity is core** -- Food days, silver, limited inventory. The inventory display matters more than in most RPGs.

5. **Infection is a death sentence** -- Once infected, resting deals damage instead of healing. This status needs to be visually alarming.

6. **No classes in base game** -- Character identity comes from equipment and accumulated scars, not class abilities. The "Broken" status effects (lost eye, severed arm, etc.) ARE the character progression.

7. **Simplicity is a feature** -- Mork Borg intentionally has few rules. The domain is small. Don't add complexity the system doesn't have.

---

## Competitor Feature Matrix

| Feature | AI Dungeon | NovelAI | KoboldAI | Wretched Whispers (planned) |
|---------|-----------|---------|----------|---------------------------|
| Freeform text input | Yes | Yes | Yes | Yes |
| Streamed responses | Yes | Yes | Yes | Yes |
| Session persistence | Yes | Yes | Local | Yes |
| Rules enforcement | No | No | No | **Yes (domain-backed)** |
| Visible mechanics | No | No | No | **Yes (dice, HP, DR)** |
| Character sheet | Basic | No | No | **Yes (full Mork Borg)** |
| Image generation | Yes | Yes | Via ext. | No (by design) |
| Custom scenarios | Yes | Yes | Yes | No (Mork Borg only) |
| Multiplayer | Yes | No | No | No |
| Voice | Partial | No | No | No |
| Model selection | Yes | Yes | Yes | No (by design) |
| Undo/retry | Yes | Yes | Yes | **No (by design)** |
| Free tier | Limited | No | Open source | TBD |
| Doom clock / tension mechanic | No | No | No | **Yes (Calendar of Nechrubel)** |

---

## Sources and Confidence Notes

- **AI Dungeon features:** Based on training data knowledge through early 2025. AI Dungeon has been through multiple iterations (GPT-2 era through GPT-4). Features like Adventures, Scenarios, multiplayer, image generation, and memory were established by 2024. MEDIUM confidence -- specific UI details may have changed.
- **NovelAI features:** Based on training data knowledge. Known for Clio/Kayra models, Lorebook system, custom modules, and image generation. MEDIUM confidence.
- **KoboldAI/KoboldCPP:** Open-source project. Feature set well-documented in training data. HIGH confidence on core capabilities.
- **Mork Borg rules:** Based on published game system knowledge. Character mechanics, Calendar of Nechrubel, combat rules are well-documented and match the domain code reviewed. HIGH confidence.
- **Domain capabilities:** Verified by reading actual source code in the repository. HIGH confidence.
- **General AI text RPG patterns:** Based on extensive training data about the genre. MEDIUM-HIGH confidence on user expectations and standard features.

**Gap:** Could not verify latest 2025-2026 features of AI Dungeon or NovelAI via live sources. Competitor feature matrix may be slightly outdated. The core analysis of what makes Wretched Whispers different (rules enforcement) remains valid regardless of competitor updates.
