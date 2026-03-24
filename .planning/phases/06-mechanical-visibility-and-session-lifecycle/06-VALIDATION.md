---
phase: 6
slug: mechanical-visibility-and-session-lifecycle
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-24
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (backend), no frontend test framework detected |
| **Config file** | WrtechedWhispers/WrtechedWhispers.Tests/WrtechedWhispers.Tests.csproj |
| **Quick run command** | `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~Tests" -v q` |
| **Full suite command** | `dotnet test WrtechedWhispers/WrtechedWhispers.sln` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~Tests" -v q`
- **After every plan wave:** Run `dotnet test WrtechedWhispers/WrtechedWhispers.sln`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 06-01-01 | 01 | 1 | MORK-03 | unit | `dotnet test --filter "DicePlugin"` | ❌ W0 | ⬜ pending |
| 06-01-02 | 01 | 1 | CHAR-03, CHAR-04 | unit | `dotnet test --filter "StateUpdateEvent"` | ❌ W0 | ⬜ pending |
| 06-01-03 | 01 | 1 | MORK-01 | integration | `dotnet test --filter "DeriveStatus"` | ❌ W0 | ⬜ pending |
| 06-02-01 | 02 | 2 | MORK-02 | manual | Visual verification of MiseryTracker | N/A | ⬜ pending |
| 06-02-02 | 02 | 2 | CHAR-03 | manual | Visual verification of injury badges | N/A | ⬜ pending |
| 06-02-03 | 02 | 2 | CHAR-04 | manual | Visual verification of armor tier | N/A | ⬜ pending |
| 06-03-01 | 03 | 2 | MORK-01 | manual | End card displays on death/apocalypse | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] DicePlugin structured return test — verify `DiceRollResult` serialization and LLM compatibility
- [ ] StateUpdateEvent enrichment test — verify injury/equipment/status fields included in SSE payload
- [ ] DeriveStatus test — verify session returns "ended" when character dead or world ended

*Existing xUnit infrastructure covers backend; frontend has no automated test framework.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| MiseryTracker 7-pip display | MORK-02 | Visual/animation component, no frontend test framework | Load game session, verify 7 pips visible in header, trigger misery event, verify pip fills pink with pulse |
| Injury badges in drawer | CHAR-03 | Visual component in character drawer | Open character drawer during combat, verify injury icons appear in pink when injured |
| Equipment condition display | CHAR-04 | Visual component extending EquipmentSlot | View armor in drawer, verify tier label visible; break shield, verify struck-through display |
| End card overlay | MORK-01 | Full session lifecycle visual verification | Play until character death, verify end card shows "YOUR WRETCH HAS FALLEN" with "Begin Anew" button |
| Read-only ended sessions | MORK-01 | Session list and chat interaction | Open ended session from list, verify chat input disabled, end card shown, history scrollable |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
