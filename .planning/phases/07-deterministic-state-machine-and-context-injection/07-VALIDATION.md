---
phase: 7
slug: deterministic-state-machine-and-context-injection
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-24
---

# Phase 7 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.3 + Moq 4.20.72 |
| **Config file** | `WretchedWhispers.Tests/WretchedWhispers.Tests.csproj` |
| **Quick run command** | `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~WretchedWhispers.Tests" --no-build -q` |
| **Full suite command** | `dotnet test WrtechedWhispers/WrtechedWhispers.sln` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test WrtechedWhispers/WrtechedWhispers.sln --no-build -q`
- **After every plan wave:** Run `dotnet test WrtechedWhispers/WrtechedWhispers.sln`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 07-01-01 | 01 | 1 | MORK-01.SM-01 | unit | `dotnet test --filter "FullyQualifiedName~StageDerivation"` | ❌ W0 | ⬜ pending |
| 07-01-02 | 01 | 1 | MORK-01.SM-02 | unit | `dotnet test --filter "FullyQualifiedName~StageTransition"` | ❌ W0 | ⬜ pending |
| 07-01-03 | 01 | 1 | MORK-01.SM-03 | unit | `dotnet test --filter "FullyQualifiedName~StagePluginRegistry"` | ❌ W0 | ⬜ pending |
| 07-02-01 | 02 | 1 | MORK-01.WP-01 | unit | `dotnet test --filter "FullyQualifiedName~WrapperPlugin"` | ❌ W0 | ⬜ pending |
| 07-02-02 | 02 | 1 | MORK-01.WP-02 | unit | `dotnet test --filter "FullyQualifiedName~Guardrail"` | ❌ W0 | ⬜ pending |
| 07-03-01 | 03 | 2 | MORK-01.PC-01 | unit | `dotnet test --filter "FullyQualifiedName~PromptCompos"` | ❌ W0 | ⬜ pending |
| 07-04-01 | 04 | 3 | MORK-01.CB-01 | integration | manual (requires LLM) | manual-only | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs` — stubs for MORK-01.SM-01
- [ ] `WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs` — stubs for MORK-01.SM-02
- [ ] `WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs` — stubs for MORK-01.SM-03
- [ ] `WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs` — stubs for MORK-01.WP-01, WP-02
- [ ] `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs` — stubs for MORK-01.PC-01

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Combat sub-agent runs with combat-only tools | MORK-01.CB-01 | Requires LLM interaction to verify agent behavior | 1. Start session, reach combat stage 2. Verify combat agent receives only combat plugins 3. Verify narrative result returns to game master |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
