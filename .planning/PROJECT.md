# Wretched Whispers

## What This Is

A web-based text RPG built on the Mork Borg open-source roleplaying system. An LLM serves as Game Master — narrating the world, running encounters, and responding to player actions — while a strongly typed C# domain enforces all game rules through DDD. Players log in, manage their game sessions, and play through complete Mork Borg campaigns from character creation to the 7th Misery destroying the world.

## Core Value

A player can sit down and play through a complete Mork Borg session — character creation to world's end — with an AI Game Master that feels like playing with a friend, while the domain guarantees the rules are always correct.

## Requirements

### Validated

- [x] Persistent game state (character sheet, conversation history, world state) — Validated in Phase 1
- [x] SQLite storage for simple deployment — Validated in Phase 1
- [x] Email/password authentication — Validated in Phase 2
- [x] Multi-tenant support (multiple players, each with their own games) — Validated in Phase 2
- [x] API layer exposing game operations (.NET backend) — Validated in Phase 3
- [x] Streamed LLM narrator responses (words appear as generated) — Validated in Phase 3
- [x] Game session management (create new, list existing, continue saved) — Validated in Phase 4
- [x] Web-based UI for gameplay (React/Next.js frontend) — Validated in Phase 5

### Active

- [ ] Full Mork Borg session lifecycle (character creation through 7th Misery)
- [ ] Core Mork Borg rules: classes, combat, items, Miseries calendar

### Out of Scope

- OAuth/social login — email/password is sufficient for v1
- Published dungeons (Rotblack Sludge, etc.) — core rules only first
- Third-party/community content — focus on official core rules
- Mobile app — web-first
- Real-time multiplayer (shared sessions) — each player plays solo campaigns
- Cloud database (PostgreSQL, etc.) — SQLite keeps deployment simple, upgrade later

## Context

- **Existing prototype:** Working console application with DDD domain and SemanticKernel LLM layer connected. Domain models character creation, combat, powers, weapons, and the Miseries calendar. LLM uses tools to invoke domain operations and narrate results.
- **Mork Borg system:** A doom-metal fantasy RPG with intentionally simple mechanics. Characters are fragile, the world is ending, and the 7 Miseries are a ticking clock toward apocalypse. The simplicity of the rules makes it well-suited to domain modeling.
- **Architecture pattern:** The LLM (via SemanticKernel) acts as orchestrator — it receives player input, decides what game actions to take, calls domain tools (attack, use power, check inventory, roll misery, etc.), and narrates the results back. The domain is the source of truth for all mechanical state.
- **History summarization:** Already implemented to manage context window and preserve narrative continuity across long sessions.
- **OpenTelemetry:** Already added to the console project for observability.

## Constraints

- **Tech stack**: .NET backend (C#, SemanticKernel) + React/Next.js frontend — existing domain code must be preserved and extended, not rewritten
- **Deployment**: Must be self-contained (SQLite, no external database provisioning required)
- **Licensing**: Mork Borg uses a third-party license — content must stay within what the license permits

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| DDD for game mechanics | Rules must be deterministic and testable, not left to LLM interpretation | — Pending |
| LLM as Game Master via tool use | Separates narrative creativity from mechanical correctness | — Pending |
| SemanticKernel for LLM integration | .NET-native, supports tool calling, streaming, multiple providers | — Pending |
| SQLite for initial persistence | Zero-config deployment, upgrade to PostgreSQL later when needed | — Pending |
| React/Next.js for frontend | Separation from .NET backend, rich ecosystem for interactive text UIs | — Pending |
| Streamed responses | Text appearing word-by-word feels alive and matches the GM-at-the-table experience | — Pending |

---
*Last updated: 2026-03-24 after Phase 5 completion*
