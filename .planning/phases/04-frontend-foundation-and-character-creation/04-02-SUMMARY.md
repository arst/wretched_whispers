---
phase: 04-frontend-foundation-and-character-creation
plan: 02
subsystem: ui
tags: [nextjs, react, tailwind-v4, zustand, auth-screens, session-list, mork-borg, doom-aesthetic]

# Dependency graph
requires:
  - phase: 04-frontend-foundation-and-character-creation
    plan: 01
    provides: Next.js scaffold, doom design system, auth store, API client, TypeScript types
  - phase: 03-session-api-and-streaming
    provides: REST API endpoints for sessions and auth
provides:
  - Landing page with Mork Borg atmosphere and Begin CTA
  - Themed login and register pages with full auth flow
  - Session list page with API-driven session cards and create functionality
  - Reusable Button, Input, Header, and AuthGuard components
  - Protected routing via AuthGuard with redirect to login
  - StoreHydration provider for Zustand skipHydration pattern
affects: [04-03, all-future-frontend-plans]

# Tech tracking
tech-stack:
  added: []
  patterns: [auth-guard-redirect, store-hydration-provider, session-card-component, auth-layout-route-group]

key-files:
  created:
    - wretched-whispers-web/src/components/ui/Button.tsx
    - wretched-whispers-web/src/components/ui/Input.tsx
    - wretched-whispers-web/src/components/layout/Header.tsx
    - wretched-whispers-web/src/components/layout/AuthGuard.tsx
    - wretched-whispers-web/src/components/providers/StoreHydration.tsx
    - wretched-whispers-web/src/app/(auth)/layout.tsx
    - wretched-whispers-web/src/app/(auth)/login/page.tsx
    - wretched-whispers-web/src/app/(auth)/register/page.tsx
    - wretched-whispers-web/src/app/sessions/layout.tsx
    - wretched-whispers-web/src/app/sessions/page.tsx
    - wretched-whispers-web/src/components/session/SessionCard.tsx
  modified:
    - wretched-whispers-web/src/app/page.tsx
    - wretched-whispers-web/src/app/layout.tsx

key-decisions:
  - "StoreHydration provider pattern: client component calling useAuthStore.persist.rehydrate() in root layout for skipHydration stores"
  - "Auth layout route group (auth) with centered card container for clean atmospheric login/register pages"
  - "Landing page uses 'use client' to read auth state for conditional Begin CTA destination"

patterns-established:
  - "AuthGuard pattern: useEffect redirect with isHydrated gate prevents SSR issues"
  - "StoreHydration provider in root layout triggers Zustand rehydrate for all skipHydration stores"
  - "Auth route group layout with no Header for immersive auth experience"
  - "Sessions layout wraps children in AuthGuard + Header for protected routes"

requirements-completed: [UI-01, UI-03]

# Metrics
duration: 3min
completed: 2026-03-05
---

# Phase 04 Plan 02: Pages and Auth Screens Summary

**Atmosphere-first landing page, themed login/register screens with Mork Borg doom-metal aesthetic, session list with API-driven cards and create functionality, protected routing via AuthGuard**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-05T09:10:38Z
- **Completed:** 2026-03-05T09:13:45Z
- **Tasks:** 3
- **Files modified:** 13

## Accomplishments
- Atmosphere-first landing page with doom title, flavor text, and Begin CTA that routes authenticated users to /sessions and unauthenticated to /login
- Fully themed login and register pages with error handling, auto-redirect for authenticated users, and register auto-login flow
- Session list page fetching from API with doom-styled session cards showing status badges, character info, HP, and relative timestamps
- Reusable Button (primary/secondary/danger variants), Input (with label/error), Header (auth-aware), and AuthGuard (redirect) components
- New session creation via POST /sessions with redirect to the new session

## Task Commits

Each task was committed atomically:

1. **Task 1: Create shared UI primitives and layout components** - `39ccd1e` (feat)
2. **Task 2: Build landing page and themed auth screens** - `2476dbd` (feat)
3. **Task 3: Build session list page with create functionality** - `eefeb61` (feat)

## Files Created/Modified

- `wretched-whispers-web/src/components/ui/Button.tsx` - Doom-styled button with primary/secondary/danger variants and loading state
- `wretched-whispers-web/src/components/ui/Input.tsx` - Doom-styled input with label, error state, and forwardRef
- `wretched-whispers-web/src/components/layout/Header.tsx` - Fixed header with game title, auth-aware nav, hydration skeleton
- `wretched-whispers-web/src/components/layout/AuthGuard.tsx` - Protected route wrapper redirecting unauthenticated to /login
- `wretched-whispers-web/src/components/providers/StoreHydration.tsx` - Triggers Zustand persist rehydration on mount
- `wretched-whispers-web/src/app/page.tsx` - Atmosphere-first landing page with doom title and Begin CTA
- `wretched-whispers-web/src/app/layout.tsx` - Added StoreHydration provider to root layout
- `wretched-whispers-web/src/app/(auth)/layout.tsx` - Centered card container for auth pages
- `wretched-whispers-web/src/app/(auth)/login/page.tsx` - Themed login form with error handling
- `wretched-whispers-web/src/app/(auth)/register/page.tsx` - Themed register form with password confirmation and auto-login
- `wretched-whispers-web/src/app/sessions/layout.tsx` - Sessions layout with Header and AuthGuard
- `wretched-whispers-web/src/app/sessions/page.tsx` - Session list with API fetch, loading skeletons, empty state, create button
- `wretched-whispers-web/src/components/session/SessionCard.tsx` - Session preview card with status badge, character info, HP

## Decisions Made

- **StoreHydration provider:** Created a dedicated client component that calls `useAuthStore.persist.rehydrate()` and `setHydrated()` in the root layout. This was missing from Plan 01 and is required for the skipHydration pattern to work at all.
- **Landing page as client component:** Made the entire landing page `"use client"` for simplicity since it reads auth state to determine the Begin CTA destination.
- **Auth route group layout:** Used Next.js route group `(auth)` with centered card container and no Header for an immersive, atmospheric login/register experience.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added StoreHydration provider for Zustand rehydrate**
- **Found during:** Task 2 (Landing page and auth screens)
- **Issue:** Auth store uses `skipHydration: true` but nothing ever calls `rehydrate()` or `setHydrated()`. Header, AuthGuard, and all auth-aware components would never see hydrated state.
- **Fix:** Created `StoreHydration` client component that calls `useAuthStore.persist.rehydrate()` and `setHydrated()` on mount, added to root layout.
- **Files modified:** `wretched-whispers-web/src/components/providers/StoreHydration.tsx` (created), `wretched-whispers-web/src/app/layout.tsx` (modified)
- **Verification:** Build passes, auth state flows correctly
- **Committed in:** `2476dbd` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Essential for correctness -- without rehydration, auth state would never restore from localStorage. No scope creep.

## Issues Encountered

None -- all tasks executed cleanly.

## User Setup Required

None - no external service configuration required. Uses .env.local from Plan 01.

## Next Phase Readiness
- All pre-gameplay screens complete: landing, login, register, session list
- Protected routing in place via AuthGuard
- Session creation redirects to /sessions/{id} -- ready for Plan 03 (character creation / game session UI)
- Reusable UI components (Button, Input, Header) established for future pages

## Self-Check: PASSED

All 13 key files verified present. All 3 task commits verified in git log.

---
*Phase: 04-frontend-foundation-and-character-creation*
*Completed: 2026-03-05*
