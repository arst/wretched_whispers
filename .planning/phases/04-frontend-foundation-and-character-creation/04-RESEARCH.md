# Phase 4: Frontend Foundation and Character Creation - Research

**Researched:** 2026-03-05
**Domain:** React/Next.js SPA, SSE streaming, doom-metal aesthetic, JWT auth
**Confidence:** HIGH

## Summary

This phase is greenfield frontend work -- no frontend code exists. The backend API is complete with ASP.NET Identity auth endpoints (`/auth/register`, `/auth/login`, `/auth/me`), session CRUD (`/sessions`), and SSE streaming (`POST /sessions/{id}/actions`). The frontend must consume these endpoints, display a Mork Borg doom-metal aesthetic, and implement character creation as a narrator-guided conversation with typewriter-style SSE streaming.

The standard 2026 stack for this is **Next.js 15+ with App Router**, **Tailwind CSS v4** (CSS-first configuration), and **Zustand** for lightweight client state. The critical technical challenge is SSE consumption with auth headers -- the native `EventSource` API does not support custom headers, so a fetch-based SSE library is required. The API runs on `http://localhost:5007` and has no CORS configuration, which must be added before the frontend can communicate cross-origin.

**Primary recommendation:** Use Next.js 15 with App Router, Tailwind CSS v4, Zustand for auth/session state, and `@microsoft/fetch-event-source` for authenticated SSE streaming. Add CORS to the .NET API. Self-host a blackletter display font for doom headers.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- "Controlled grime" direction -- dark base with Mork Borg accents, not full visual chaos
- Background: near-black with subtle paper/parchment grain or noise texture overlay
- Typography: custom doom/distressed display font for headers and key UI elements; clean sans-serif (Inter or similar) for body text and readability
- Color palette: yellow (#FFFF00-ish) as primary for headings, active elements, and CTAs; hot pink/magenta for danger, damage, and death indicators; white for body text
- Grunge accents on borders, dividers, and decorative elements -- not overwhelming
- Conversational back-and-forth character creation: narrator asks questions, player types responses via the same text input used for gameplay
- Atmospheric splash screen displayed while the GM generates the opening message -- mood-setting with title/art/flavor text, transitions to chat when first narrative SSE event arrives
- No live character sheet during creation -- minimal inline hints instead. Key reveals (name, stats, equipment) get visual emphasis as styled callouts within the conversation
- Dice rolls and stat assignments rendered as inline styled callouts using tool_result SSE events
- Full character sheet sidebar is a Phase 5 concern
- Atmosphere-first landing page: game title, flavor text, prominent "Begin" CTA. Session list accessible but secondary
- Themed auth screens: login and register pages with the Mork Borg aesthetic
- Minimal header bar: persistent small header with game title/logo, session name when in one, and back/home button. No sidebar
- Responsive: same single-column layout for desktop and tablet, scales fluidly. No special breakpoints or extra panels for desktop in Phase 4
- Distinct visual treatment for narrator vs player messages
- Loading/thinking indicator: animated placeholder in the GM message area styled in the narrator's visual treatment
- Typewriter streaming: narrator text appears word-by-word as narrative SSE events arrive
- Themed text input: fixed bottom bar with doom aesthetic styling, in-character placeholder text, themed send button. Single line that expands for longer text

### Claude's Discretion
- Specific doom display font selection (blackletter, metal-style, or hand-drawn)
- Exact grunge texture and border treatment details
- Splash screen content and transition animation
- Loading indicator animation style (dots, skull, dripping text, etc.)
- Exact message card styling, spacing, and border treatments
- Token storage strategy (localStorage vs httpOnly cookie for JWT)
- Next.js project structure and routing approach
- Component library choice (if any -- Tailwind, shadcn, etc.)
- State management approach for auth and session data
- Error handling UI patterns

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CHAR-01 | User creates character through guided narrator conversation | SSE streaming pattern, chat UI architecture, tool_result callout rendering |
| GAME-05 | Loading/thinking indicator shows while LLM is processing | SSE connection state management, animated placeholder component |
| UI-01 | Responsive layout readable on desktop and tablet | Tailwind CSS v4 fluid layout, single-column responsive design |
| UI-02 | Dark theme suitable for grim game atmosphere | Tailwind dark theme, CSS custom properties for doom palette |
| UI-03 | Mork Borg doom-metal visual aesthetic (yellow/black/pink palette, textures) | Font selection, color system, grunge texture techniques |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Next.js | 15.x (stable) | React framework with App Router | Standard React framework for 2025-2026; App Router is the recommended approach; Turbopack for fast dev |
| React | 19.x | UI library | Ships with Next.js 15; concurrent features, hooks |
| Tailwind CSS | 4.x | Utility-first CSS | CSS-first config (no JS config file), 70% smaller output than v3, automatic content detection |
| TypeScript | 5.x | Type safety | Ships with create-next-app; typed routes in Next.js 15+ |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Zustand | 5.x | Client state management | Auth state (tokens, user), session state, UI state. 1.16KB gzipped, no providers needed |
| @microsoft/fetch-event-source | 2.x | SSE with custom headers | POST requests to `/sessions/{id}/actions` with Authorization bearer token. Native EventSource cannot send headers |
| next/font | (built-in) | Font optimization | Self-host Inter for body text; self-host custom doom font for headers |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Zustand | Jotai | Jotai is atomic/bottom-up (2.1KB); Zustand is store-based/top-down (1.16KB). Zustand is simpler for this app's auth+session global state |
| Zustand | React Context | Context causes full-subtree re-renders; Zustand has selector-based subscriptions for targeted updates |
| @microsoft/fetch-event-source | eventsource npm | eventsource supports custom fetch but @microsoft/fetch-event-source is more widely used for LLM streaming, supports POST, and has better abort/retry control |
| Tailwind only | shadcn/ui | shadcn adds pre-built accessible components but requires heavy restyling for the doom aesthetic. Tailwind-only is lighter for a highly custom visual design |

**Installation:**
```bash
npx create-next-app@latest wretched-whispers-web --typescript --tailwind --eslint --app --src-dir
cd wretched-whispers-web
npm install zustand @microsoft/fetch-event-source
```

## Architecture Patterns

### Recommended Project Structure
```
wretched-whispers-web/
├── src/
│   ├── app/                          # Next.js App Router
│   │   ├── layout.tsx                # Root layout (fonts, theme, providers)
│   │   ├── page.tsx                  # Landing page (atmosphere-first)
│   │   ├── (auth)/                   # Route group for auth pages
│   │   │   ├── login/page.tsx
│   │   │   └── register/page.tsx
│   │   └── session/
│   │       ├── page.tsx              # Session list
│   │       └── [id]/page.tsx         # Game session (chat UI)
│   ├── components/
│   │   ├── ui/                       # Generic UI primitives (Button, Input, Card)
│   │   ├── chat/                     # Chat-specific components
│   │   │   ├── ChatWindow.tsx        # Message list + auto-scroll
│   │   │   ├── NarratorMessage.tsx   # Styled GM message card
│   │   │   ├── PlayerMessage.tsx     # Player message bubble
│   │   │   ├── ToolResultCallout.tsx # Dice roll / stat callout
│   │   │   ├── ThinkingIndicator.tsx # Loading animation
│   │   │   └── ChatInput.tsx         # Fixed bottom input bar
│   │   ├── layout/                   # Header, splash screen
│   │   └── session/                  # Session list, session card
│   ├── hooks/
│   │   ├── useAuth.ts               # Auth state hook (wraps Zustand store)
│   │   ├── useSseStream.ts          # SSE streaming hook with fetch-event-source
│   │   └── useAutoScroll.ts         # Scroll-to-bottom on new messages
│   ├── lib/
│   │   ├── api.ts                   # API client (fetch wrapper with auth headers)
│   │   ├── auth.ts                  # Token storage, refresh logic
│   │   └── sse.ts                   # SSE event parsing utilities
│   ├── stores/
│   │   ├── authStore.ts             # Zustand: tokens, user, login/logout actions
│   │   └── sessionStore.ts          # Zustand: current session, messages, streaming state
│   ├── types/
│   │   └── api.ts                   # TypeScript types matching backend DTOs
│   └── styles/
│       └── globals.css              # Tailwind imports, @theme config, grunge textures
├── public/
│   ├── fonts/                       # Self-hosted doom display font files
│   └── textures/                    # Noise/grain overlay images
├── .env.local                       # NEXT_PUBLIC_API_URL=http://localhost:5007
└── next.config.ts
```

### Pattern 1: Authenticated API Client
**What:** Centralized fetch wrapper that attaches Bearer token from Zustand store and handles token refresh
**When to use:** Every API call to the backend
**Example:**
```typescript
// src/lib/api.ts
import { useAuthStore } from '@/stores/authStore';

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

export async function apiFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const { accessToken, refreshToken, setTokens, logout } = useAuthStore.getState();

  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...options.headers,
    },
  });

  if (response.status === 401 && refreshToken) {
    // Attempt token refresh
    const refreshResponse = await fetch(`${API_URL}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });

    if (refreshResponse.ok) {
      const data = await refreshResponse.json();
      setTokens(data.accessToken, data.refreshToken, data.expiresIn);
      // Retry original request with new token
      return fetch(`${API_URL}${path}`, {
        ...options,
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${data.accessToken}`,
          ...options.headers,
        },
      });
    } else {
      logout();
    }
  }

  return response;
}
```

### Pattern 2: SSE Streaming with fetch-event-source
**What:** POST-based SSE consumption with auth headers, abort control, and typed event parsing
**When to use:** Submitting player actions and streaming GM responses
**Example:**
```typescript
// src/hooks/useSseStream.ts
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useAuthStore } from '@/stores/authStore';
import { useSessionStore } from '@/stores/sessionStore';

const API_URL = process.env.NEXT_PUBLIC_API_URL!;

export function useSseStream(sessionId: string) {
  const abortRef = useRef<AbortController | null>(null);

  const sendAction = useCallback(async (message: string) => {
    const { accessToken } = useAuthStore.getState();
    const { appendNarrativeChunk, addToolResult, setStreaming, setStateUpdate } =
      useSessionStore.getState();

    // Abort any in-flight stream
    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    setStreaming(true);

    await fetchEventSource(`${API_URL}/sessions/${sessionId}/actions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify({ message }),
      signal: ctrl.signal,

      onmessage(ev) {
        switch (ev.event) {
          case 'narrative':
            appendNarrativeChunk(JSON.parse(ev.data).text);
            break;
          case 'tool_result':
            addToolResult(JSON.parse(ev.data));
            break;
          case 'state_update':
            setStateUpdate(JSON.parse(ev.data));
            break;
          case 'done':
            setStreaming(false);
            break;
          case 'error':
            setStreaming(false);
            // Handle error display
            break;
        }
      },

      onerror(err) {
        setStreaming(false);
        throw err; // Stop retrying
      },
    });
  }, [sessionId]);

  // Cleanup on unmount
  useEffect(() => {
    return () => abortRef.current?.abort();
  }, []);

  return { sendAction };
}
```

### Pattern 3: Zustand Auth Store with localStorage Persistence
**What:** Token storage in localStorage with Zustand middleware for persistence across page refreshes
**When to use:** Auth state that survives browser refresh (requirement AUTH-03)
**Example:**
```typescript
// src/stores/authStore.ts
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: number | null;
  isAuthenticated: boolean;
  setTokens: (access: string, refresh: string, expiresIn: number) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      isAuthenticated: false,
      setTokens: (access, refresh, expiresIn) =>
        set({
          accessToken: access,
          refreshToken: refresh,
          expiresAt: Date.now() + expiresIn * 1000,
          isAuthenticated: true,
        }),
      logout: () =>
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          isAuthenticated: false,
        }),
    }),
    { name: 'ww-auth' }
  )
);
```

### Anti-Patterns to Avoid
- **Putting API calls in components directly:** Use hooks (useAuth, useSseStream) or lib/api.ts. Components should call hooks, not fetch.
- **Using React Context for frequently-changing state:** Context re-renders the entire subtree. Zustand with selectors gives granular subscriptions.
- **Using native EventSource for authenticated SSE:** Native EventSource has no header support. Always use @microsoft/fetch-event-source for bearer token auth.
- **Storing tokens in cookies for a SPA:** The API uses ASP.NET Identity bearer tokens (not cookie auth). Use localStorage via Zustand persist middleware. The `?useCookies=false` query parameter is required on the login endpoint.
- **Server Components for interactive pages:** The chat UI, auth forms, and session list all need client-side interactivity. Use `"use client"` directive on interactive pages/components. Reserve Server Components for the static landing page if desired.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSE parsing with auth | Custom fetch + manual text/event-stream parsing | @microsoft/fetch-event-source | Handles reconnection, abort, typed events, POST support. Manual parsing misses edge cases (multi-line data, retry fields) |
| Token refresh logic | Ad-hoc retry in every fetch call | Centralized apiFetch wrapper with automatic refresh | Single place for token refresh avoids race conditions with concurrent requests |
| Font loading/optimization | Manual @font-face with preload | next/font (local) | Next.js handles font subsetting, preload hints, layout shift prevention |
| Responsive layout system | Custom media queries | Tailwind responsive utilities | `max-w-2xl mx-auto` with `px-4` gives single-column responsive without breakpoints |
| Auto-scroll chat | Manual scrollIntoView + resize observer | Custom hook with ref + useEffect | Simple but needs to handle "user scrolled up" detection to avoid jarring scroll during reading |

**Key insight:** The doom aesthetic is highly custom -- no off-the-shelf component library will match. Use Tailwind utilities directly for the visual layer. The hand-roll boundary is infrastructure (SSE, auth, fonts) vs. presentation (all custom).

## Common Pitfalls

### Pitfall 1: CORS Not Configured on API
**What goes wrong:** Frontend on localhost:3000 cannot reach API on localhost:5007. All requests fail with CORS errors.
**Why it happens:** The API has no CORS middleware configured (confirmed by code inspection -- no `AddCors` or `UseCors` in Program.cs).
**How to avoid:** Add CORS configuration to the API before any frontend work begins. Allow `http://localhost:3000` origin with credentials.
**Warning signs:** Browser console shows "Access to fetch has been blocked by CORS policy"

### Pitfall 2: ASP.NET Identity Login Requires useCookies=false
**What goes wrong:** Login endpoint returns a Set-Cookie header instead of a JSON body with tokens.
**Why it happens:** ASP.NET Identity API endpoints default to cookie-based auth. The bearer token response requires `?useCookies=false` query parameter.
**How to avoid:** Always call `POST /auth/login?useCookies=false` from the frontend. The existing tests confirm this pattern.
**Warning signs:** Login response has no body or returns HTML redirect

### Pitfall 3: SSE Connection Not Closing After done Event
**What goes wrong:** Browser holds open the connection, preventing future requests to the same endpoint (browser limits concurrent connections per origin).
**Why it happens:** @microsoft/fetch-event-source retries by default on connection close.
**How to avoid:** Throw an error in `onclose` callback or call `ctrl.abort()` after receiving the `done` event to stop retry behavior.
**Warning signs:** Second action submission hangs or times out

### Pitfall 4: Hydration Mismatch with localStorage Auth
**What goes wrong:** Server renders "not authenticated" state, client hydrates with stored tokens -- React throws hydration error.
**Why it happens:** localStorage is not available during server-side rendering. Zustand persist middleware reads from storage on client only.
**How to avoid:** Use `skipHydration: true` in Zustand persist config and call `useAuthStore.persist.rehydrate()` in a useEffect. Or use `"use client"` on auth-dependent components and show a loading skeleton on first render.
**Warning signs:** React hydration mismatch warnings in console, flash of unauthenticated content

### Pitfall 5: Typewriter Streaming Causing Excessive Re-renders
**What goes wrong:** Every SSE narrative chunk triggers a state update, which re-renders the entire message list.
**Why it happens:** Naive implementation stores all text in a single Zustand state slice.
**How to avoid:** Use a ref for the currently-streaming message content and update the ref directly (no state update per chunk). Only commit to state when streaming completes. Or use a dedicated "streaming message" component that reads from a ref.
**Warning signs:** Visible lag, dropped frames during streaming, React DevTools showing hundreds of renders per second

### Pitfall 6: 409 Conflict Not Handled
**What goes wrong:** User double-clicks send, gets a 409 Conflict response with no feedback.
**Why it happens:** The API has a concurrency guard -- only one action per session at a time.
**How to avoid:** Disable the send button while streaming is active. Show an error toast if 409 is received. The session store's `isStreaming` flag controls this.
**Warning signs:** Silent failures, user confusion when second message seems to disappear

### Pitfall 7: Tool Results Arriving After Narrative
**What goes wrong:** Dice roll callouts appear below all narrative text instead of inline where they were contextually relevant.
**Why it happens:** The API sends tool_result events after the complete narrative stream (see GameSessionService lines 138-142).
**How to avoid:** Append tool results to the end of the current GM turn as styled callouts. They are a summary of what happened during the turn, not inline annotations. Design the UI to make this feel natural (e.g., a "results" section below the narrative).
**Warning signs:** Callouts feel disconnected from the narrative they reference

## Code Examples

### ASP.NET Identity Auth Endpoints (Backend Contract)

The frontend must call these exact endpoints with these exact shapes:

```typescript
// Register: POST /auth/register
// Request: { email: string, password: string }
// Response: 200 OK (no body)

// Login: POST /auth/login?useCookies=false
// Request: { email: string, password: string }
// Response: { tokenType: "Bearer", accessToken: string, expiresIn: number, refreshToken: string }

// Refresh: POST /auth/refresh
// Request: { refreshToken: string }
// Response: { tokenType: "Bearer", accessToken: string, expiresIn: number, refreshToken: string }

// Verify: GET /auth/me (Authorization: Bearer <token>)
// Response: { userId: string }
```

### SSE Event Types (Backend Contract)

```typescript
// Event: narrative
// Data: { text: string }  -- a chunk of narrator text (word or few words)

// Event: tool_result
// Data: { function: string, result: any }  -- dice roll, stat assignment, etc.

// Event: state_update
// Data: { campaignId: string, currentDay: number, currentHour: number,
//         characterId?: string, characterHp?: number, characterMaxHp?: number,
//         miseryCount: number, status: "character-creation" | "in-progress" | "ended" }

// Event: done
// Data: {}  -- stream complete

// Event: error
// Data: { message: string }  -- error occurred during processing
```

### Backend DTO Types for Frontend

```typescript
// src/types/api.ts

export interface SessionPreviewDto {
  sessionId: string;
  campaignName: string;
  description: string;
  characterName: string | null;
  currentHp: number | null;
  maxHp: number | null;
  status: 'character-creation' | 'in-progress' | 'ended';
  lastPlayed: string | null;
}

export interface SessionDetailDto {
  sessionId: string;
  campaignId: string;
  campaignName: string;
  description: string;
  currentDay: number;
  currentHour: number;
  status: string;
  messages: ChatMessageDto[];
  totalMessages: number;
  page: number;
  pageSize: number;
}

export interface ChatMessageDto {
  role: string;       // "user" | "assistant" | "system"
  content: string | null;
  authorName: string | null;  // "Game_Master" for narrator messages
}

export interface CreateSessionResponse {
  sessionId: string;
  campaignId: string;
}
```

### Doom Color System with Tailwind v4

```css
/* src/styles/globals.css */
@import "tailwindcss";

@theme {
  --color-doom-black: #0a0a0a;
  --color-doom-dark: #141414;
  --color-doom-card: #1a1a1a;
  --color-doom-yellow: #ffe000;
  --color-doom-pink: #ff1493;
  --color-doom-bone: #e8e0d4;
  --color-doom-ash: #8a8a8a;
  --color-doom-blood: #8b0000;

  --font-display: "DoomFont", serif;
  --font-body: "Inter", sans-serif;
}

/* Grain texture overlay */
body::before {
  content: "";
  position: fixed;
  inset: 0;
  background-image: url("/textures/noise.png");
  opacity: 0.04;
  pointer-events: none;
  z-index: 50;
}
```

### Font Setup with next/font

```typescript
// src/app/layout.tsx
import localFont from 'next/font/local';
import { Inter } from 'next/font/google';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-body',
});

const doomFont = localFont({
  src: '../fonts/doom-display.woff2',
  variable: '--font-display',
  display: 'swap',
});

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${inter.variable} ${doomFont.variable}`}>
      <body className="bg-doom-black text-white font-body min-h-screen">
        {children}
      </body>
    </html>
  );
}
```

## Doom Display Font Recommendation

**Recommendation: Self-host a blackletter font.** Options ranked by fit:

1. **Cloister Black** -- classic blackletter, heavy strokes, excellent for doom headers. Free for web use. Available on Google Fonts as a web font or self-hosted.
2. **UnifrakturMaguntia** -- available on Google Fonts, sharp traditional blackletter, good legibility at heading sizes.
3. **MedievalSharp** -- available on Google Fonts, slightly more readable blackletter variant.

For the Mork Borg aesthetic specifically, look at **free display fonts from itch.io Mork Borg community** (many SIL/OFL licensed). A hand-drawn or distressed blackletter matches the "controlled grime" directive better than a clean blackletter.

**Body text:** Inter (Google Fonts) -- clean, highly legible, excellent for body text on dark backgrounds. Good weight range for emphasis.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| tailwind.config.js | CSS-first @theme in globals.css | Tailwind v4 (Jan 2025) | No JS config file, configuration lives in CSS |
| @tailwind directives | @import "tailwindcss" | Tailwind v4 | Single import replaces three directives |
| tailwindcss PostCSS plugin | @tailwindcss/postcss | Tailwind v4 | Different PostCSS plugin name |
| Pages Router | App Router | Next.js 13+ (stable 15) | File-based routing in app/ dir, React Server Components, layouts |
| Redux / Context | Zustand | 2023+ dominant | No providers, selector-based subscriptions, 1KB bundle |
| Native EventSource | fetch-based SSE | 2023+ for LLM apps | POST support, custom headers, abort control |

**Deprecated/outdated:**
- `tailwind.config.js` is still supported in Tailwind v4 but is the legacy approach -- use CSS-first @theme
- `tailwindcss-animate` is deprecated as of March 2025 for Tailwind v4 -- use native CSS animations or motion.dev
- Next.js Pages Router still works but App Router is the recommended path for new projects

## Open Questions

1. **Exact doom display font**
   - What we know: Must be blackletter/distressed, freely licensed (SIL/OFL), self-hosted via next/font
   - What's unclear: Exact font choice depends on visual review of options against the Mork Borg book aesthetic
   - Recommendation: Pick 2-3 candidates (Cloister Black, UnifrakturMaguntia, or an itch.io community font), include font files in the project, let the implementer evaluate visually. Can be swapped easily since it is a CSS variable.

2. **Noise/grain texture source**
   - What we know: Need a subtle paper/parchment grain PNG for the body overlay
   - What's unclear: Need to source or generate a suitable texture image
   - Recommendation: Generate a simple noise texture (128x128 PNG, subtle grain) or use a freely licensed parchment texture. The CSS overlay with low opacity (0.03-0.05) handles the subtlety.

3. **Mork Borg exact hex codes**
   - What we know: Yellow and hot pink are the signature colors. The Mork Borg Design Primer contains official hex/CMYK values but the document is behind a download.
   - What's unclear: Exact hex values for the canonical yellow and pink
   - Recommendation: Use #FFE000 (warm yellow) and #FF1493 (deep pink/magenta) as starting points. These match the visual impression of the book. Can be fine-tuned during implementation since they are CSS variables.

## CORS Configuration Required

The .NET API currently has **no CORS configuration**. This is a blocking prerequisite for any frontend work.

```csharp
// Must be added to Program.cs BEFORE frontend can communicate
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000") // Next.js dev server
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// After app.UseAuthentication(), app.UseAuthorization()
app.UseCors();
```

This should be the first task in the phase plan -- without it, nothing else works.

## Sources

### Primary (HIGH confidence)
- Project source code: WretchedWhispers.Api/Program.cs, Endpoints/SessionEndpoints.cs, Services/GameSessionService.cs, Models/*.cs -- actual API contract
- Project tests: WretchedWhispers.Tests/Auth/AuthEndpointTests.cs -- confirmed auth request/response format
- [Next.js Official Docs: Project Structure](https://nextjs.org/docs/app/getting-started/project-structure) -- App Router structure
- [Next.js Official Docs: Installation](https://nextjs.org/docs/app/getting-started/installation) -- create-next-app setup
- [Tailwind CSS v4 Blog Post](https://tailwindcss.com/blog/tailwindcss-v4) -- CSS-first configuration
- [Tailwind CSS: Install with Next.js](https://tailwindcss.com/docs/guides/nextjs) -- setup guide
- [shadcn/ui Tailwind v4 compatibility](https://ui.shadcn.com/docs/tailwind-v4) -- confirmed v4 support

### Secondary (MEDIUM confidence)
- [Andrew Lock: ASP.NET Identity API Endpoints](https://andrewlock.net/exploring-the-dotnet-8-preview-introducing-the-identity-api-endpoints/) -- verified login response format (tokenType, accessToken, expiresIn, refreshToken)
- [@microsoft/fetch-event-source npm](https://www.npmjs.com/package/@microsoft/fetch-event-source) -- SSE with custom headers, POST support
- [MDN: Using Server-Sent Events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events/Using_server-sent_events) -- SSE protocol reference
- [Zustand vs Jotai comparison (multiple 2025-2026 sources)](https://dev.to/hijazi313/state-management-in-2025-when-to-use-context-redux-zustand-or-jotai-2d2k) -- state management landscape

### Tertiary (LOW confidence)
- Mork Borg exact hex color values -- could not access the Design Primer PDF; recommended values (#FFE000, #FF1493) are approximations based on visual reference
- Doom font candidates -- based on community resources, not tested for web rendering quality

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Next.js 15/Tailwind v4/Zustand are well-documented, current, verified via official sources
- Architecture: HIGH - Project structure follows Next.js official recommendations; API contract verified from actual source code
- SSE streaming: HIGH - @microsoft/fetch-event-source is standard for LLM streaming; API SSE format confirmed from GameSessionService source
- Auth integration: HIGH - Exact request/response format confirmed from project test suite
- Pitfalls: HIGH - CORS gap confirmed by code inspection; useCookies=false confirmed by test suite; hydration issues are well-documented
- Visual aesthetic: MEDIUM - Color values are approximations; font choice needs visual evaluation

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (stable stack, 30-day window)
