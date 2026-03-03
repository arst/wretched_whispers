---
phase: 02-authentication-and-multi-tenancy
plan: 01
subsystem: database
tags: [aspnet-identity, ef-core, sqlite, multi-tenancy, identity-user-context]

# Dependency graph
requires:
  - phase: 01-domain-foundation
    provides: "EF Core DbContext with SQLite, CampaignEntity, persistence tests"
provides:
  - "IdentityUserContext-based DbContext with Identity table mappings"
  - "CampaignEntity.UserId property with index for multi-tenant filtering"
  - "Fresh InitialCreateWithIdentity migration (Identity + game tables)"
affects: [02-authentication-and-multi-tenancy, 03-api-and-streaming]

# Tech tracking
tech-stack:
  added: [Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.*]
  patterns: [IdentityUserContext inheritance, UserId FK for multi-tenancy]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Migrations/20260303124058_InitialCreateWithIdentity.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/WretchedWhispersDbContext.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/CampaignEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/CampaignEntityConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/WretchedWhispers.Infrastructure.csproj

key-decisions:
  - "Downgraded all project TFMs from net10.0 to net9.0 (SDK 10.0 not available)"
  - "Used IdentityUserContext (no roles) instead of IdentityDbContext (saves 3 tables)"
  - "Deleted existing migration and DB for fresh combined migration (pre-release, no real data)"

patterns-established:
  - "IdentityUserContext<IdentityUser> as DbContext base class with base.OnModelCreating called first"
  - "UserId string property on persistence entities for multi-tenant FK (maxLength 450, indexed)"

requirements-completed: [INFR-03]

# Metrics
duration: 4min
completed: 2026-03-03
---

# Phase 02 Plan 01: Identity Infrastructure Summary

**IdentityUserContext-based DbContext with CampaignEntity.UserId FK and combined Identity + game table migration on SQLite**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-03T12:38:43Z
- **Completed:** 2026-03-03T12:42:15Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- Migrated DbContext from plain DbContext to IdentityUserContext<IdentityUser> with proper base.OnModelCreating call
- Added UserId string property to CampaignEntity with required constraint, max-length 450, and index
- Created single combined EF migration containing 4 Identity tables + 5 game tables + UserId column
- All 214 existing tests pass unchanged with the new Identity-based DbContext

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Identity package and migrate DbContext to IdentityUserContext** - `255ee74` (feat)
2. **Task 2: Add UserId to CampaignEntity, create EF migration, update test base** - `5bca843` (feat)

## Files Created/Modified
- `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/WretchedWhispersDbContext.cs` - Changed base class to IdentityUserContext<IdentityUser>, added base.OnModelCreating call
- `WrtechedWhispers/WretchedWhispers.Infrastructure/WretchedWhispers.Infrastructure.csproj` - Added Identity.EntityFrameworkCore package, TFM net9.0
- `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/CampaignEntity.cs` - Added UserId string property
- `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/CampaignEntityConfiguration.cs` - Added UserId required + max-length + index configuration
- `WrtechedWhispers/WretchedWhispers.Infrastructure/Migrations/20260303124058_InitialCreateWithIdentity.cs` - Fresh combined migration
- `WrtechedWhispers/WretchedWhispers.Core/WretchedWhispers.Core.csproj` - TFM net9.0
- `WrtechedWhispers/WretchedWhispers.Semantic/WretchedWhispers.Semantic.csproj` - TFM net9.0
- `WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj` - TFM net9.0
- `WrtechedWhispers/WretchedWhispers.SingleAgent.Console/WretchedWhispers.SingleAgent.Console.csproj` - TFM net9.0
- `WrtechedWhispers/WretchedWhispers.Orchestration.Console/WretchedWhispers.Orchestration.Console.csproj` - TFM net9.0

## Decisions Made
- **TFM downgrade:** All 6 projects downgraded from net10.0 to net9.0 because only .NET 9 SDK (9.0.311) is installed. The net10.0 TFM was set expecting a preview SDK that is not available. All packages use 9.0.* which is the correct version match.
- **IdentityUserContext over IdentityDbContext:** No roles needed for this project; saves 3 unnecessary tables (AspNetRoles, AspNetRoleClaims, AspNetUserRoles).
- **Fresh combined migration:** Deleted existing InitialCreate migration and database, created a single InitialCreateWithIdentity migration. Pre-release project with no real users, so no data migration needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Downgraded TFMs from net10.0 to net9.0 across all projects**
- **Found during:** Task 1 (adding NuGet package)
- **Issue:** All 6 projects target net10.0 but only .NET 9 SDK (9.0.311) is installed; `dotnet add package` and `dotnet build` fail with NETSDK1045
- **Fix:** Changed TargetFramework from net10.0 to net9.0 in all 6 .csproj files
- **Files modified:** All 6 .csproj files
- **Verification:** `dotnet build` succeeds, all 214 tests pass
- **Committed in:** 255ee74 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** TFM fix was necessary for any build/tooling to work. No scope creep.

## Issues Encountered

- EF Core migration tooling generated the Migrations folder under `WrtechedWhispers/WretchedWhispers.Infrastructure/Migrations/` (project root) rather than `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Migrations/` (the old location). This is the default EF Core convention and is correct; the old location was a custom choice. No impact on functionality.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Identity tables are configured in the DbContext and migration; ready for Plan 02 (API endpoints + Identity service registration)
- CampaignEntity has UserId for multi-tenant query filtering
- SqliteTestBase works unchanged with IdentityUserContext for existing persistence tests

---
*Phase: 02-authentication-and-multi-tenancy*
*Completed: 2026-03-03*
