# Roadmap: Wretched Whispers

## Overview

Wretched Whispers transforms an existing console-based Mork Borg prototype into a web application where players create characters, explore a doomed world, and face the 7 Miseries -- all narrated by an AI Game Master while the domain enforces every rule. The roadmap moves from persistence foundation through authentication, API/streaming, frontend character creation, core gameplay, and finally mechanical visibility -- each phase delivering a coherent, verifiable capability that builds on the last.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Persistence Foundation** - Replace in-memory repositories with SQLite/EF Core persistence for all domain aggregates and chat history
- [ ] **Phase 2: Authentication and Multi-Tenancy** - User accounts with email/password login and session isolation between players
- [ ] **Phase 3: API Layer and Streaming** - REST endpoints for session management and SSE streaming bridge for LLM gameplay responses
- [ ] **Phase 4: Frontend Foundation and Character Creation** - React/Next.js app with Mork Borg aesthetic and guided character creation as first playable experience
- [ ] **Phase 5: Core Gameplay Interface** - Chat-based gameplay with streaming narrator responses, message history, and character sheet display
- [ ] **Phase 6: Mechanical Visibility and Session Lifecycle** - Dice rolls, Misery tracker, injury indicators, and complete game lifecycle from creation through doom

## Phase Details

### Phase 1: Persistence Foundation
**Goal**: All domain state and conversation history survives application restarts and can be loaded back with full fidelity
**Depends on**: Nothing (first phase)
**Requirements**: INFR-02
**Success Criteria** (what must be TRUE):
  1. Character aggregate round-trips through save/load without losing any state (HP, inventory, abilities, armor, broken limbs, infections)
  2. Campaign and Encounter aggregates persist and reload with correct relationships
  3. Chat history persists alongside domain state and loads back into SemanticKernel ChatHistory
  4. Existing console application works against SQLite storage instead of in-memory repositories
**Plans**: 2 plans

Plans:
- [x] 01-01-PLAN.md — EF Core setup, entity wrappers, DbContext, JSON serialization, aggregate SQLite repositories, round-trip tests
- [x] 01-02-PLAN.md — Chat history persistence, DI wiring, Settings, migrations, console app switchover, in-memory removal

### Phase 01.1: Domain Design Improvements (INSERTED)

**Goal:** Fix critical bugs and structural design issues in the domain layer (WretchedWhispers.Core) before building features on top of it — inverted scroll restriction, public Encounter setters, Character god-class decomposition, injury flag consolidation, aggregate boundary enforcement, and entity sealing
**Requirements**: INTERNAL-REFACTOR
**Depends on:** Phase 1
**Plans:** 3/3 plans complete

Plans:
- [x] 01.1-01-PLAN.md — Fix critical bugs (scroll restriction, Encounter setters), create InjuryKind/InjurySet foundation types, seal all domain entities
- [x] 01.1-02-PLAN.md — Character decomposition: migrate injury booleans to InjurySet, ArmorTier strategy delegation, Inventory to sealed class, Abilities immutability
- [x] 01.1-03-PLAN.md — Aggregate boundary enforcement (CharacterPlugin routing), serialization test updates, full round-trip verification

### Phase 2: Authentication and Multi-Tenancy
**Goal**: Players can create accounts, log in, and have their game sessions isolated from other players
**Depends on**: Phase 1
**Requirements**: AUTH-01, AUTH-02, AUTH-03, INFR-03
**Success Criteria** (what must be TRUE):
  1. User can sign up with email and password and receive confirmation of account creation
  2. User can log in and receive a JWT token that authenticates subsequent API requests
  3. User session persists across browser refresh (token stored and reused)
  4. User cannot access or see another user's game sessions
**Plans**: 2 plans

Plans:
- [x] 02-01-PLAN.md — Identity infrastructure: DbContext to IdentityUserContext, CampaignEntity UserId FK, Identity EF Core package, combined migration
- [ ] 02-02-PLAN.md — Web API project with Identity auth endpoints, multi-tenant campaign repository, integration tests for auth flow and tenant isolation

### Phase 3: API Layer and Streaming
**Goal**: Backend exposes all game operations over HTTP with real-time streaming of LLM narrator responses
**Depends on**: Phase 2
**Requirements**: INFR-01, INFR-04, SESS-01, SESS-02, SESS-03, SESS-04, GAME-06
**Success Criteria** (what must be TRUE):
  1. Authenticated user can create a new game session via API and receive a session ID
  2. User can list their existing game sessions with enough info to choose which to continue
  3. User can resume a saved session and the game state matches where they left off (character, inventory, encounter, narrative context)
  4. LLM narrator responses stream as SSE events that a client can consume token-by-token
  5. When LLM fails or times out, the API returns a structured error and game state remains consistent (no half-applied actions)
**Plans**: TBD

Plans:
- [ ] 03-01: TBD
- [ ] 03-02: TBD
- [ ] 03-03: TBD

### Phase 4: Frontend Foundation and Character Creation
**Goal**: Players open the web app, see the Mork Borg aesthetic, and create a character through a guided narrator conversation
**Depends on**: Phase 3
**Requirements**: CHAR-01, GAME-05, UI-01, UI-02, UI-03
**Success Criteria** (what must be TRUE):
  1. Web app loads with dark doom-metal visual aesthetic (yellow/black/pink palette, appropriate typography and textures)
  2. Layout is readable and functional on both desktop and tablet screen sizes
  3. User can create a character through an interactive narrator-guided conversation that reveals stats, name, and equipment
  4. Loading/thinking indicator is visible while the LLM processes during character creation
**Plans**: TBD

Plans:
- [ ] 04-01: TBD
- [ ] 04-02: TBD

### Phase 5: Core Gameplay Interface
**Goal**: Players can play the game -- type actions, see streaming narrator responses, review history, and monitor their character's state
**Depends on**: Phase 4
**Requirements**: GAME-01, GAME-02, GAME-03, GAME-04, CHAR-02
**Success Criteria** (what must be TRUE):
  1. User can type free-text actions describing what their character does and submit them to the narrator
  2. Narrator responses appear word-by-word as the LLM generates them (streaming typewriter effect)
  3. User can scroll back through the full message history of the current session
  4. Message history persists -- closing the browser and resuming the session shows all previous messages
  5. Character sheet sidebar displays current HP, abilities, inventory, and armor in real time as the game progresses
**Plans**: TBD

Plans:
- [ ] 05-01: TBD
- [ ] 05-02: TBD

### Phase 6: Mechanical Visibility and Session Lifecycle
**Goal**: Players can see the real dice rolls and mechanical outcomes behind the narrative, track the world's doom, monitor their character's physical state, and play through a complete Mork Borg session from creation to death or apocalypse
**Depends on**: Phase 5
**Requirements**: MORK-01, MORK-02, MORK-03, CHAR-03, CHAR-04
**Success Criteria** (what must be TRUE):
  1. Dice rolls and mechanical outcomes display alongside narrative text (e.g., "d20+STR(+2)=14 vs DR 12 -- HIT")
  2. Misery tracker displays a visual 7-slot doom clock showing how many Miseries have occurred
  3. Character injuries and status effects are visually indicated (broken limbs, infection, severed parts)
  4. Equipment condition is visible (armor degradation, weapon state)
  5. A player can experience a complete session lifecycle -- character creation, exploration, combat, Misery events -- ending in character death or the 7th Misery destroying the world

**Plans**: TBD

Plans:
- [ ] 06-01: TBD
- [ ] 06-02: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Persistence Foundation | 2/2 | Complete | 2026-03-02 |
| 1.1. Domain Design Improvements | 3/3 | Complete | 2026-03-03 |
| 2. Authentication and Multi-Tenancy | 1/2 | In Progress | - |
| 3. API Layer and Streaming | 0/0 | Not started | - |
| 4. Frontend Foundation and Character Creation | 0/0 | Not started | - |
| 5. Core Gameplay Interface | 0/0 | Not started | - |
| 6. Mechanical Visibility and Session Lifecycle | 0/0 | Not started | - |
