---
phase: 03-api-layer-and-streaming
plan: 01
subsystem: api
tags: [minimal-api, opentelemetry, semantic-kernel, session-crud, multi-tenant, asp-net-core]

# Dependency graph
requires:
  - phase: 02-authentication-and-multi-tenancy
    provides: "Identity API endpoints, multi-tenant campaign repository, bearer token auth"
provides:
  - "POST /sessions endpoint for creating new game sessions"
  - "GET /sessions endpoint with rich session previews (character name, HP, status)"
  - "GET /sessions/{id} endpoint for session detail with paginated chat history"
  - "GET /sessions/{id}/messages endpoint for standalone paginated messages"
  - "OpenTelemetry tracing/metrics for ASP.NET Core and Semantic Kernel"
  - "AzureOpenAI and GameSession configuration sections"
affects: [03-02-streaming-action-endpoint, 04-frontend]

# Tech tracking
tech-stack:
  added: [Microsoft.SemanticKernel 1.65.0, Microsoft.SemanticKernel.Agents.Core 1.65.0, Microsoft.SemanticKernel.Connectors.AzureOpenAI 1.65.0, OpenTelemetry.Extensions.Hosting 1.15.0, OpenTelemetry.Instrumentation.AspNetCore 1.15.0, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0, OpenTelemetry.Exporter.Console 1.13.1, Microsoft.Extensions.Resilience]
  patterns: [minimal-api-endpoint-groups, dto-record-types, query-param-pagination]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/CreateSessionResponse.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/SessionPreviewDto.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/SessionDetailDto.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/ChatMessageDto.cs
    - WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Sessions/SessionEndpointTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj
    - WrtechedWhispers/WretchedWhispers.Api/Program.cs
    - WrtechedWhispers/WretchedWhispers.Api/appsettings.json

key-decisions:
  - "Session ID = Campaign ID (1:1 mapping) for simplicity, matching locked architecture decision"
  - "Status derived from domain state: no characters = character-creation, IsActive = in-progress, else = ended"
  - "OTel sensitive diagnostics enabled for SK via AppContext switch for development tracing"

patterns-established:
  - "Minimal API endpoint groups: static class with MapXxxEndpoints extension method, RequireAuthorization on group level"
  - "DTO records: immutable records in Models/ namespace for API contracts"
  - "Ownership verification: load user campaigns and check if requested ID is in the list (returns 404, not 403)"

requirements-completed: [SESS-01, SESS-02, SESS-03, INFR-04]

# Metrics
duration: 6min
completed: 2026-03-03
---

# Phase 3 Plan 1: Session CRUD and OTel Summary

**Minimal API session endpoints (create/list/resume/messages) with multi-tenant isolation, OTel tracing for ASP.NET Core + Semantic Kernel, and 8 integration tests**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-03T14:17:32Z
- **Completed:** 2026-03-03T14:24:31Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments
- Session CRUD endpoints with bearer token auth and multi-tenant ownership verification
- Rich session previews with character name, HP, campaign status, and last played
- OpenTelemetry tracing and metrics wired for both ASP.NET Core and Semantic Kernel activity sources
- 8 integration tests verifying auth, CRUD operations, multi-tenant isolation, and pagination

## Task Commits

Each task was committed atomically:

1. **Task 1: Install packages, add project reference, configure OTel and settings** - `cc227ea` (chore)
2. **Task 2: Create DTOs and session CRUD endpoints** - `f63b3b5` (feat)
3. **Task 3: Integration tests for session CRUD endpoints** - `351b528` (test)

## Files Created/Modified
- `WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs` - OTel extension method with ASP.NET Core + SK tracing, metrics, and structured logging
- `WretchedWhispers.Api/Models/CreateSessionResponse.cs` - Response DTO for POST /sessions
- `WretchedWhispers.Api/Models/SessionPreviewDto.cs` - Rich preview DTO with character name, HP, status, description, lastPlayed
- `WretchedWhispers.Api/Models/SessionDetailDto.cs` - Session resume DTO with campaign state and paginated messages
- `WretchedWhispers.Api/Models/ChatMessageDto.cs` - Chat message DTO with role, content, authorName
- `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` - Four endpoint handlers with ownership verification and pagination
- `WretchedWhispers.Tests/Sessions/SessionEndpointTests.cs` - 8 integration tests with WebApplicationFactory
- `WretchedWhispers.Api/WretchedWhispers.Api.csproj` - Added Semantic project reference + 8 NuGet packages
- `WretchedWhispers.Api/Program.cs` - Wired OTel and session endpoints
- `WretchedWhispers.Api/appsettings.json` - Added AzureOpenAI and GameSession config sections

## Decisions Made
- Session ID equals Campaign ID (1:1 mapping) per the locked architecture decision from planning
- Status is derived from domain state rather than stored: `Players.Count == 0` -> character-creation, `IsActive()` -> in-progress, else -> ended
- OTel sensitive diagnostics enabled via AppContext switch for development-time SK tracing visibility
- Ownership verification returns 404 (not 403) for sessions belonging to other users, avoiding information leakage

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None

## User Setup Required

None - no external service configuration required. AzureOpenAI settings in appsettings.json are placeholders to be populated via user secrets or environment variables when the streaming endpoint (Plan 02) is implemented.

## Next Phase Readiness
- Session CRUD foundation complete, ready for Plan 02 (streaming action endpoint with SSE)
- All endpoint patterns established: endpoint groups, DTOs, auth, ownership checks, pagination
- OTel infrastructure ready to capture SK agent traces when AI integration begins
- 230 total tests pass (222 existing + 8 new)

---
*Phase: 03-api-layer-and-streaming*
*Completed: 2026-03-03*

## Self-Check: PASSED
- All 8 created files verified on disk
- All 3 task commits verified in git history (cc227ea, f63b3b5, 351b528)
