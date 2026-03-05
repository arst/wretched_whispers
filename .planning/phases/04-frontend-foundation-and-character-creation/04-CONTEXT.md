# Phase 4: Frontend Foundation and Character Creation - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Players open the web app, see the Mork Borg aesthetic, and create a character through a guided narrator conversation. This phase delivers: React/Next.js app shell, authentication screens, session management UI, the chat interface with SSE streaming, and the character creation flow as the first playable experience. Full character sheet sidebar, gameplay actions beyond character creation, and mechanical visibility are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Doom-metal aesthetic
- "Controlled grime" direction — dark base with Mork Borg accents, not full visual chaos
- Background: near-black with subtle paper/parchment grain or noise texture overlay
- Typography: custom doom/distressed display font for headers and key UI elements; clean sans-serif (Inter or similar) for body text and readability
- Color palette: yellow (#FFFF00-ish) as primary for headings, active elements, and CTAs; hot pink/magenta for danger, damage, and death indicators; white for body text
- Grunge accents on borders, dividers, and decorative elements — not overwhelming

### Character creation flow
- Conversational back-and-forth: narrator asks questions ("What is your name, wretch?"), player types responses via the same text input used for gameplay
- Atmospheric splash screen displayed while the GM generates the opening message — mood-setting with title/art/flavor text, transitions to chat when first narrative SSE event arrives
- No live character sheet during creation — minimal inline hints instead. Key reveals (name, stats, equipment) get visual emphasis as styled callouts within the conversation
- Dice rolls and stat assignments rendered as inline styled callouts (e.g., yellow-bordered box showing "d6+d6+d6 = 14 -> Strength +1") using tool_result SSE events
- Full character sheet sidebar is a Phase 5 concern

### App shell and navigation
- Atmosphere-first landing page: game title, flavor text, prominent "Begin" CTA. Session list accessible but secondary (menu or separate page)
- Themed auth screens: login and register pages with the Mork Borg aesthetic (dark background, doom font headers, styled form inputs). Atmosphere starts at first interaction
- Minimal header bar: persistent small header with game title/logo, session name when in one, and back/home button. No sidebar — screen real estate maximized for chat
- Responsive: same single-column layout for desktop and tablet, scales fluidly. No special breakpoints or extra panels for desktop in Phase 4

### Chat interface
- Distinct visual treatment: narrator messages get styled card with different background/border treatment (e.g., dark card with yellow accent border); player messages are simpler (right-aligned or lighter shade)
- Loading/thinking indicator: animated placeholder in the GM message area (animated dots, throbbing icon, or similar) styled in the narrator's visual treatment. Feels like the GM is thinking
- Typewriter streaming: narrator text appears word-by-word as narrative SSE events arrive. Real-time feel is core to the experience
- Themed text input: fixed bottom bar with doom aesthetic styling — styled border, flavor placeholder text ("Speak, wretch..." or "What do you do?"), themed send button. Single line that expands for longer text, Enter or button to submit

### Claude's Discretion
- Specific doom display font selection (blackletter, metal-style, or hand-drawn)
- Exact grunge texture and border treatment details
- Splash screen content and transition animation
- Loading indicator animation style (dots, skull, dripping text, etc.)
- Exact message card styling, spacing, and border treatments
- Token storage strategy (localStorage vs httpOnly cookie for JWT)
- Next.js project structure and routing approach
- Component library choice (if any — Tailwind, shadcn, etc.)
- State management approach for auth and session data
- Error handling UI patterns

</decisions>

<specifics>
## Specific Ideas

- The Mork Borg book is the reference point for the aesthetic — controlled version of its visual energy, not a faithful reproduction
- Narrator should feel like a distinct character, not a generic AI chatbot. The visual treatment of GM messages should reinforce this
- The atmospheric splash screen sets the tone before a single word is generated — first impression matters
- Input placeholder text should feel in-character ("Speak, wretch..." not "Type a message...")
- Tool results (dice, stats) should feel exciting during character creation, not like debug output

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- No frontend code exists — this is greenfield
- API endpoints ready to consume: `/auth` (Identity), `/sessions` (CRUD), `/sessions/{id}/actions` (SSE streaming)
- SSE event types defined: `narrative` (text chunks), `tool_result` (mechanical outcomes), `state_update` (game state deltas), `done`, `error`
- DTOs available: `SessionPreviewDto` (id, name, description, characterName, currentHp, maxHp, status, lastPlayed), `SessionDetailDto`, `ChatMessageDto` (role, content, authorName), `CreateSessionResponse`, `SseEvent`

### Established Patterns
- JWT auth with 60min access tokens, 14-day refresh — frontend needs token management
- Session status derived from domain: `character-creation` (no players), `in-progress` (active campaign), `ended`
- Per-turn SSE: client opens connection on action submit, receives streamed response, connection closes on `done` event
- 409 Conflict on concurrent actions to same session — frontend should handle this gracefully

### Integration Points
- `POST /auth/register` and `POST /auth/login` — Identity API endpoints
- `GET /auth/me` — verify token validity
- `POST /sessions` — create new game session (returns session ID)
- `GET /sessions` — list user's sessions with previews
- `GET /sessions/{id}` — session detail with paginated messages
- `GET /sessions/{id}/messages` — paginated message history
- `POST /sessions/{id}/actions` — submit player action, returns SSE stream
- CORS configuration needed on API for frontend origin

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 04-frontend-foundation-and-character-creation*
*Context gathered: 2026-03-05*
