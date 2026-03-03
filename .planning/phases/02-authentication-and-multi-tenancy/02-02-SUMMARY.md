---
phase: 02-authentication-and-multi-tenancy
plan: 02
subsystem: auth
tags: [aspnet-identity, bearer-token, web-api, multi-tenancy, integration-tests, webapplicationfactory]

# Dependency graph
requires:
  - phase: 02-authentication-and-multi-tenancy
    plan: 01
    provides: "IdentityUserContext-based DbContext with CampaignEntity.UserId FK"
provides:
  - "Web API project with ASP.NET Identity register/login/refresh endpoints at /auth"
  - "Bearer token auth with 60min access + 14-day refresh token expiration"
  - "Multi-tenant campaign repository with GetForUser and SaveCampaign(campaign, userId)"
  - "Integration tests proving full auth flow and tenant isolation"
affects: [03-api-and-streaming, 04-frontend]

# Tech tracking
tech-stack:
  added: [Microsoft.AspNetCore.Mvc.Testing 9.0.*]
  patterns: [WebApplicationFactory integration testing, Identity API endpoints, bearer token configuration]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Program.cs
    - WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj
    - WrtechedWhispers/WretchedWhispers.Api/appsettings.json
    - WrtechedWhispers/WretchedWhispers.Api/appsettings.Development.json
    - WrtechedWhispers/WretchedWhispers.Tests/Auth/AuthEndpointTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/CampaignMultiTenancyTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Core/Campaigns/ICampaignsRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteCampaignsRepository.cs
    - WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj
    - WrtechedWhispers/WrtechedWhispers.sln

key-decisions:
  - "BearerTokenOptions configured via Configure<BearerTokenOptions>(IdentityConstants.BearerScheme) requiring explicit using for Microsoft.AspNetCore.Authentication.BearerToken"
  - "WebApplicationFactory with in-memory SQLite and EnsureCreated for auth integration tests"
  - "Partial Program class declaration for WebApplicationFactory accessibility from test project"

patterns-established:
  - "Identity API endpoints mapped under /auth prefix group"
  - "WebApplicationFactory-based integration testing with in-memory SQLite DB replacement"
  - "Repository dual-interface: backward-compatible parameterless methods plus userId-scoped overloads"

requirements-completed: [AUTH-01, AUTH-02, AUTH-03, INFR-03]

# Metrics
duration: 8min
completed: 2026-03-03
---

# Phase 02 Plan 02: Auth Endpoints and Multi-Tenant Repository Summary

**ASP.NET Identity API endpoints at /auth with bearer token auth (60min/14day), userId-scoped campaign repository, and 8 integration tests proving auth flow and tenant isolation**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-03T12:45:39Z
- **Completed:** 2026-03-03T12:53:57Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments
- Created Web API project with Identity register/login/refresh endpoints at /auth prefix
- Configured bearer token auth with 60-minute access tokens and 14-day refresh tokens
- Added multi-tenant campaign repository methods (GetForUser, SaveCampaign with userId) while preserving backward compatibility
- Built integration test suite proving full auth flow (register, login, token validation, 401 rejection) and campaign tenant isolation
- All 222 tests pass (214 existing + 8 new)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create Web API project with Identity auth endpoints** - `24507c0` (feat)
2. **Task 2: Update campaign repository for multi-tenant user filtering** - `79659f8` (feat)
3. **Task 3: Integration tests for auth flow and multi-tenant isolation** - `2596411` (test)

## Files Created/Modified
- `WrtechedWhispers/WretchedWhispers.Api/Program.cs` - Web API host with Identity endpoints, bearer token config, /health and /auth/me endpoints
- `WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj` - Web SDK project referencing Infrastructure
- `WrtechedWhispers/WretchedWhispers.Api/appsettings.json` - SQLite connection string configuration
- `WrtechedWhispers/WretchedWhispers.Api/appsettings.Development.json` - Development logging configuration
- `WrtechedWhispers/WrtechedWhispers.sln` - Added Api project to solution
- `WrtechedWhispers/WretchedWhispers.Core/Campaigns/ICampaignsRepository.cs` - Added GetForUser and SaveCampaign(campaign, userId) methods
- `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteCampaignsRepository.cs` - Implemented userId-filtered queries and userId-aware save
- `WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj` - Added MVC Testing package and Api project reference
- `WrtechedWhispers/WretchedWhispers.Tests/Auth/AuthEndpointTests.cs` - 5 auth endpoint integration tests via WebApplicationFactory
- `WrtechedWhispers/WretchedWhispers.Tests/Persistence/CampaignMultiTenancyTests.cs` - 3 campaign multi-tenancy tests

## Decisions Made
- **BearerTokenOptions namespace:** `BearerTokenOptions` lives in `Microsoft.AspNetCore.Authentication.BearerToken`, not implicitly available. Added explicit using directive.
- **Partial Program class:** Added `public partial class Program { }` at bottom of Program.cs to make it accessible for `WebApplicationFactory<Program>` in the test project.
- **WebApplicationFactory DB setup:** Used `BuildServiceProvider()` inside `ConfigureServices` to create and call `EnsureCreated()` on the in-memory SQLite database, ensuring Identity + game table schemas exist before tests run.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added missing BearerTokenOptions using directive**
- **Found during:** Task 1 (Create Web API project)
- **Issue:** `BearerTokenOptions` type not found -- CS0246 build error because the type is in `Microsoft.AspNetCore.Authentication.BearerToken` namespace
- **Fix:** Added `using Microsoft.AspNetCore.Authentication.BearerToken;` to Program.cs
- **Files modified:** WrtechedWhispers/WretchedWhispers.Api/Program.cs
- **Verification:** Build succeeds with 0 errors
- **Committed in:** 24507c0 (Task 1 commit)

**2. [Rule 1 - Bug] Added missing Xunit using directives and IWebHostBuilder using in test files**
- **Found during:** Task 3 (Integration tests)
- **Issue:** Test files missing `using Xunit;` and `using Microsoft.AspNetCore.Hosting;` -- existing tests have explicit usings (not via global usings)
- **Fix:** Added `using Xunit;` and `using Microsoft.AspNetCore.Hosting;` to both test files
- **Files modified:** AuthEndpointTests.cs, CampaignMultiTenancyTests.cs
- **Verification:** Build succeeds, all tests pass
- **Committed in:** 2596411 (Task 3 commit)

**3. [Rule 1 - Bug] Added EnsureCreated call in WebApplicationFactory**
- **Found during:** Task 3 (Integration tests)
- **Issue:** Auth endpoint tests returned 500 InternalServerError because in-memory SQLite database had no schema tables
- **Fix:** Added `BuildServiceProvider` + `EnsureCreated()` inside ConfigureServices to create Identity + game table schema
- **Files modified:** AuthEndpointTests.cs
- **Verification:** All 5 auth tests pass
- **Committed in:** 2596411 (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (3 bugs)
**Impact on plan:** All auto-fixes necessary for correct compilation and test execution. No scope creep.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Full auth stack is operational: register, login, token refresh, bearer token validation
- Web API project is ready for Phase 3 to extend with game API endpoints and SSE streaming
- Campaign repository has user-scoped methods ready for multi-tenant API endpoints
- WebApplicationFactory pattern established for future API integration tests

## Self-Check: PASSED

All 9 claimed files verified present. All 3 commit hashes verified in git log.

---
*Phase: 02-authentication-and-multi-tenancy*
*Completed: 2026-03-03*
