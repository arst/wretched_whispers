---
phase: 04-frontend-foundation-and-character-creation
plan: 01
subsystem: ui
tags: [nextjs, tailwind-v4, zustand, typescript, cors, doom-aesthetic, blackletter-font, sse]

# Dependency graph
requires:
  - phase: 03-session-api-and-streaming
    provides: REST API endpoints and SSE streaming for sessions
  - phase: 03.1-persistence-multi-tenancy-fix
    provides: Tenant-scoped API with JWT auth
provides:
  - Next.js 16 app shell with doom-metal design system
  - CORS on .NET API allowing frontend origin
  - TypeScript interfaces for all backend DTOs and SSE events
  - Zustand auth store with localStorage persistence
  - Authenticated API client with automatic token refresh
  - Auth helper functions (login, register, verifyToken, logout)
affects: [04-02, 04-03, all-future-frontend-plans]

# Tech tracking
tech-stack:
  added: [nextjs-16, react-19, tailwind-css-v4, zustand, "@microsoft/fetch-event-source", unifrakturmaguntia-font, inter-font]
  patterns: [css-first-tailwind-theme, zustand-persist-skiphydration, apifetch-token-refresh, doom-color-system]

key-files:
  created:
    - wretched-whispers-web/src/app/layout.tsx
    - wretched-whispers-web/src/app/globals.css
    - wretched-whispers-web/src/app/page.tsx
    - wretched-whispers-web/src/types/api.ts
    - wretched-whispers-web/src/stores/authStore.ts
    - wretched-whispers-web/src/lib/api.ts
    - wretched-whispers-web/src/lib/auth.ts
    - wretched-whispers-web/public/textures/noise.png
  modified:
    - WrtechedWhispers/WretchedWhispers.Api/Program.cs

key-decisions:
  - "UnifrakturMaguntia Google Font for blackletter display headers (weight 400, the only available weight)"
  - "Zustand persist with skipHydration:true to avoid SSR hydration mismatches"
  - "apiFetch wrapper reads tokens via useAuthStore.getState() (non-hook access for lib code)"
  - "Doom color palette: yellow #ffe000, pink #ff1493, bone #e8e0d4, ash #8a8a8a, blood #8b0000"
  - "Noise texture: 128x128 grayscale PNG at 4% opacity via body::before pseudo-element"

patterns-established:
  - "CSS-first Tailwind v4 config: @theme block in globals.css defines custom colors and fonts"
  - "Font CSS variables: --font-doom-display and --font-inter set by next/font, consumed by @theme"
  - "Auth store non-hook access: useAuthStore.getState() for lib/api.ts outside React components"
  - "Token refresh pattern: apiFetch catches 401, attempts refresh, retries or logout"
  - ".env.example committed, .env.local gitignored for local secrets"

requirements-completed: [UI-02, UI-03]

# Metrics
duration: 6min
completed: 2026-03-05
---

# Phase 04 Plan 01: Frontend Foundation Summary

**Next.js 16 app with Mork Borg doom-metal design system, CORS-enabled API, Zustand auth store with localStorage persistence, and typed API client with automatic token refresh**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-05T09:00:42Z
- **Completed:** 2026-03-05T09:07:27Z
- **Tasks:** 3
- **Files modified:** 19

## Accomplishments
- CORS added to .NET API allowing http://localhost:3000 cross-origin requests
- Next.js 16 app scaffolded with doom color palette (yellow/pink/bone/ash), UnifrakturMaguntia blackletter font, Inter body font, and grain texture overlay
- TypeScript interfaces matching all backend DTOs (sessions, chat messages, SSE events, auth responses)
- Zustand auth store with localStorage persistence using skipHydration for SSR safety
- Authenticated API client (apiFetch) with automatic Bearer token attachment and 401 token refresh
- Auth helper functions (login with useCookies=false, register, verifyToken, logout)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add CORS to .NET API for frontend origin** - `43327ec` (feat)
2. **Task 2: Scaffold Next.js app with doom-metal design system** - `027c90a` (feat)
3. **Task 3: Create TypeScript API types, auth store, and API client** - `73444d7` (feat)

## Files Created/Modified

- `WrtechedWhispers/WretchedWhispers.Api/Program.cs` - Added CORS middleware for localhost:3000
- `wretched-whispers-web/package.json` - Next.js 16, React 19, Tailwind v4, Zustand, fetch-event-source
- `wretched-whispers-web/src/app/globals.css` - Doom color palette via @theme, grain overlay, grunge border utility
- `wretched-whispers-web/src/app/layout.tsx` - Root layout with UnifrakturMaguntia + Inter fonts, dark body
- `wretched-whispers-web/src/app/page.tsx` - Doom-themed placeholder landing page
- `wretched-whispers-web/public/textures/noise.png` - 128x128 grayscale noise texture for grain overlay
- `wretched-whispers-web/.env.example` - API URL config template
- `wretched-whispers-web/src/types/api.ts` - TypeScript interfaces for all backend DTOs and SSE events
- `wretched-whispers-web/src/stores/authStore.ts` - Zustand auth store with persist middleware
- `wretched-whispers-web/src/lib/api.ts` - Authenticated fetch wrapper with token refresh
- `wretched-whispers-web/src/lib/auth.ts` - Auth helpers (login, register, verifyToken, logout)

## Decisions Made

- **UnifrakturMaguntia font:** Selected from Google Fonts for blackletter display headers. Only weight 400 available (plan assumed 700). The font renders well at large heading sizes with adequate visual weight at 400.
- **skipHydration pattern:** Used Zustand persist `skipHydration: true` with `partialize` to exclude `isHydrated` from storage, preventing SSR hydration mismatches (Pitfall 4 from research).
- **Non-hook state access:** `useAuthStore.getState()` used in lib/api.ts and lib/auth.ts since these run outside React component context.
- **Doom palette colors:** Used research-recommended hex values (#ffe000 yellow, #ff1493 pink, #e8e0d4 bone, #8a8a8a ash, #8b0000 blood) as CSS custom properties in @theme block.
- **Cleaned up default scaffolding:** Removed default Next.js SVG assets (vercel.svg, next.svg, etc.) and replaced default page/layout with doom-themed versions.
- **.env.example pattern:** Committed .env.example while .gitignoring .env.local. Updated .gitignore to explicitly allow .env.example.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected UnifrakturMaguntia font weight from 700 to 400**
- **Found during:** Task 2 (Next.js build verification)
- **Issue:** Plan specified `weight: "700"` but UnifrakturMaguntia only has weight 400 available
- **Fix:** Changed weight to "400" in font configuration
- **Files modified:** wretched-whispers-web/src/app/layout.tsx
- **Verification:** Build passes, font renders correctly
- **Committed in:** 027c90a (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Trivial font weight correction. No scope change.

## Issues Encountered
- create-next-app interactive prompts required piping input (React Compiler and import alias prompts not covered by CLI flags) -- resolved by piping empty input via `yes ''`

## User Setup Required

None - no external service configuration required. The .env.local is created automatically by Task 2.

## Next Phase Readiness
- Frontend shell running with doom aesthetic -- ready for auth screens (Plan 02)
- API client and auth store in place -- ready for authenticated feature development
- TypeScript types defined -- ready for session list and chat UI (Plans 02-03)
- CORS configured -- frontend can communicate with API immediately

## Self-Check: PASSED

All 9 key files verified present. All 3 task commits verified in git log.

---
*Phase: 04-frontend-foundation-and-character-creation*
*Completed: 2026-03-05*
