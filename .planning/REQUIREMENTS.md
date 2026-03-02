# Requirements: Wretched Whispers

**Defined:** 2026-03-02
**Core Value:** A player can sit down and play through a complete Mork Borg session — character creation to world's end — with an AI Game Master that feels like playing with a friend, while the domain guarantees the rules are always correct.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Authentication

- [ ] **AUTH-01**: User can sign up with email and password
- [ ] **AUTH-02**: User can log in and receive JWT authentication token
- [ ] **AUTH-03**: User session persists across browser refresh

### Session Management

- [ ] **SESS-01**: User can create a new game session
- [ ] **SESS-02**: User can view list of their existing game sessions
- [ ] **SESS-03**: User can continue a saved game session from where they left off
- [ ] **SESS-04**: Game state auto-saves after each player action

### Gameplay

- [ ] **GAME-01**: User can type free-text actions for their character
- [ ] **GAME-02**: Narrator responses stream word-by-word as generated
- [ ] **GAME-03**: User can scroll back through message history
- [ ] **GAME-04**: Message history persists across sessions
- [ ] **GAME-05**: Loading/thinking indicator shows while LLM is processing
- [ ] **GAME-06**: Graceful error recovery when LLM fails or times out

### Character

- [ ] **CHAR-01**: User creates character through guided narrator conversation
- [ ] **CHAR-02**: Character sheet sidebar displays HP, abilities, inventory, armor
- [ ] **CHAR-03**: Visual injury/status indicators (broken limbs, infection, severed parts)
- [ ] **CHAR-04**: Equipment condition visible (armor degradation, weapon state)

### Mork Borg Mechanics

- [ ] **MORK-01**: Full session lifecycle from character creation through 7th Misery or death
- [ ] **MORK-02**: Visual Misery tracker showing doom clock progress (7 slots)
- [ ] **MORK-03**: Visible dice rolls and mechanical outcomes alongside narrative

### UI & Aesthetic

- [ ] **UI-01**: Responsive layout readable on desktop and tablet
- [ ] **UI-02**: Dark theme suitable for grim game atmosphere
- [ ] **UI-03**: Mork Borg doom-metal visual aesthetic (yellow/black/pink palette, textures)

### Infrastructure

- [ ] **INFR-01**: .NET API layer with SSE streaming for LLM responses
- [x] **INFR-02**: SQLite persistence for all game state (character, chat history, world state)
- [ ] **INFR-03**: Multi-tenant session isolation (each player's games are private)
- [ ] **INFR-04**: OpenTelemetry observability for API and LLM calls

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Session Management

- **SESS-05**: Quick character re-roll after death (fast restart for Mork Borg's high lethality)

### Gameplay

- **GAME-07**: Exportable session transcripts (download campaign as readable story)
- **GAME-08**: Death summary narrative (recap of doomed journey when character dies)

### Mork Borg Mechanics

- **MORK-04**: Campaign pacing controls (player chooses dawn dice speed)
- **MORK-05**: Multi-agent narrative (biography agent, lore agent alongside GM)

### UI & Aesthetic

- **UI-04**: Ambient doom-metal sound design and sound effects

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| OAuth/social login | Email/password sufficient for v1; adds external dependencies |
| Real-time multiplayer (shared sessions) | Massive complexity; Mork Borg works fine solo |
| Image generation for scenes | High cost, high latency, not core to a text RPG |
| Custom scenario editor / world builder | Focus on one game system first |
| Voice input/output | Text is the medium; adds speech complexity |
| Published dungeons (Rotblack Sludge, etc.) | Core rules only for v1 |
| Third-party/community content | Focus on official core rules |
| Mobile app | Responsive web is sufficient; native mobile is a different product |
| Undo/retry functionality | Violates Mork Borg philosophy of brutal consequences and permanent death |
| LLM model selection | One model, tuned well; consistent experience over choice |
| Cloud database (PostgreSQL, etc.) | SQLite keeps deployment simple; upgrade path exists |
| Character class system (advanced) | Classless characters work for v1; add Fanged Deserter etc. later |
| Social features / profiles / leaderboards | Solo RPG; social features distract from core loop |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| AUTH-01 | Phase 2 | Pending |
| AUTH-02 | Phase 2 | Pending |
| AUTH-03 | Phase 2 | Pending |
| SESS-01 | Phase 3 | Pending |
| SESS-02 | Phase 3 | Pending |
| SESS-03 | Phase 3 | Pending |
| SESS-04 | Phase 3 | Pending |
| GAME-01 | Phase 5 | Pending |
| GAME-02 | Phase 5 | Pending |
| GAME-03 | Phase 5 | Pending |
| GAME-04 | Phase 5 | Pending |
| GAME-05 | Phase 4 | Pending |
| GAME-06 | Phase 3 | Pending |
| CHAR-01 | Phase 4 | Pending |
| CHAR-02 | Phase 5 | Pending |
| CHAR-03 | Phase 6 | Pending |
| CHAR-04 | Phase 6 | Pending |
| MORK-01 | Phase 6 | Pending |
| MORK-02 | Phase 6 | Pending |
| MORK-03 | Phase 6 | Pending |
| UI-01 | Phase 4 | Pending |
| UI-02 | Phase 4 | Pending |
| UI-03 | Phase 4 | Pending |
| INFR-01 | Phase 3 | Pending |
| INFR-02 | Phase 1 | Complete |
| INFR-03 | Phase 2 | Pending |
| INFR-04 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 27 total
- Mapped to phases: 27
- Unmapped: 0

---
*Requirements defined: 2026-03-02*
*Last updated: 2026-03-02 after roadmap creation*
